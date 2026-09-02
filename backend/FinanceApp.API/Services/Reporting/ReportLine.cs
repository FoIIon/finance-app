using FinanceApp.API.DTOs;
using FinanceApp.API.Models;

namespace FinanceApp.API.Services.Reporting;

/// <summary>
/// La projection d'une transaction dont le reporting a besoin, et rien de plus. Chargée en une
/// requête puis agrégée côté client : SQLite ne sait pas sommer des decimal en SQL.
/// </summary>
public sealed class ReportLine
{
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public bool IsExceptional { get; set; }
    public bool IsFixed { get; set; }
    public bool IsRefund { get; set; }
    public bool IsProvisional { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = string.Empty;
    public string CategoryColor { get; set; } = string.Empty;
    public bool IsTransfer { get; set; }
    public bool ExcludeFromMonthlyReport { get; set; }

    public BilanEntry Classify() =>
        BilanClassifier.Classify(new BilanLine(Type, Amount, IsTransfer, ExcludeFromMonthlyReport, IsFixed, IsRefund));

    /// <summary>Projection EF : à appeler en fin de requête, une fois les filtres posés.</summary>
    public static IQueryable<ReportLine> Project(IQueryable<Transaction> query) =>
        query.Select(t => new ReportLine
        {
            Date = t.Date,
            Type = t.Type,
            Amount = t.Amount,
            IsExceptional = t.IsExceptional,
            IsFixed = t.IsFixed,
            IsRefund = t.IsRefund,
            IsProvisional = t.IsProvisional,
            CategoryId = t.Category.Id,
            CategoryName = t.Category.Name,
            CategoryIcon = t.Category.Icon,
            CategoryColor = t.Category.Color,
            IsTransfer = t.Category.IsTransfer,
            ExcludeFromMonthlyReport = t.Category.ExcludeFromMonthlyReport,
        });
}

/// <summary>Une ligne déjà classée : la transaction et le bloc où elle compte.</summary>
public readonly record struct ClassifiedLine(ReportLine Line, BilanEntry Entry);

public static class ReportLines
{
    public static List<ClassifiedLine> Classify(IEnumerable<ReportLine> lines) =>
        lines.Select(l => new ClassifiedLine(l, l.Classify())).ToList();

    public static IEnumerable<ClassifiedLine> InBlock(this IEnumerable<ClassifiedLine> lines, BilanBlock block) =>
        lines.Where(x => x.Entry.Block == block);

    public static IEnumerable<ClassifiedLine> InExpenseBlocks(this IEnumerable<ClassifiedLine> lines) =>
        lines.Where(x => x.Entry.IsExpenseBlock);

    public static decimal Total(this IEnumerable<ClassifiedLine> lines) =>
        lines.Sum(x => x.Entry.Amount);
}

/// <summary>Répartition par catégorie d'un ensemble de lignes classées.</summary>
public static class CategoryBreakdowns
{
    /// <param name="total">Base des pourcentages. Zéro ou négatif : les pourcentages restent à zéro.</param>
    /// <param name="dropZero">Masque les catégories dont le net est nul (un versement et son retrait).</param>
    public static List<CategoryBreakdownDto> Build(IEnumerable<ClassifiedLine> lines, decimal total, bool dropZero)
    {
        var result = lines
            .GroupBy(x => new { x.Line.CategoryId, x.Line.CategoryName, x.Line.CategoryIcon, x.Line.CategoryColor })
            .Select(g =>
            {
                var amount = g.Sum(x => x.Entry.Amount);
                return new CategoryBreakdownDto
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    CategoryIcon = g.Key.CategoryIcon,
                    CategoryColor = g.Key.CategoryColor,
                    Amount = amount,
                    Percentage = total > 0 ? Math.Round(amount / total * 100, 1) : 0,
                };
            });

        if (dropZero) result = result.Where(c => c.Amount != 0);

        return result.OrderByDescending(c => c.Amount).ToList();
    }
}
