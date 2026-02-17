using System.Security.Claims;
using System.Text.Json;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/banking")]
[Authorize]
public class BankingController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly GoCardlessClient _goCardless;
    private readonly IConfiguration _configuration;

    public BankingController(AppDbContext context, GoCardlessClient goCardless, IConfiguration configuration)
    {
        _context = context;
        _goCardless = goCardless;
        _configuration = configuration;
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId))
            throw new InvalidOperationException("Claim NameIdentifier absent ou invalide.");
        return userId;
    }

    [HttpGet("institutions")]
    public async Task<ActionResult<List<InstitutionDto>>> GetInstitutions([FromQuery] string country = "FR")
    {
        var result = await _goCardless.GetInstitutionsAsync(country);
        var institutions = new List<InstitutionDto>();

        foreach (var item in result.EnumerateArray())
        {
            institutions.Add(new InstitutionDto
            {
                Id = item.GetProperty("id").GetString() ?? "",
                Name = item.GetProperty("name").GetString() ?? "",
                Logo = item.GetProperty("logo").GetString() ?? "",
                Countries = item.GetProperty("countries").EnumerateArray()
                    .Select(c => c.GetString() ?? "").ToList()
            });
        }

        return Ok(institutions);
    }

    [HttpPost("connect")]
    public async Task<ActionResult<ConnectBankResponse>> Connect(ConnectBankRequest dto)
    {
        var userId = GetUserId();
        var reference = Guid.NewGuid().ToString();

        // Créer l'accord avec la banque
        var agreement = await _goCardless.CreateAgreementAsync(dto.InstitutionId);
        var agreementId = agreement.GetProperty("id").GetString()!;

        // URL de redirection vers le frontend
        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:5173";
        var redirectUrl = $"{frontendUrl}/bank?ref={reference}";

        // Créer la réquisition
        var requisition = await _goCardless.CreateRequisitionAsync(
            dto.InstitutionId, agreementId, redirectUrl, reference);

        var requisitionId = requisition.GetProperty("id").GetString()!;
        var authUrl = requisition.GetProperty("link").GetString()!;

        // Sauvegarder la connexion bancaire
        var connection = new BankConnection
        {
            UserId = userId,
            InstitutionId = dto.InstitutionId,
            InstitutionName = dto.InstitutionName,
            InstitutionLogo = dto.InstitutionLogo,
            RequisitionId = requisitionId,
            Reference = reference,
            Status = BankConnectionStatus.Linked
        };

        _context.BankConnections.Add(connection);
        await _context.SaveChangesAsync();

        return Ok(new ConnectBankResponse { AuthorizationUrl = authUrl });
    }

    [HttpGet("callback")]
    public async Task<ActionResult<BankConnectionDto>> Callback([FromQuery] string @ref)
    {
        var userId = GetUserId();

        // Trouver la connexion via la référence unique
        var connection = await _context.BankConnections
            .Include(bc => bc.BankAccounts)
            .FirstOrDefaultAsync(bc => bc.UserId == userId && bc.Reference == @ref);

        if (connection == null)
            return NotFound("Connexion bancaire introuvable.");

        // Vérifier le statut de la réquisition
        var requisition = await _goCardless.GetRequisitionAsync(connection.RequisitionId);
        var status = requisition.GetProperty("status").GetString();

        if (status != "LN")
        {
            connection.Status = BankConnectionStatus.Error;
            await _context.SaveChangesAsync();
            return BadRequest("La liaison bancaire a échoué.");
        }

        // Récupérer les comptes liés
        var accounts = requisition.GetProperty("accounts");
        foreach (var accountId in accounts.EnumerateArray())
        {
            var externalId = accountId.GetString()!;

            // Éviter les doublons
            if (connection.BankAccounts.Any(ba => ba.ExternalAccountId == externalId))
                continue;

            try
            {
                var details = await _goCardless.GetAccountDetailsAsync(externalId);
                var account = details.GetProperty("account");

                connection.BankAccounts.Add(new BankAccount
                {
                    ExternalAccountId = externalId,
                    Iban = account.TryGetProperty("iban", out var iban) ? iban.GetString() ?? "" : "",
                    OwnerName = account.TryGetProperty("ownerName", out var owner) ? owner.GetString() ?? "" : "",
                    AccountName = account.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    Currency = account.TryGetProperty("currency", out var currency) ? currency.GetString() ?? "" : ""
                });
            }
            catch
            {
                // Ignorer les comptes inaccessibles
            }
        }

        await _context.SaveChangesAsync();
        return Ok(MapConnectionToDto(connection));
    }

    [HttpGet("connections")]
    public async Task<ActionResult<List<BankConnectionDto>>> GetConnections()
    {
        var userId = GetUserId();
        var connections = await _context.BankConnections
            .Include(bc => bc.BankAccounts)
            .Where(bc => bc.UserId == userId)
            .ToListAsync();

        return Ok(connections.Select(MapConnectionToDto).ToList());
    }

    [HttpDelete("connections/{id}")]
    public async Task<ActionResult> DeleteConnection(int id)
    {
        var userId = GetUserId();
        var connection = await _context.BankConnections
            .Include(bc => bc.BankAccounts)
            .FirstOrDefaultAsync(bc => bc.Id == id && bc.UserId == userId);

        if (connection == null) return NotFound();

        _context.BankConnections.Remove(connection);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("connections/{id}/sync")]
    public async Task<ActionResult> SyncConnection(int id, [FromServices] BankSyncService syncService)
    {
        var userId = GetUserId();
        var connection = await _context.BankConnections
            .FirstOrDefaultAsync(bc => bc.Id == id && bc.UserId == userId);

        if (connection == null) return NotFound();

        try
        {
            await syncService.SyncConnectionAsync(connection.Id);
            return Ok();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            return StatusCode(429, "Trop de requêtes vers la banque. Réessayez dans quelques minutes.");
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, $"Erreur de communication avec la banque : {ex.Message}");
        }
    }

    [HttpGet("accounts")]
    public async Task<ActionResult<List<BankAccountDto>>> GetAccounts()
    {
        var userId = GetUserId();
        var accounts = await _context.BankAccounts
            .Where(ba => ba.BankConnection.UserId == userId)
            .ToListAsync();

        return Ok(accounts.Select(MapAccountToDto).ToList());
    }

    [HttpPatch("accounts/{id}")]
    public async Task<ActionResult<BankAccountDto>> UpdateAccount(int id, UpdateBankAccountDto dto)
    {
        var userId = GetUserId();
        var account = await _context.BankAccounts
            .Include(ba => ba.BankConnection)
            .FirstOrDefaultAsync(ba => ba.Id == id && ba.BankConnection.UserId == userId);

        if (account == null) return NotFound();

        account.IsActive = dto.IsActive;
        await _context.SaveChangesAsync();

        return Ok(MapAccountToDto(account));
    }

    private static BankConnectionDto MapConnectionToDto(BankConnection connection) => new()
    {
        Id = connection.Id,
        InstitutionId = connection.InstitutionId,
        InstitutionName = connection.InstitutionName,
        InstitutionLogo = connection.InstitutionLogo,
        Status = connection.Status,
        CreatedAt = connection.CreatedAt,
        LastSyncAt = connection.LastSyncAt,
        Accounts = connection.BankAccounts.Select(MapAccountToDto).ToList()
    };

    private static BankAccountDto MapAccountToDto(BankAccount account) => new()
    {
        Id = account.Id,
        ExternalAccountId = account.ExternalAccountId,
        Iban = account.Iban,
        OwnerName = account.OwnerName,
        AccountName = account.AccountName,
        Currency = account.Currency,
        IsActive = account.IsActive
    };
}
