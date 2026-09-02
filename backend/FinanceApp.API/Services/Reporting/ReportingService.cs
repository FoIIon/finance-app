using System.Globalization;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services.Reporting;

/// <summary>
/// Charge les transactions d'un périmètre et les confie aux builders. C'est la seule classe du
/// reporting qui parle à la base : les règles vivent dans <see cref="BilanClassifier"/> et les
/// builders, testés sans base.
///
/// Toute agrégation se fait côté client après une projection <see cref="ReportLine"/> : SQLite ne
/// sait pas sommer des decimal en SQL.
/// </summary>
public class ReportingService
{
    private static readonly CultureInfo Culture = new("fr-FR");

    private readonly AppDbContext _context;
    private readonly AccountBalanceService _balances;

    public ReportingService(AppDbContext context, AccountBalanceService balances)
    {
        _context = context;
        _balances = balances;
    }

    private IQueryable<Transaction> Scoped(IReadOnlyCollection<int> accountIds) =>
        _context.Transactions.Where(t => accountIds.Contains(t.AccountId));

    /// <summary>Bilan mensuel en cinq blocs, voir <see cref="MonthlyReportBuilder"/>.</summary>
    public async Task<MonthlyReportDto> MonthlyReportAsync(List<int> accountIds, int year, int month)
    {
        if (accountIds.Count == 0) return new MonthlyReportDto { Year = year, Month = month };

        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        var lines = await ReportLine.Project(Scoped(accountIds).Where(t => t.Date >= start && t.Date < end)).ToListAsync();
        return MonthlyReportBuilder.Build(year, month, lines);
    }

    /// <summary>
    /// Indicateurs, répartitions et tendance six mois d'une période. Le filtre par compte bancaire
    /// s'applique aux indicateurs, pas à la tendance ni au solde : ils restent consolidés.
    /// </summary>
    public async Task<TransactionSummaryDto> SummaryAsync(
        int userId,
        List<int> accountIds,
        DateTime? from,
        DateTime? to,
        int? bankAccountId,
        bool includeExceptional,
        DateTime? now = null)
    {
        if (accountIds.Count == 0) return new TransactionSummaryDto();

        var query = Scoped(accountIds);
        if (from.HasValue) query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue) query = query.Where(t => t.Date <= to.Value);
        if (bankAccountId.HasValue) query = query.Where(t => t.BankAccountId == bankAccountId.Value);

        var lines = await ReportLine.Project(query).ToListAsync();
        var summary = SummaryBuilder.Totals(lines, includeExceptional);

        var today = now ?? DateTime.UtcNow;
        var sixMonthsAgo = today.AddMonths(-5);
        var startOfMonth = new DateTime(sixMonthsAgo.Year, sixMonthsAgo.Month, 1);

        var trendLines = await ReportLine.Project(Scoped(accountIds).Where(t => t.Date >= startOfMonth)).ToListAsync();
        var currentTotalBalance = await _balances.TotalBookedBalanceAsync(userId, accountIds);
        summary.MonthlyBalance = SummaryBuilder.MonthlyBalance(startOfMonth, 6, trendLines, includeExceptional, currentTotalBalance);

