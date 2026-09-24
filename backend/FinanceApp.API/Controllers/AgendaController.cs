using FinanceApp.API.Data;
using FinanceApp.API.Services.Calendar;
using FinanceApp.API.Services.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Controllers;

/// <summary>
/// GET api/agenda : calendrier, échéances et récurrentes actives d'un dashboard fondus en une projection
/// par jour. Le contrôleur charge et projette (AgendaProjectors), AgendaBuilder applique les règles. Toute
/// date est dans le fuseau du ménage. Rien ici ne touche au bilan ni ne persiste un statut.
/// </summary>
[ApiController]
[Route("api/agenda")]
[Authorize]
public class AgendaController : ApiControllerBase
{
    /// <summary>Recul chargé pour les échéances payées : au-delà, seules les impayées comptent (retards portés).</summary>
    private const int PaidEcheancesMonthsBack = 3;

    private readonly AppDbContext _context;
    private readonly HouseholdOptions _household;
    private readonly TimeProvider _clock;

    public AgendaController(AppDbContext context, IOptions<HouseholdOptions> household, TimeProvider clock)
    {
        _context = context;
        _household = household.Value;
        _clock = clock;
    }

    private Task<bool> IsMemberAsync(int dashboardId, int userId) =>
        _context.Dashboards.AnyAsync(d => d.Id == dashboardId && d.Members.Any(m => m.UserId == userId));

    /// <param name="view">week (défaut) : sept jours glissants depuis anchor. month : le mois civil qui contient anchor.</param>
    /// <param name="anchor">Défaut : aujourd'hui dans le fuseau du ménage.</param>
    [HttpGet]
    public async Task<ActionResult<AgendaResult>> Get(
        [FromQuery] int dashboardId,
        [FromQuery] string? view,
        [FromQuery] DateOnly? anchor,
        CancellationToken cancellationToken)
    {
        if (!await IsMemberAsync(dashboardId, GetUserId())) return NotFound();

        AgendaView agendaView;
        switch (view?.Trim().ToLowerInvariant())
        {
            case null or "" or "week": agendaView = AgendaView.Week; break;
            case "month": agendaView = AgendaView.Month; break;
            default: return BadRequest("view doit valoir week ou month.");
        }

        var today = _household.TodayLocal(_clock.GetUtcNow().UtcDateTime);
        var pivot = anchor ?? today;
        DateOnly from, to;
        if (agendaView == AgendaView.Week)
        {
            from = pivot;
            to = pivot.AddDays(6);
        }
        else
        {
            from = new DateOnly(pivot.Year, pivot.Month, 1);
            to = from.AddMonths(1).AddDays(-1);
        }

        // Les items chargés couvrent la fenêtre affichée et l'à venir, plus le passé nécessaire aux règles
        // (retards portés, détection des occurrences manquantes).
        var upTo = Max(to, today.AddDays(AgendaBuilder.UpcomingDays - 1));
        var low = Min(from, today);
        var paidFloor = low.AddMonths(-PaidEcheancesMonthsBack);

        var occurrences = await _context.CalendarOccurrences.AsNoTracking()
            .Where(o => o.DashboardId == dashboardId && o.LocalDate <= upTo)
            .ToListAsync(cancellationToken);
        var echeances = await _context.Echeances.AsNoTracking()
            .Where(e => e.DashboardId == dashboardId && e.DueDate <= upTo
                     && (e.DueDate >= paidFloor || (e.PaidAt == null && e.TransactionId == null)))
            .ToListAsync(cancellationToken);
        var recurrings = await _context.RecurringTransactions.AsNoTracking()
            .Where(r => r.DashboardId == dashboardId && r.IsActive)
            .ToListAsync(cancellationToken);
        var source = await _context.CalendarSources.AsNoTracking()
            .FirstOrDefaultAsync(s => s.DashboardId == dashboardId, cancellationToken);
        var candidates = recurrings.Count == 0
            ? new List<SettlementCandidate>()
            : await LoadSettlementCandidatesAsync(dashboardId, low, upTo, cancellationToken);

        var items = AgendaProjectors.FromCalendar(occurrences)
            .Concat(AgendaProjectors.FromEcheances(echeances, today))
            .Concat(AgendaProjectors.FromRecurring(recurrings, low, upTo, candidates))
            .ToList();

        var result = AgendaBuilder.Build(from, to, today, agendaView, items);
        result.TimeZone = _household.TimeZone;
        result.Calendar = AgendaCalendarStatus.From(source);
        return Ok(result);
    }

    /// <summary>
    /// Les transactions qui peuvent régler une occurrence de routine : comptes du dashboard, du premier jour du
    /// mois de <paramref name="low"/> au dernier jour du mois de <paramref name="upTo"/>, jamais provisionnelles,
    /// jamais déjà la preuve d'une échéance (une transaction ne prouve qu'une chose). Lecture seule, rien n'est
    /// suivi. La fenêtre SQL compare des instants UTC avec un jour de marge de chaque côté, la date du candidat
    /// est ramenée au jour du ménage et c'est RecurringSettlement qui borne au mois calendaire.
    /// </summary>
    private async Task<List<SettlementCandidate>> LoadSettlementCandidatesAsync(int dashboardId, DateOnly low, DateOnly upTo, CancellationToken cancellationToken)
    {
        var accountIds = await _context.DashboardAccounts.AsNoTracking()
            .Where(da => da.DashboardId == dashboardId)
            .Select(da => da.AccountId)
            .ToListAsync(cancellationToken);
        if (accountIds.Count == 0) return new List<SettlementCandidate>();

        var monthStart = new DateOnly(low.Year, low.Month, 1);
        var monthEnd = new DateOnly(upTo.Year, upTo.Month, 1).AddMonths(1).AddDays(-1);
        var from = monthStart.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var toExclusive = monthEnd.AddDays(2).ToDateTime(TimeOnly.MinValue);

        var rows = await _context.Transactions.AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) && !t.IsProvisional)
            .Where(t => t.Date >= from && t.Date < toExclusive)
            .Where(t => !_context.Echeances.Any(e => e.TransactionId == t.Id))
            .Select(t => new { t.Id, t.Date, t.Amount, t.Type, t.Description, t.CounterpartyName, t.RecurringTransactionId })
            .ToListAsync(cancellationToken);

        return rows
            .Select(t => new SettlementCandidate(t.Id, _household.TodayLocal(t.Date), t.Amount, t.Type, t.Description, t.CounterpartyName, t.RecurringTransactionId))
            .ToList();
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;
    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
