using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Calendar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services.Reporting;

public enum RecurringLinkOutcome
{
    Ok,
    NotFound,
    Conflict
}

/// <summary>Le résultat d'un lien : l'item d'Agenda recalculé quand tout va bien, sinon la raison et un message sans donnée du ménage.</summary>
public sealed record RecurringLinkResult(RecurringLinkOutcome Outcome, string? Message, AgendaItem? Item)
{
    public static readonly RecurringLinkResult NotFound = new(RecurringLinkOutcome.NotFound, null, null);
    public static RecurringLinkResult Conflict(string message) => new(RecurringLinkOutcome.Conflict, message, null);
    public static RecurringLinkResult Ok(AgendaItem? item) => new(RecurringLinkOutcome.Ok, null, item);
}

/// <summary>
/// Le geste manuel de la routine : quand RecurringSettlement ne trouve rien, désigner la transaction qui règle
/// une récurrente, ou défaire un lien qui trompe. Ce qu'il écrit, et rien d'autre : <c>Transaction.RecurringTransactionId</c>,
/// une colonne, une ligne. Le lien sert ensuite la règle 1 du matcher, ce mois-ci et les suivants pour le
/// provisionnement. Le périmètre est toujours celui du dashboard : une récurrente d'un autre dashboard ou une
/// transaction hors de ses comptes n'existent pas pour l'appelant (404, jamais 403). Le contrôleur a déjà vérifié
/// que l'appelant est membre.
/// </summary>
public class RecurringLinkService
{
    /// <summary>Au-delà, la liste des candidats ne dit plus rien : les vingt plus récentes du mois suffisent à retrouver un paiement.</summary>
    public const int MaxCandidates = 20;

    /// <summary>
    /// Une récurrente ProvisionAtMonthStart n'accepte ni lien ni délien manuels : ProvisionService estime la
    /// provision suivante par la moyenne des trois dernières réelles estampillées (12 € liés au salaire feraient
    /// tomber la provision d'octobre) et ne réconcilie que sur le même compte et la même catégorie, un réel lié
    /// ailleurs laisserait la provision et le réel côte à côte dans le bilan.
    /// </summary>
    public const string ProvisionedMessage = "Cette récurrente est provisionnée, son rapprochement passe par la provision.";

    private readonly AppDbContext _context;
    private readonly HouseholdOptions _household;

    public RecurringLinkService(AppDbContext context, IOptions<HouseholdOptions> household)
    {
        _context = context;
        _household = household.Value;
    }

    /// <summary>
    /// Les transactions du mois qu'on peut désigner : comptes du dashboard, même sens que la récurrente, jamais
    /// provisionnelles, ni preuve d'une échéance, ni liées à une autre récurrente. Les <see cref="MaxCandidates"/>
    /// plus récentes, plus celles déjà liées à cette récurrente et celle qui règle l'occurrence du mois si elles
    /// n'y sont pas : la fiche « réglée » les cherche par identifiant. Null si la récurrente n'est pas du dashboard.
    /// </summary>
    public async Task<List<RecurringCandidateDto>?> CandidatesAsync(int dashboardId, int recurringId, int year, int month, CancellationToken ct)
    {
        var recurring = await FindRecurringAsync(dashboardId, recurringId, ct);
        if (recurring == null) return null;

        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var all = await LoadMonthCandidatesAsync(dashboardId, recurring, monthStart, monthEnd, ct);

        var settled = AgendaProjectors.FromRecurring(new[] { recurring }, monthStart, monthEnd, all)
            .Where(i => i.TransactionId.HasValue)
            .Select(i => i.TransactionId!.Value)
            .ToHashSet();

        var recent = all.OrderByDescending(c => c.Date).ThenByDescending(c => c.Id).Take(MaxCandidates);
        var kept = all.Where(c => c.RecurringTransactionId == recurringId || settled.Contains(c.Id));

        return recent.Concat(kept)
            .DistinctBy(c => c.Id)
            .OrderByDescending(c => c.Date).ThenByDescending(c => c.Id)
            .Select(c => new RecurringCandidateDto
            {
                Id = c.Id,
                Date = c.Date,
                Amount = Math.Abs(c.Amount),
                Description = c.Description,
                CounterpartyName = c.CounterpartyName,
                LinkedToThisRecurring = c.RecurringTransactionId == recurringId,
            })
            .ToList();
    }

