using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly TradeRepublicAuthStore _trAuthStore;

    public BankingController(AppDbContext context, GoCardlessClient goCardless, IConfiguration configuration, TradeRepublicAuthStore trAuthStore)
    {
        _context = context;
        _goCardless = goCardless;
        _configuration = configuration;
        _trAuthStore = trAuthStore;
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId))
            throw new InvalidOperationException("Claim NameIdentifier absent ou invalide.");
        return userId;
    }

    [HttpGet("institutions")]
    public async Task<ActionResult<List<InstitutionDto>>> GetInstitutions([FromQuery] string country = "BE")
    {
        // Restreindre aux banques belges uniquement (Trade Republic géré séparément)
        country = "BE";
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

    /// <summary>
    /// Régénère un lien d'authentification GoCardless pour une connexion existante (Error/Expired).
    /// Préserve les BankAccounts (matchés par ExternalAccountId au callback) et donc les transactions.
    /// </summary>
    [HttpPost("connections/{id}/reconnect")]
    public async Task<ActionResult<ConnectBankResponse>> ReconnectConnection(int id)
    {
        var userId = GetUserId();
        var connection = await _context.BankConnections
            .FirstOrDefaultAsync(bc => bc.Id == id && bc.UserId == userId);
        if (connection == null) return NotFound();

        var reference = Guid.NewGuid().ToString();
        var agreement = await _goCardless.CreateAgreementAsync(connection.InstitutionId);
        var agreementId = agreement.GetProperty("id").GetString()!;

        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:5173";
        var redirectUrl = $"{frontendUrl}/bank?ref={reference}";

        var requisition = await _goCardless.CreateRequisitionAsync(
            connection.InstitutionId, agreementId, redirectUrl, reference);

        connection.RequisitionId = requisition.GetProperty("id").GetString()!;
        connection.Reference = reference;
        connection.Status = BankConnectionStatus.Linked;
        await _context.SaveChangesAsync();

        return Ok(new ConnectBankResponse
        {
            AuthorizationUrl = requisition.GetProperty("link").GetString()!
        });
    }

    [HttpDelete("connections/{id}")]
    public async Task<ActionResult> DeleteConnection(int id)
    {
        var userId = GetUserId();
        var connection = await _context.BankConnections
            .Include(bc => bc.BankAccounts)
            .FirstOrDefaultAsync(bc => bc.Id == id && bc.UserId == userId);

        if (connection == null) return NotFound();

        // Nullifier BankAccountId sur les transactions liées avant la suppression en cascade
        var bankAccountIds = connection.BankAccounts.Select(ba => ba.Id).ToList();
        if (bankAccountIds.Any())
        {
            await _context.Transactions
                .Where(t => t.BankAccountId != null && bankAccountIds.Contains(t.BankAccountId!.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BankAccountId, (int?)null));
        }

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

    [HttpPost("traderepublic/login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<TradeRepublicLoginResponse>> TradeRepublicLogin(
        TradeRepublicLoginRequest dto,
        [FromServices] TradeRepublicClient trClient)
    {
        var userId = GetUserId();

        // Chercher une connexion TR existante à réutiliser (évite de créer un doublon)
        var existingConnection = await _context.BankConnections
            .FirstOrDefaultAsync(bc => bc.UserId == userId && bc.Provider == "TradeRepublic"
                && (bc.Status == BankConnectionStatus.Linked || bc.Status == BankConnectionStatus.Error));

        // Supprimer les connexions TR en attente de 2FA périmées
        var staleConnections = await _context.BankConnections
            .Include(bc => bc.BankAccounts)
            .Where(bc => bc.UserId == userId && bc.Provider == "TradeRepublic" && bc.Status == BankConnectionStatus.PendingTwoFactor)
            .ToListAsync();

        // Nullifier BankAccountId sur les transactions liées avant la suppression en cascade
        var staleBankAccountIds = staleConnections.SelectMany(c => c.BankAccounts.Select(ba => ba.Id)).ToList();
        if (staleBankAccountIds.Any())
        {
            await _context.Transactions
                .Where(t => t.BankAccountId != null && staleBankAccountIds.Contains(t.BankAccountId!.Value))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BankAccountId, (int?)null));
        }

        _context.BankConnections.RemoveRange(staleConnections);

        // Initier le login via HTTP (déclenche l'envoi d'un SMS)
        string processId;
        try
        {
            processId = await trClient.InitiateLoginAsync(dto.PhoneNumber, dto.Pin);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        // Réutiliser la connexion existante si possible, sinon en créer une nouvelle
        BankConnection connection;
        if (existingConnection != null)
        {
            existingConnection.Status = BankConnectionStatus.PendingTwoFactor;
            connection = existingConnection;
        }
        else
        {
            connection = new BankConnection
            {
                UserId = userId,
                InstitutionId = "trade-republic",
                InstitutionName = "Trade Republic",
                InstitutionLogo = "",
                Provider = "TradeRepublic",
                Status = BankConnectionStatus.PendingTwoFactor
            };
            _context.BankConnections.Add(connection);
        }

        await _context.SaveChangesAsync();

        // Stocker le processId pour la vérification du code SMS
        _trAuthStore.Store(connection.Id, new PendingLogin
        {
            ProcessId = processId,
            UserId = userId
        });

        return Ok(new TradeRepublicLoginResponse { ConnectionId = connection.Id });
    }

    [HttpPost("traderepublic/verify")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult> TradeRepublicVerify(
        TradeRepublicVerifyRequest dto,
        [FromServices] TradeRepublicClient trClient)
    {
        var userId = GetUserId();

        var connection = await _context.BankConnections
            .FirstOrDefaultAsync(bc => bc.Id == dto.ConnectionId && bc.UserId == userId);

        if (connection == null) return NotFound();

        var pending = _trAuthStore.Get(dto.ConnectionId);
        if (pending == null)
            return BadRequest("Session expirée. Veuillez recommencer la connexion.");

        try
        {
            var (sessionToken, refreshToken, deviceToken) = await trClient.ConfirmTwoFactorAsync(pending.ProcessId, dto.Code);

            // Stocker les tokens chiffrés — jamais en clair
            if (!string.IsNullOrEmpty(sessionToken))
                connection.EncryptedSessionToken = trClient.EncryptToken(sessionToken);
            connection.EncryptedRefreshToken = trClient.EncryptToken(refreshToken);
            if (!string.IsNullOrEmpty(deviceToken))
                connection.EncryptedDeviceToken = trClient.EncryptToken(deviceToken);
            connection.Status = BankConnectionStatus.Linked;
            await _context.SaveChangesAsync();

            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
        finally
        {
            _trAuthStore.Remove(dto.ConnectionId);
        }
    }

    // === Comptes manuels (non connectables via Open Banking) ===

    [HttpGet("manual-accounts")]
    public async Task<ActionResult<List<ManualAccountDto>>> GetManualAccounts()
    {
        var userId = GetUserId();
        var accounts = await _context.BankAccounts
            .Include(ba => ba.SourceBankAccount)
            .Include(ba => ba.IncrementCategory)
            .Where(ba => ba.IsManual && ba.UserId == userId && ba.IsActive)
            .ToListAsync();

        return Ok(accounts.Select(a => new ManualAccountDto
        {
            Id = a.Id,
            Name = a.AccountName,
            Iban = a.Iban,
            InitialBalance = a.InitialBalance ?? 0,
            InitialBalanceDate = a.InitialBalanceDate ?? DateTime.UtcNow,
            SourceBankAccountId = a.SourceBankAccountId,
            SourceBankAccountName = a.SourceBankAccount?.AccountName ?? a.SourceBankAccount?.Iban,
            IncrementCategoryId = a.IncrementCategoryId,
            IncrementCategoryName = a.IncrementCategory?.Name,
        }).ToList());
    }

    [HttpPost("manual-accounts")]
    public async Task<ActionResult<ManualAccountDto>> CreateManualAccount(CreateManualAccountDto dto)
    {
        var userId = GetUserId();

        // Trouver / créer la BankConnection "Manual" du user (placeholder requis par le schéma)
        var manualConn = await _context.BankConnections.FirstOrDefaultAsync(bc => bc.UserId == userId && bc.Provider == "Manual");
        if (manualConn == null)
        {
            manualConn = new BankConnection
            {
                UserId = userId,
                Provider = "Manual",
                InstitutionId = "MANUAL",
                InstitutionName = "Comptes manuels",
                InstitutionLogo = "",
                RequisitionId = "manual-" + Guid.NewGuid(),
                Reference = Guid.NewGuid().ToString(),
                Status = BankConnectionStatus.Linked,
            };
            _context.BankConnections.Add(manualConn);
            await _context.SaveChangesAsync();
        }

        // Vérifier que SourceBankAccount appartient au user
        if (dto.SourceBankAccountId.HasValue)
        {
            var src = await _context.BankAccounts
                .Include(b => b.BankConnection)
                .FirstOrDefaultAsync(b => b.Id == dto.SourceBankAccountId.Value);
            if (src == null || (src.BankConnection?.UserId != userId && src.UserId != userId))
                return BadRequest("Compte source invalide.");
        }

        var account = new BankAccount
        {
            BankConnectionId = manualConn.Id,
            IsManual = true,
            UserId = userId,
            AccountName = dto.Name,
            Iban = dto.Iban ?? string.Empty,
            ExternalAccountId = "manual-" + Guid.NewGuid(),
            Currency = dto.Currency ?? "EUR",
            IsActive = true,
            InitialBalance = dto.InitialBalance,
            InitialBalanceDate = dto.InitialBalanceDate ?? DateTime.UtcNow,
            SourceBankAccountId = dto.SourceBankAccountId,
            IncrementCategoryId = dto.IncrementCategoryId,
        };

        _context.BankAccounts.Add(account);
        await _context.SaveChangesAsync();

        return Ok(new ManualAccountDto
        {
            Id = account.Id,
            Name = account.AccountName,
            Iban = account.Iban,
            InitialBalance = account.InitialBalance ?? 0,
            InitialBalanceDate = account.InitialBalanceDate ?? DateTime.UtcNow,
            SourceBankAccountId = account.SourceBankAccountId,
            IncrementCategoryId = account.IncrementCategoryId,
        });
    }

    [HttpPut("manual-accounts/{id}")]
    public async Task<ActionResult<ManualAccountDto>> UpdateManualAccount(int id, UpdateManualAccountDto dto)
    {
        var userId = GetUserId();
        var account = await _context.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.IsManual && b.UserId == userId);
        if (account == null) return NotFound();

        if (dto.Name != null) account.AccountName = dto.Name;
        if (dto.Iban != null) account.Iban = dto.Iban;
        if (dto.InitialBalance.HasValue) account.InitialBalance = dto.InitialBalance.Value;
        if (dto.InitialBalanceDate.HasValue) account.InitialBalanceDate = dto.InitialBalanceDate.Value;
        if (dto.SourceBankAccountId.HasValue) account.SourceBankAccountId = dto.SourceBankAccountId;
        if (dto.IncrementCategoryId.HasValue) account.IncrementCategoryId = dto.IncrementCategoryId;

        await _context.SaveChangesAsync();

        return Ok(new ManualAccountDto
        {
            Id = account.Id,
            Name = account.AccountName,
            Iban = account.Iban,
            InitialBalance = account.InitialBalance ?? 0,
            InitialBalanceDate = account.InitialBalanceDate ?? DateTime.UtcNow,
            SourceBankAccountId = account.SourceBankAccountId,
            IncrementCategoryId = account.IncrementCategoryId,
        });
    }

    [HttpDelete("manual-accounts/{id}")]
    public async Task<ActionResult> DeleteManualAccount(int id)
    {
        var userId = GetUserId();
        var account = await _context.BankAccounts.FirstOrDefaultAsync(b => b.Id == id && b.IsManual && b.UserId == userId);
        if (account == null) return NotFound();

        _context.BankAccounts.Remove(account);
        await _context.SaveChangesAsync();
        return NoContent();
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
