using FinanceApp.API.DTOs;
using FinanceApp.API.Models;

namespace FinanceApp.API.Services.Reporting;

/// <summary>
/// Le résumé d'une période (indicateurs, répartitions, tendance) avec les règles du bilan.
///
/// Ce que la fusion des moteurs a changé à l'écran (02/09/2026) : une régularisation créditrice
/// marquée fixe (remboursement d'énergie) ne gonfle plus les entrées, elle réduit les dépenses, comme
/// au bilan. Le reste reste identique puisque entrées − dépenses ne bouge pas. Et le balayage vers le
/// livret, hors bilan, ne compte plus en mises de côté sur l'écran d'accueil alors que le bilan
/// l'écartait déjà : c'était le même euro compté deux fois selon l'onglet.
/// </summary>
public static class SummaryBuilder
{
    /// <summary>Indicateurs et répartitions de la période. La courbe mensuelle est ajoutée à part.</summary>
    public static TransactionSummaryDto Totals(IReadOnlyList<ReportLine> lines, bool includeExceptional)
    {
        var all = ReportLines.Classify(lines);

        // Toujours calculé sur tout, il sert au libellé « dont X € exceptionnels » même quand le filtre les retire.
        var exceptionalExpenses = all
            .InExpenseBlocks()
            .Where(x => x.Line.Type == TransactionType.Expense && x.Line.IsExceptional)
            .Sum(x => x.Line.Amount);

        var flux = includeExceptional ? all : all.Where(x => !x.Line.IsExceptional).ToList();

        var entrees = flux.InBlock(BilanBlock.Entrees).ToList();
        var depenses = flux.InExpenseBlocks().ToList();
        var mises = flux.InBlock(BilanBlock.MisesDeCote).ToList();

        var totalIncome = entrees.Total();
        var totalExpenses = depenses.Total();
        var totalSavings = mises.Total();

        return new TransactionSummaryDto
        {
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            Balance = totalIncome - totalExpenses,
            TotalSavings = totalSavings,
            ExceptionalExpenses = exceptionalExpenses,
            CategoryBreakdown = CategoryBreakdowns.Build(depenses, totalExpenses, dropZero: false),
            IncomeBreakdown = CategoryBreakdowns.Build(entrees, totalIncome, dropZero: false),
            SavingsBreakdown = CategoryBreakdowns.Build(mises, totalSavings, dropZero: true),
        };
    }

    /// <summary>
    /// La tendance sur <paramref name="months"/> mois à partir de <paramref name="startOfMonth"/> :
    /// barres entrées / dépenses du mois, et solde total en fin de mois remonté depuis le solde actuel.
    /// </summary>
    /// <param name="lines">Toutes les transactions du périmètre depuis <paramref name="startOfMonth"/>.</param>
    /// <param name="currentTotalBalance">Solde consolidé d'aujourd'hui, ancré sur le booké.</param>
    public static List<MonthlyBalanceDto> MonthlyBalance(
        DateTime startOfMonth,
        int months,
        IReadOnlyList<ReportLine> lines,
        bool includeExceptional,
        decimal currentTotalBalance)
    {
        var classified = ReportLines.Classify(lines);

        // Barres : mêmes exclusions que les indicateurs (blocs du bilan, exceptionnel selon le filtre).
        var bars = classified
            .Where(x => includeExceptional || !x.Line.IsExceptional)
            .GroupBy(x => (x.Line.Date.Year, x.Line.Date.Month))
            .ToDictionary(
                g => g.Key,
                g => (Income: g.InBlock(BilanBlock.Entrees).Total(), Expenses: g.InExpenseBlocks().Total()));

        // Net de trésorerie : ce qui a réellement bougé sur les comptes. Les transferts s'annulent ou
        // alimentent un compte manuel déjà compté dans le solde. Les provisions n'existent pas en banque,
        // les compter décalerait tous les points passés du montant provisionné.
        var cashNet = classified
            .Where(x => !x.Line.IsProvisional && !x.Line.IsTransfer)
            .GroupBy(x => (x.Line.Date.Year, x.Line.Date.Month))
            .ToDictionary(
                g => g.Key,
                g => g.Where(x => x.Line.Type == TransactionType.Income).Sum(x => x.Line.Amount)
                   - g.Where(x => x.Line.Type == TransactionType.Expense).Sum(x => x.Line.Amount));

        return Enumerable.Range(0, months)
            .Select(i => startOfMonth.AddMonths(i))
            .Select(month =>
            {
                var bar = bars.GetValueOrDefault((month.Year, month.Month));
                // Solde fin de mois = solde actuel − net cumulé des mois strictement postérieurs.
                var netAfter = cashNet
                    .Where(kv => kv.Key.Year > month.Year || (kv.Key.Year == month.Year && kv.Key.Month > month.Month))
                    .Sum(kv => kv.Value);
                return new MonthlyBalanceDto
                {
                    Month = month.ToString("MMM yyyy"),
                    Income = bar.Income,
                    Expenses = bar.Expenses,
                    Balance = bar.Income - bar.Expenses,
                    TotalBalance = currentTotalBalance - netAfter,
                };
            })
            .ToList();
    }
}
