using FinanceApp.API.Models;
using FinanceApp.API.Services.Reporting;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>Le résumé d'une période, aligné sur les blocs du bilan.</summary>
public class SummaryBuilderTests
{
    static ReportLine L(TransactionType type, decimal montant, string categorie, int id, DateTime? date = null, bool transfert = false, bool horsBilan = false, bool fixe = false, bool remboursement = false, bool exceptionnel = false, bool provision = false) =>
        new()
        {
            Date = date ?? new DateTime(2026, 8, 15),
            Type = type,
            Amount = montant,
            CategoryId = id,
            CategoryName = categorie,
            IsTransfer = transfert,
            ExcludeFromMonthlyReport = horsBilan,
            IsFixed = fixe,
            IsRefund = remboursement,
            IsExceptional = exceptionnel,
            IsProvisional = provision,
        };

    [Fact]
    public void UneRegularisationFixe_ReduitLesDepenses_AuLieuDeGonflerLesEntrees()
    {
        var lignes = new[]
        {
            L(TransactionType.Expense, 150m, "Énergie", 14, fixe: true),
            L(TransactionType.Income, 40m, "Énergie", 14, fixe: true),
        };

        var s = SummaryBuilder.Totals(lignes, includeExceptional: true);

        Assert.Equal(0m, s.TotalIncome);
        Assert.Equal(110m, s.TotalExpenses);
        Assert.Equal(-110m, s.Balance);
    }

    [Fact]
    public void LeBalayageHorsBilan_NeComptePlusEnMisesDeCote()
    {
        // Le bilan l'écartait déjà, l'écran d'accueil le comptait encore : même euro, deux onglets.
        var lignes = new[]
        {
            L(TransactionType.Expense, 1837.12m, "Virement interne", 20, transfert: true, horsBilan: true),
            L(TransactionType.Expense, 400m, "Épargne", 16, transfert: true),
        };

        var s = SummaryBuilder.Totals(lignes, includeExceptional: true);

        Assert.Equal(400m, s.TotalSavings);
        var epargne = Assert.Single(s.SavingsBreakdown);
        Assert.Equal("Épargne", epargne.CategoryName);
        Assert.Equal(100m, epargne.Percentage);
    }

    [Fact]
    public void UnRemboursement_NetteLesDepenses_EtLeSoldeNeBougePas()
    {
        var lignes = new[]
        {
            L(TransactionType.Income, 2000m, "Salaire", 8),
            L(TransactionType.Expense, 271.50m, "Loisirs", 4),
            L(TransactionType.Income, 271.50m, "Loisirs", 4, remboursement: true),
        };

        var s = SummaryBuilder.Totals(lignes, includeExceptional: true);

        Assert.Equal(2000m, s.TotalIncome);
        Assert.Equal(0m, s.TotalExpenses);
        Assert.Equal(2000m, s.Balance);
    }

    [Fact]
    public void ExclureLExceptionnel_RetireLaLigneDesFlux_MaisPasDuMontantAffiche()
    {
        var lignes = new[]
        {
            L(TransactionType.Expense, 800m, "Maison", 17, exceptionnel: true),
            L(TransactionType.Expense, 100m, "Alimentation", 1),
        };

        var avec = SummaryBuilder.Totals(lignes, includeExceptional: true);
        var sans = SummaryBuilder.Totals(lignes, includeExceptional: false);

        Assert.Equal(900m, avec.TotalExpenses);
        Assert.Equal(100m, sans.TotalExpenses);
        Assert.Equal(800m, avec.ExceptionalExpenses);
        Assert.Equal(800m, sans.ExceptionalExpenses);
        Assert.Single(sans.CategoryBreakdown);
    }

    [Fact]
    public void LesPourcentages_SontRapportesAuTotalDuBloc()
    {
        var lignes = new[]
        {
            L(TransactionType.Expense, 750m, "Alimentation", 1),
            L(TransactionType.Expense, 250m, "Transport", 2),
        };

        var s = SummaryBuilder.Totals(lignes, includeExceptional: true);

        Assert.Equal(75m, s.CategoryBreakdown[0].Percentage);
        Assert.Equal(25m, s.CategoryBreakdown[1].Percentage);
    }

    [Fact]
    public void LaCourbeDuSolde_RemonteDepuisLeSoldeActuel_MoisParMois()
    {
        var debut = new DateTime(2026, 7, 1);
        var lignes = new[]
        {
            // Juillet : net −200
            L(TransactionType.Expense, 200m, "Alimentation", 1, date: new DateTime(2026, 7, 10)),
            // Août : net +500
            L(TransactionType.Income, 2000m, "Salaire", 8, date: new DateTime(2026, 8, 1)),
            L(TransactionType.Expense, 1500m, "Logement", 3, date: new DateTime(2026, 8, 5), fixe: true),
        };

        var courbe = SummaryBuilder.MonthlyBalance(debut, 2, lignes, includeExceptional: true, currentTotalBalance: 10000m);

        Assert.Equal(2, courbe.Count);
        Assert.Equal(9500m, courbe[0].TotalBalance);   // juillet = aujourd'hui − net d'août
        Assert.Equal(10000m, courbe[1].TotalBalance);  // août = aujourd'hui
        Assert.Equal(2000m, courbe[1].Income);
        Assert.Equal(1500m, courbe[1].Expenses);
    }

    [Fact]
    public void LaCourbeDuSolde_IgnoreLesProvisionsEtLesTransferts_MaisLesBarresGardentLesTransfertsHorsBlocs()
    {
        var debut = new DateTime(2026, 8, 1);
        var lignes = new[]
        {
            L(TransactionType.Income, 3000m, "Salaire", 8, date: new DateTime(2026, 8, 1), provision: true),
            L(TransactionType.Expense, 400m, "Épargne", 16, date: new DateTime(2026, 8, 7), transfert: true),
            L(TransactionType.Expense, 100m, "Alimentation", 1, date: new DateTime(2026, 8, 9)),
        };

        var courbe = SummaryBuilder.MonthlyBalance(debut, 1, lignes, includeExceptional: true, currentTotalBalance: 5000m);

        var aout = Assert.Single(courbe);
        Assert.Equal(5000m, aout.TotalBalance);
        // La provision n'est pas en banque, le transfert n'est pas une dépense : seuls les 100 € comptent en barre.
        Assert.Equal(100m, aout.Expenses);
        Assert.Equal(3000m, aout.Income); // la barre montre le salaire attendu, la courbe du solde non
    }

    [Fact]
    public void LesBarres_RetirentLExceptionnel_QuandLeFiltreLeDemande()
    {
        var debut = new DateTime(2026, 8, 1);
        var lignes = new[]
        {
            L(TransactionType.Expense, 800m, "Maison", 17, date: new DateTime(2026, 8, 3), exceptionnel: true),
            L(TransactionType.Expense, 100m, "Alimentation", 1, date: new DateTime(2026, 8, 9)),
        };

        var sans = SummaryBuilder.MonthlyBalance(debut, 1, lignes, includeExceptional: false, currentTotalBalance: 0m);

        Assert.Equal(100m, sans[0].Expenses);
    }
}
