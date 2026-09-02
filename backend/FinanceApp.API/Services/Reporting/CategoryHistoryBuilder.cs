using System.Globalization;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;

namespace FinanceApp.API.Services.Reporting;

/// <summary>
/// L'historique mensuel d'une catégorie, dans les deux projections que le frontend consomme :
/// les dépenses seules (barres courant / exceptionnel de la modale Dépenses) et les trois sens
/// (entrées, sorties, mises de côté de la modale Entrées/Sorties). Les deux partent des mêmes
/// blocs du bilan, donc affichent le même chiffre pour le même mois.
///
/// Avant (jusqu'au 01/09/2026), l'historique des dépenses sommait les dépenses brutes : sur
/// Loisirs en août, 279,50 € de barres là où le tableau affichait 8,00 €, les places de foot ayant
/// été remboursées trois jours plus tard.
/// </summary>
public static class CategoryHistoryBuilder
{
    /// <summary>Dépenses de la catégorie par mois, nettes des remboursements, avec la part exceptionnelle.</summary>
    public static List<CategoryMonthHistoryDto> ExpenseHistory(
        DateTime startMonth,
        int months,
        IEnumerable<ReportLine> lines,
        CultureInfo culture)
    {
        var byMonth = ReportLines.Classify(lines)
            .InExpenseBlocks()
            .GroupBy(x => (x.Line.Date.Year, x.Line.Date.Month))
            .ToDictionary(g => g.Key, g => g.ToList());

        return Enumerable.Range(0, months)
            .Select(i => startMonth.AddMonths(i))
            .Select(month =>
            {
                var items = byMonth.GetValueOrDefault((month.Year, month.Month)) ?? new List<ClassifiedLine>();
                var total = items.Total();
                var exceptional = items.Where(x => x.Line.IsExceptional).Total();
                return new CategoryMonthHistoryDto
                {
                    Month = month.ToString("yyyy-MM"),
                    Label = month.ToString("MMM yyyy", culture),
                    Total = total,
                    CurrentTotal = total - exceptional,
                    ExceptionalTotal = exceptional,
                };
            })
            .ToList();
    }

    /// <summary>Les trois sens d'un mois pour une catégorie, par mois, sur les lignes fournies.</summary>
    public static Dictionary<(int Year, int Month), FlowTotals> FlowByMonth(IEnumerable<ReportLine> lines) =>
        lines
            .GroupBy(l => (l.Date.Year, l.Date.Month))
            .ToDictionary(
                g => g.Key,
                g => CategoryFlowHistory.Aggregate(
                    g.Select(l => new FlowLine(l.Type, l.Amount, l.IsTransfer, l.IsRefund, l.IsFixed, l.ExcludeFromMonthlyReport))));
}