    /// <summary>
    /// Pose le lien. 404 hors périmètre (récurrente, transaction hors des comptes du dashboard). 409 si la
    /// récurrente est provisionnée (voir <see cref="ProvisionedMessage"/>), si la transaction règle déjà autre
    /// chose (autre récurrente, échéance), si elle est provisionnelle (elle est au provisionnement, pas au ménage)
    /// ou si elle n'est pas du sens de la récurrente. Rend l'item d'Agenda recalculé pour l'occurrence du mois de
    /// la transaction, null si la récurrente n'a pas d'occurrence ce mois-là.
    /// </summary>
    public async Task<RecurringLinkResult> LinkAsync(int dashboardId, int recurringId, int transactionId, CancellationToken ct)
    {
        var recurring = await FindRecurringAsync(dashboardId, recurringId, ct);
        if (recurring == null) return RecurringLinkResult.NotFound;
        if (recurring.ProvisionAtMonthStart) return RecurringLinkResult.Conflict(ProvisionedMessage);

        var accountIds = await AccountIdsAsync(dashboardId, ct);
        var tx = await _context.Transactions.FirstOrDefaultAsync(t => t.Id == transactionId && accountIds.Contains(t.AccountId), ct);
        if (tx == null) return RecurringLinkResult.NotFound;

        if (tx.IsProvisional) return RecurringLinkResult.Conflict("Une transaction provisionnelle ne règle rien.");
        if (tx.RecurringTransactionId.HasValue && tx.RecurringTransactionId.Value != recurringId)
            return RecurringLinkResult.Conflict("Cette transaction règle déjà une autre récurrente.");
        if (await _context.Echeances.AnyAsync(e => e.TransactionId == tx.Id, ct))
            return RecurringLinkResult.Conflict("Cette transaction règle déjà une échéance.");
        if (tx.Type != recurring.Type)
            return RecurringLinkResult.Conflict("Cette transaction n'est pas du même sens que la récurrente.");

        if (tx.RecurringTransactionId != recurringId)
        {
            tx.RecurringTransactionId = recurringId;
            await _context.SaveChangesAsync(ct);
        }

        var day = _household.TodayLocal(tx.Date);
        var monthStart = new DateOnly(day.Year, day.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var candidates = await LoadMonthCandidatesAsync(dashboardId, recurring, monthStart, monthEnd, ct);
        var items = AgendaProjectors.FromRecurring(new[] { recurring }, monthStart, monthEnd, candidates);
        var item = items.FirstOrDefault(i => i.TransactionId == tx.Id) ?? items.FirstOrDefault();
        return RecurringLinkResult.Ok(item);
    }

    /// <summary>
    /// Remet le lien à null s'il valait cette récurrente sur une transaction des comptes du dashboard. 404 sinon,
    /// y compris pour une provision : son lien est au provisionnement, le défaire casserait la réconciliation du
    /// versement réel. 409 sur une récurrente provisionnée, comme pour le lien.
    /// </summary>
    public async Task<RecurringLinkResult> UnlinkAsync(int dashboardId, int recurringId, int transactionId, CancellationToken ct)
    {
        var recurring = await FindRecurringAsync(dashboardId, recurringId, ct);
        if (recurring == null) return RecurringLinkResult.NotFound;
        if (recurring.ProvisionAtMonthStart) return RecurringLinkResult.Conflict(ProvisionedMessage);

        var accountIds = await AccountIdsAsync(dashboardId, ct);
        var tx = await _context.Transactions.FirstOrDefaultAsync(
            t => t.Id == transactionId && accountIds.Contains(t.AccountId) && t.RecurringTransactionId == recurringId && !t.IsProvisional, ct);
        if (tx == null) return RecurringLinkResult.NotFound;

        tx.RecurringTransactionId = null;
        await _context.SaveChangesAsync(ct);
        return RecurringLinkResult.Ok(null);
    }

    private Task<RecurringTransaction?> FindRecurringAsync(int dashboardId, int recurringId, CancellationToken ct) =>
        _context.RecurringTransactions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == recurringId && r.DashboardId == dashboardId, ct);

    private Task<List<int>> AccountIdsAsync(int dashboardId, CancellationToken ct) =>
        _context.DashboardAccounts.AsNoTracking()
            .Where(da => da.DashboardId == dashboardId)
            .Select(da => da.AccountId)
            .ToListAsync(ct);

    /// <summary>
    /// Les candidats d'un mois pour une récurrente : même périmètre que la projection d'AgendaController, plus le
    /// sens et l'exclusion des transactions liées à une autre récurrente. Un jour de marge de chaque côté en SQL
    /// (instants UTC), puis la date ramenée au jour du ménage et le mois filtré en mémoire.
    /// </summary>
    private async Task<List<SettlementCandidate>> LoadMonthCandidatesAsync(int dashboardId, RecurringTransaction recurring, DateOnly monthStart, DateOnly monthEnd, CancellationToken ct)
    {
        var accountIds = await AccountIdsAsync(dashboardId, ct);
        if (accountIds.Count == 0) return new List<SettlementCandidate>();

        var from = monthStart.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var toExclusive = monthEnd.AddDays(2).ToDateTime(TimeOnly.MinValue);

        var rows = await _context.Transactions.AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId) && !t.IsProvisional && t.Type == recurring.Type)
            .Where(t => t.Date >= from && t.Date < toExclusive)
            .Where(t => t.RecurringTransactionId == null || t.RecurringTransactionId == recurring.Id)
            .Where(t => !_context.Echeances.Any(e => e.TransactionId == t.Id))
            .Select(t => new { t.Id, t.Date, t.Amount, t.Type, t.Description, t.CounterpartyName, t.RecurringTransactionId })
            .ToListAsync(ct);

        return rows
            .Select(t => new SettlementCandidate(t.Id, _household.TodayLocal(t.Date), t.Amount, t.Type, t.Description, t.CounterpartyName, t.RecurringTransactionId))
            .Where(c => c.Date >= monthStart && c.Date <= monthEnd)
            .ToList();
    }
}
