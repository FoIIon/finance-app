using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.API.Services.Calendar;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    private readonly HouseholdOptions _household;

    public EcheanceController(AppDbContext context, IOptions<HouseholdOptions> household)
    {
        _context = context;
        _household = household.Value;
    }

    /// <summary>La date du jour dans le fuseau du ménage, jamais l'UTC nu : le Pi tourne en UTC.</summary>
    private DateOnly Today => _household.TodayLocal(DateTime.UtcNow);

    private Task<bool> IsMemberAsync(int dashboardId, int userId) =>
        _context.Dashboards.AnyAsync(d => d.Id == dashboardId && d.Members.Any(m => m.UserId == userId));

    /// <summary>La ligne, si elle appartient à un dashboard dont l'appelant est membre. Sinon null.</summary>
    private Task<Echeance?> FindOwnedAsync(int id, int userId) =>
        _context.Echeances
            .Include(e => e.Documents)
            .Include(e => e.Transaction)
            .FirstOrDefaultAsync(e => e.Id == id && e.Dashboard.Members.Any(m => m.UserId == userId));

    private const string InvalidCommunicationMessage = "Communication structurée invalide, vérifiez les douze chiffres.";
    private const string InvalidIbanMessage = "IBAN du bénéficiaire invalide, quinze à trente-quatre lettres et chiffres.";

    /// <summary>
    /// Les deux clés du rapprochement, normalisées depuis la saisie brute. Une communication qui échoue au
    /// contrôle 97 ou un IBAN hors forme rendent un message, jamais la valeur saisie.
    /// </summary>
    private static (string? Iban, string? Communication, string? Error) NormalizeKeys(string? rawIban, string? rawCommunication)
    {
        string? iban = null;
        if (!string.IsNullOrWhiteSpace(rawIban))
        {
            iban = GoCardlessTransactionFields.Normalize(rawIban);
            // La forme seulement : la banque fait foi sur la validité du compte.
            if (iban.Length is < 15 or > 34 || !iban.All(char.IsAsciiLetterOrDigit)) return (null, null, InvalidIbanMessage);
        }

        string? communication = null;
        if (!string.IsNullOrWhiteSpace(rawCommunication))
        {
            communication = StructuredCommunication.Normalize(rawCommunication);
            if (communication == null) return (null, null, InvalidCommunicationMessage);
        }

        return (iban, communication, null);
    }

    private EcheancePaymentDto? PaymentOf(Echeance e)
    {
        if (e.Transaction == null) return null;
        var t = e.Transaction;
        return new EcheancePaymentDto
        {
            TransactionId = t.Id,
            // Relue de SQLite en Kind Unspecified, la date d'une transaction est UTC : le jour du ménage en découle.
            Date = _household.TodayLocal(t.Date),
            Amount = t.Amount,
            Description = t.Description.Length > 200 ? t.Description[..200] : t.Description,
            CounterpartyName = t.CounterpartyName,
        };
    }

    private EcheanceDto Map(Echeance e, DateOnly today) => new()
    {
        Id = e.Id,
        DashboardId = e.DashboardId,
        Label = e.Label,
        DueDate = e.DueDate,
        Amount = e.Amount,
        IsAmountKnown = e.Amount.HasValue,
        Notes = e.Notes,
        Status = EcheanceStatusRules.Of(e, today).ToString(),
        // Relu de SQLite, un DateTime sort en Kind Unspecified et se sérialise sans « Z » : le navigateur
        // le lirait en heure locale et « Payée le » glisserait d'un jour entre 22 h et minuit. Même procédé
        // qu'AgendaCalendarStatus.From pour LastSyncAt.
        PaidAt = e.PaidAt.HasValue ? DateTime.SpecifyKind(e.PaidAt.Value, DateTimeKind.Utc) : null,
        TransactionId = e.TransactionId,
        CounterpartyIban = e.CounterpartyIban,
        StructuredCommunication = e.StructuredCommunication,
        MatchedAt = e.MatchedAt.HasValue ? DateTime.SpecifyKind(e.MatchedAt.Value, DateTimeKind.Utc) : null,
        AutoMatchRefusedAt = e.AutoMatchRefusedAt.HasValue ? DateTime.SpecifyKind(e.AutoMatchRefusedAt.Value, DateTimeKind.Utc) : null,
        Payment = PaymentOf(e),
        DocumentIds = e.Documents.Select(d => d.Id).OrderBy(id => id).ToList(),
        CreatedByUserId = e.CreatedByUserId,
        CreatedAt = DateTime.SpecifyKind(e.CreatedAt, DateTimeKind.Utc),
        UpdatedAt = DateTime.SpecifyKind(e.UpdatedAt, DateTimeKind.Utc),
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

        // Sans la transaction : la liste ne sert qu'aux libellés et au statut (dérivé des colonnes), Payment
        // y reste null. La fiche (GetById) charge la transaction pour le détail du virement.
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

        var (iban, communication, keyError) = NormalizeKeys(dto.CounterpartyIban, dto.StructuredCommunication);
        if (keyError != null) return BadRequest(keyError);

        var now = DateTime.UtcNow;
        var echeance = new Echeance
        {
            DashboardId = dto.DashboardId,
            Label = dto.Label.Trim(),
            DueDate = dto.DueDate,
            Amount = dto.Amount,
            Notes = dto.Notes,
            CounterpartyIban = iban,
            StructuredCommunication = communication,
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

        var (iban, communication, keyError) = NormalizeKeys(dto.CounterpartyIban, dto.StructuredCommunication);
        if (keyError != null) return BadRequest(keyError);

        // « Modifier » ne touche jamais au lien de paiement : PaidAt, TransactionId et MatchedAt restent tels
        // quels, seuls Pay et Unpay les changent. Une fiche ouverte avant une passe de rapprochement et
        // enregistrée après garde donc le lien que la passe a posé. Une clé corrigée lève le refus de
        // rapprochement : l'utilisateur a changé ce sur quoi on devinait.
        if (iban != echeance.CounterpartyIban || communication != echeance.StructuredCommunication)
            echeance.AutoMatchRefusedAt = null;

        echeance.Label = dto.Label.Trim();
        echeance.DueDate = dto.DueDate;
        echeance.Amount = dto.Amount;
        echeance.Notes = dto.Notes;
        echeance.CounterpartyIban = iban;
        echeance.StructuredCommunication = communication;
        echeance.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
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

    /// <summary>
    /// Annule le paiement, manuel ou prouvé par transaction : l'échéance redevient à payer. Si c'est le
    /// rapprocheur qui avait lié la transaction, le geste vaut « arrête de deviner pour celle-ci » : le
    /// rapprocheur l'ignore jusqu'à ce qu'une clé soit corrigée. Un lien manuel qu'on défait n'est pas un refus.
    /// </summary>
    [HttpPost("{id}/unpay")]
    public async Task<ActionResult<EcheanceDto>> Unpay(int id)
    {
        var echeance = await FindOwnedAsync(id, GetUserId());
        if (echeance == null) return NotFound();

        var now = DateTime.UtcNow;
        if (echeance.MatchedAt.HasValue && echeance.TransactionId.HasValue)
            echeance.AutoMatchRefusedAt = now;
        echeance.PaidAt = null;
        echeance.TransactionId = null;
        echeance.MatchedAt = null;
        echeance.Transaction = null;
        echeance.UpdatedAt = now;
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
