using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

/// <summary>
/// Les échéances d'un dashboard. Appartenance vérifiée par membre du dashboard, en une requête, avant
/// toute lecture : hors périmètre, tout rend 404, on ne révèle pas qu'une ligne existe. Le statut se
/// calcule à la lecture (EcheanceStatusRules), rien ici n'écrit un statut ni ne touche au bilan.
/// </summary>
[ApiController]
[Route("api/echeances")]
[Authorize]
public class EcheanceController : ApiControllerBase
{
    private readonly AppDbContext _context;

    public EcheanceController(AppDbContext context)
    {
        _context = context;
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private Task<bool> IsMemberAsync(int dashboardId, int userId) =>
        _context.Dashboards.AnyAsync(d => d.Id == dashboardId && d.Members.Any(m => m.UserId == userId));

    /// <summary>La ligne, si elle appartient à un dashboard dont l'appelant est membre. Sinon null.</summary>
    private Task<Echeance?> FindOwnedAsync(int id, int userId) =>
        _context.Echeances
            .Include(e => e.Documents)
            .FirstOrDefaultAsync(e => e.Id == id && e.Dashboard.Members.Any(m => m.UserId == userId));

    private static EcheanceDto Map(Echeance e, DateOnly today) => new()
    {
        Id = e.Id,
        DashboardId = e.DashboardId,
        Label = e.Label,
        DueDate = e.DueDate,
        Amount = e.Amount,
        IsAmountKnown = e.Amount.HasValue,
        Notes = e.Notes,
        Status = EcheanceStatusRules.Of(e, today).ToString(),
        PaidAt = e.PaidAt,
        TransactionId = e.TransactionId,
        DocumentIds = e.Documents.Select(d => d.Id).OrderBy(id => id).ToList(),
        CreatedByUserId = e.CreatedByUserId,
        CreatedAt = e.CreatedAt,
        UpdatedAt = e.UpdatedAt,
    };

    /// <summary>Le statut étant dérivé, son filtre s'applique côté client après projection.</summary>
    [HttpGet]
    public async Task<ActionResult<List<EcheanceDto>>> GetAll(
        [FromQuery] int dashboardId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] EcheanceStatus? status)
    {
        var userId = GetUserId();
        if (!await IsMemberAsync(dashboardId, userId)) return NotFound();

        var query = _context.Echeances
            .Include(e => e.Documents)
            .Where(e => e.DashboardId == dashboardId);
        if (from.HasValue) query = query.Where(e => e.DueDate >= from.Value);
        if (to.HasValue) query = query.Where(e => e.DueDate <= to.Value);

        var today = Today;
        var rows = await query.OrderBy(e => e.DueDate).ThenBy(e => e.Id).ToListAsync();
        var result = rows.Select(e => Map(e, today));
        if (status.HasValue) result = result.Where(dto => dto.Status == status.Value.ToString());

        return Ok(result.ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<EcheanceDto>> GetById(int id)
    {
        var echeance = await FindOwnedAsync(id, GetUserId());
        if (echeance == null) return NotFound();
        return Ok(Map(echeance, Today));
    }

    [HttpPost]
    public async Task<ActionResult<EcheanceDto>> Create(CreateEcheanceDto dto)
    {
        var userId = GetUserId();
        if (!await IsMemberAsync(dto.DashboardId, userId)) return NotFound();

        var now = DateTime.UtcNow;
        var echeance = new Echeance
        {
            DashboardId = dto.DashboardId,
            Label = dto.Label.Trim(),
            DueDate = dto.DueDate,
            Amount = dto.Amount,
            Notes = dto.Notes,
            CreatedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        _context.Echeances.Add(echeance);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = echeance.Id }, Map(echeance, Today));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<EcheanceDto>> Update(int id, UpdateEcheanceDto dto)
    {
        var userId = GetUserId();
        var echeance = await FindOwnedAsync(id, userId);
        if (echeance == null) return NotFound();

        if (dto.TransactionId.HasValue && dto.TransactionId != echeance.TransactionId)
        {
            // La transaction doit vivre sur un compte du dashboard : on ne prouve pas une échéance
            // du Commun avec une ligne du Perso.
            var candidate = await _context.Transactions
                .Where(t => t.Id == dto.TransactionId.Value
                         && t.Account.DashboardAccounts.Any(da => da.DashboardId == echeance.DashboardId))
                .Select(t => new { t.IsProvisional })
                .FirstOrDefaultAsync();
            if (candidate == null) return BadRequest("Transaction introuvable sur les comptes de ce dashboard.");
            // Une provision (salaire attendu, matérialisé en début de mois) n'est pas un paiement.
            if (candidate.IsProvisional) return BadRequest("Une transaction provisionnelle ne prouve pas un paiement.");

            var alreadyProves = await _context.Echeances.AnyAsync(e => e.TransactionId == dto.TransactionId.Value && e.Id != id);
            if (alreadyProves) return Conflict("Cette transaction règle déjà une autre échéance.");
        }

        echeance.Label = dto.Label.Trim();
        echeance.DueDate = dto.DueDate;
        echeance.Amount = dto.Amount;
        echeance.Notes = dto.Notes;
        echeance.TransactionId = dto.TransactionId;
        echeance.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Deux mises à jour concurrentes sur la même transaction : l'index unique tranche.
            return Conflict("Cette transaction règle déjà une autre échéance.");
        }
        return Ok(Map(echeance, Today));
    }

    /// <summary>Marque l'échéance payée à la main, à l'instant. Déjà payée : 409.</summary>
    [HttpPost("{id}/pay")]
    public async Task<ActionResult<EcheanceDto>> Pay(int id)
    {
        var echeance = await FindOwnedAsync(id, GetUserId());
        if (echeance == null) return NotFound();
        if (EcheanceStatusRules.IsPaid(echeance)) return Conflict("Cette échéance est déjà payée.");

        var now = DateTime.UtcNow;
        echeance.PaidAt = now;
        echeance.UpdatedAt = now;
        await _context.SaveChangesAsync();
        return Ok(Map(echeance, Today));
    }

    /// <summary>Annule le paiement, manuel ou prouvé par transaction : l'échéance redevient à payer.</summary>
    [HttpPost("{id}/unpay")]
    public async Task<ActionResult<EcheanceDto>> Unpay(int id)
    {
        var echeance = await FindOwnedAsync(id, GetUserId());
        if (echeance == null) return NotFound();

        echeance.PaidAt = null;
        echeance.TransactionId = null;
        echeance.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok(Map(echeance, Today));
    }

    /// <summary>Supprime l'échéance. Ses documents restent, détachés (FK en SetNull).</summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var echeance = await FindOwnedAsync(id, GetUserId());
        if (echeance == null) return NotFound();

        _context.Echeances.Remove(echeance);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
