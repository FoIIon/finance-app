using FinanceApp.API.Models;
using FinanceApp.API.Services.Reporting;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>Le bilan en cinq blocs, sur un mois qui ressemble aux vrais.</summary>
public class MonthlyReportBuilderTests
{
    static ReportLine L(TransactionType type, decimal montant, string categorie, int id, bool transfert = false, bool horsBilan = false, bool fixe = false, bool remboursement = false, bool exceptionnel = false) =>
        new()
        {
            Date = new DateTime(2026, 8, 15),
            Type = type,
            Amount = montant,
            CategoryId = id,
            CategoryName = categorie,
            IsTransfer = transfert,
            ExcludeFromMonthlyReport = horsBilan,
            IsFixed = fixe,
            IsRefund = remboursement,
            IsExceptional = exceptionnel,
        };

    [Fact]
    public void LeTotal_EstEntreesMoinsFixeMoinsMisesMoinsVariable_LeHorsBilanAPart()
    {
        var lignes = new[]
        {
            L(TransactionType.Income, 3000m, "Salaire", 8),
            L(TransactionType.Expense, 900m, "Logement", 3, fixe: true),
            L(TransactionType.Expense, 400m, "Épargne", 16, transfert: true),
            L(TransactionType.Expense, 250m, "Alimentation", 1),
            L(TransactionType.Expense, 1200m, "Virement interne", 20, transfert: true, horsBilan: true),
        };

        var r = MonthlyReportBuilder.Build(2026, 8, lignes);

        Assert.Equal(3000m, r.Entrees);
        Assert.Equal(900m, r.Fixe);
        Assert.Equal(400m, r.MisesDeCote);
        Assert.Equal(250m, r.Variable);
        Assert.Equal(1200m, r.HorsBilan);
        Assert.Equal(1450m, r.Total);
    }

    [Fact]
    public void UnRemboursement_NetteSaCategorie_SansToucherAuxEntrees()
    {
        // 271,50 € de places de foot le 06/08, rendus le 10/08.
        var lignes = new[]
        {
            L(TransactionType.Expense, 271.50m, "Loisirs", 4),
            L(TransactionType.Income, 271.50m, "Loisirs", 4, remboursement: true),
        };

        var r = MonthlyReportBuilder.Build(2026, 8, lignes);

        Assert.Equal(0m, r.Entrees);
        Assert.Equal(0m, r.Variable);
        Assert.Empty(r.EntreesByCategory);
    }

    [Fact]
    public void UneRegularisationEnergie_SeDeduitDuBlocFixe()
    {
        var lignes = new[]
        {
            L(TransactionType.Expense, 150m, "Énergie", 14, fixe: true),
            L(TransactionType.Income, 40m, "Énergie", 14, fixe: true),
        };

        var r = MonthlyReportBuilder.Build(2026, 8, lignes);

        Assert.Equal(110m, r.Fixe);
        Assert.Equal(0m, r.Entrees);
        var energie = Assert.Single(r.FixeByCategory);
        Assert.Equal(110m, energie.Amount);
    }

    [Fact]
    public void LeVariableExceptionnel_NeCompteQueLesLignesMarquees()
    {
        var lignes = new[]
        {
            L(TransactionType.Expense, 1800m, "Maison", 17, exceptionnel: true),
            L(TransactionType.Expense, 120m, "Maison", 17),
            L(TransactionType.Expense, 500m, "Logement", 3, fixe: true, exceptionnel: true),
        };

        var r = MonthlyReportBuilder.Build(2026, 8, lignes);

        Assert.Equal(1920m, r.Variable);
        Assert.Equal(1800m, r.VariableExceptionnel);
    }

    [Fact]
    public void LesBlocsFixeMisesEtHorsBilan_MasquentUneCategorieANul()
    {
        // Un versement et son retrait le même mois : la catégorie pèse zéro, elle disparaît de la liste.
        var lignes = new[]
        {
            L(TransactionType.Expense, 200m, "Épargne", 16, transfert: true),
            L(TransactionType.Income, 200m, "Épargne", 16, transfert: true),
            L(TransactionType.Expense, 50m, "Courses", 1),
        };

        var r = MonthlyReportBuilder.Build(2026, 8, lignes);

        Assert.Equal(0m, r.MisesDeCote);
        Assert.Empty(r.MisesDeCoteByCategory);
        Assert.Single(r.VariableByCategory);
    }

    [Fact]
    public void UnMoisSansLigne_RendDesBlocsAZero()
    {
        var r = MonthlyReportBuilder.Build(2026, 2, Array.Empty<ReportLine>());

        Assert.Equal(2026, r.Year);
        Assert.Equal(2, r.Month);
        Assert.Equal(0m, r.Total);
        Assert.Empty(r.VariableByCategory);
    }

    [Fact]
    public void LesRepartitions_SontTrieesParMontantDecroissant()
    {
        var lignes = new[]
        {
            L(TransactionType.Expense, 30m, "Loisirs", 4),
            L(TransactionType.Expense, 600m, "Alimentation", 1),
            L(TransactionType.Expense, 90m, "Transport", 2),
        };

        var r = MonthlyReportBuilder.Build(2026, 8, lignes);

        Assert.Equal(new[] { "Alimentation", "Transport", "Loisirs" }, r.VariableByCategory.Select(c => c.CategoryName));
    }
}
