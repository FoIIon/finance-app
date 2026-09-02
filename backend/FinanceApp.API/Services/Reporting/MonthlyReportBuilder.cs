using FinanceApp.API.DTOs;

namespace FinanceApp.API.Services.Reporting;

/// <summary>
/// Le bilan mensuel en cinq blocs, à partir des transactions du mois déjà chargées.
/// ENTRÉES − FIXE − MISES DE CÔTÉ − VARIABLE = TOTAL, le HORS BILAN s'affiche sous le total.
/// Pure : ni base, ni horloge, donc testable ligne par ligne.
/// </summary>
public static class MonthlyReportBuilder
{
    public static MonthlyReportDto Build(int year, int month, IEnumerable<ReportLine> lines)
    {
        var classified = ReportLines.Classify(lines);

        var entrees = classified.InBlock(BilanBlock.Entrees).ToList();
        var fixe = classified.InBlock(BilanBlock.Fixe).ToList();
        var mises = classified.InBlock(BilanBlock.MisesDeCote).ToList();
        var variable = classified.InBlock(BilanBlock.Variable).ToList();
        var horsBilan = classified.InBlock(BilanBlock.HorsBilan).ToList();

        var totalEntrees = entrees.Total();
        var totalFixe = fixe.Total();
        var totalMises = mises.Total();
        var totalVariable = variable.Total();

        return new MonthlyReportDto
        {
            Year = year,
            Month = month,
            Entrees = totalEntrees,
            Fixe = totalFixe,
            MisesDeCote = totalMises,
            Variable = totalVariable,
            VariableExceptionnel = variable.Where(x => x.Line.IsExceptional).Total(),
            HorsBilan = horsBilan.Total(),
            Total = totalEntrees - totalFixe - totalMises - totalVariable,
            // Pas de pourcentage sur le bilan : la lecture se fait en euros, bloc par bloc.
            EntreesByCategory = CategoryBreakdowns.Build(entrees, total: 0, dropZero: false),
            FixeByCategory = CategoryBreakdowns.Build(fixe, total: 0, dropZero: true),
            MisesDeCoteByCategory = CategoryBreakdowns.Build(mises, total: 0, dropZero: true),
            VariableByCategory = CategoryBreakdowns.Build(variable, total: 0, dropZero: false),
            HorsBilanByCategory = CategoryBreakdowns.Build(horsBilan, total: 0, dropZero: true),
        };
    }
}