        return summary;
    }

    /// <summary>Reste du mois jour par jour, voir <see cref="BurndownBuilder"/>.</summary>
    /// <param name="dashboardId">Dashboard dont on lit les récurrentes à venir. Null : aucune récurrente.</param>
    public async Task<BurndownDto> BurndownAsync(List<int> accountIds, int? dashboardId, int year, int month, DateTime? now = null)
    {
        if (accountIds.Count == 0) return new BurndownDto { Year = year, Month = month, RecurringIncluded = true };

        var today = now ?? DateTime.UtcNow;
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        var monthLines = await ReportLine.Project(Scoped(accountIds).Where(t => t.Date >= start && t.Date < end)).ToListAsync();

        var paceStart = today.Date.AddDays(-(BurndownBuilder.PaceWindowDays - 1));
        var paceEnd = today.Date.AddDays(1);
        var paceLines = await ReportLine.Project(Scoped(accountIds).Where(t => t.Date >= paceStart && t.Date < paceEnd)).ToListAsync();

        var recurrings = new List<RecurringTransaction>();
        var provisioned = new HashSet<int>();
        if (end > today && dashboardId.HasValue)
        {
            // Hors catégories de transfert, cohérent avec la courbe qui les exclut.
            recurrings = await _context.RecurringTransactions
                .Include(r => r.Category)
                .Where(r => r.DashboardId == dashboardId.Value && r.IsActive
                         && (r.Category == null || !r.Category.IsTransfer))
                .ToListAsync();

            provisioned = (await _context.Transactions
                .Where(t => t.IsProvisional && t.RecurringTransactionId != null && t.Date >= start && t.Date < end)
                .Select(t => t.RecurringTransactionId!.Value)
                .ToListAsync()).ToHashSet();
        }

        return BurndownBuilder.Build(year, month, today, monthLines, paceLines, recurrings, provisioned);
    }

    /// <summary>Dépenses d'une catégorie par mois, nettes des remboursements, part exceptionnelle séparée.</summary>
    public async Task<List<CategoryMonthHistoryDto>> CategoryHistoryAsync(List<int> accountIds, int categoryId, int months, DateTime? now = null)
    {
        if (accountIds.Count == 0) return new List<CategoryMonthHistoryDto>();

        months = Math.Clamp(months, 1, 36);
        var today = now ?? DateTime.UtcNow;
        var startMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-(months - 1));

        var lines = await ReportLine.Project(Scoped(accountIds).Where(t => t.CategoryId == categoryId && t.Date >= startMonth)).ToListAsync();
        return CategoryHistoryBuilder.ExpenseHistory(startMonth, months, lines, Culture);
    }

    /// <summary>
    /// Historique deux sens d'une catégorie avec le N-1 quand il est comparable. Null si la catégorie
    /// n'existe pas. Mêmes filtres que le résumé qui a produit la ligne cliquée, sinon le graphe
    /// contredit le tableau.
    /// </summary>
    public async Task<CategoryFlowHistoryDto?> CategoryFlowHistoryAsync(
        List<int> accountIds,
        int categoryId,
        int months,
        int? bankAccountId,
        bool includeExceptional,
        DateTime? now = null)
    {
        if (accountIds.Count == 0) return new CategoryFlowHistoryDto();

        var categorie = await _context.Categories
            .Where(c => c.Id == categoryId)
            .Select(c => new { c.IsTransfer, c.ExcludeFromMonthlyReport })
            .FirstOrDefaultAsync();
        if (categorie is null) return null;

        months = Math.Clamp(months, 1, 24);
        var today = now ?? DateTime.UtcNow;
        var startMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-(months - 1));
        // Douze mois plus bas pour ramener le N-1 de chaque mois de la fenêtre en une requête.
        var fetchFrom = startMonth.AddMonths(-12);

        var query = Scoped(accountIds).Where(t => t.CategoryId == categoryId && t.Date >= fetchFrom);
        if (bankAccountId.HasValue) query = query.Where(t => t.BankAccountId == bankAccountId.Value);
        if (!includeExceptional) query = query.Where(t => !t.IsExceptional);

        var lines = await ReportLine.Project(query).ToListAsync();
        var byMonth = CategoryHistoryBuilder.FlowByMonth(lines);

        var horsBilan = categorie.ExcludeFromMonthlyReport;
        FlowTotals TotauxDe(DateTime m) => byMonth.GetValueOrDefault((m.Year, m.Month));

        var premiereBancaire = await Scoped(accountIds)
            .Where(t => t.BankAccountId != null)
            .OrderBy(t => t.Date)
            .Select(t => (DateTime?)t.Date)
            .FirstOrDefaultAsync();
        var premierComparable = CategoryFlowHistory.FirstComparableMonth(premiereBancaire);

        var mois = Enumerable.Range(0, months)
            .Select(i => startMonth.AddMonths(i))
            .Select(month =>
            {
                var t = TotauxDe(month);
                var dto = new CategoryFlowMonthDto
                {
                    Month = month.ToString("yyyy-MM"),
                    Label = month.ToString("MMM yyyy", Culture),
                    Income = t.Income,
                    Expenses = t.Expenses,
                    Savings = t.Savings,
                    Net = CategoryFlowHistory.Net(t, horsBilan),
                };

                if (CategoryFlowHistory.IsComparable(month, premierComparable))
                {
                    var n1 = TotauxDe(month.AddMonths(-12));
                    dto.IncomePreviousYear = n1.Income;
                    dto.ExpensesPreviousYear = n1.Expenses;
                    dto.NetPreviousYear = CategoryFlowHistory.Net(n1, horsBilan);
                }

                return dto;
            })
            .ToList();

        return new CategoryFlowHistoryDto
        {
            Months = mois,
            IsTransferCategory = categorie.IsTransfer,
            IsOffBalanceCategory = horsBilan,
            PreviousYearAvailable = mois.Any(m => m.NetPreviousYear.HasValue),
            PreviousYearAvailableFrom = premierComparable,
            FirstBankTransactionDate = premiereBancaire,
            FirstFullBankMonth = CategoryFlowHistory.FirstFullBankMonth(premiereBancaire),
        };
    }
}
