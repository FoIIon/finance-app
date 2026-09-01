using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// L'historique deux sens d'une catégorie, sur les lignes réelles qui ont motivé le service.
/// </summary>
public class CategoryFlowHistoryTests
{
    static FlowLine Depense(decimal m) => new(TransactionType.Expense, m, IsTransfer: false, IsRefund: false);
    static FlowLine Entree(decimal m) => new(TransactionType.Income, m, IsTransfer: false, IsRefund: false);
    static FlowLine Remboursement(decimal m) => new(TransactionType.Income, m, IsTransfer: false, IsRefund: true);
    static FlowLine Transfert(decimal m) => new(TransactionType.Expense, m, IsTransfer: true, IsRefund: false);

    [Fact]
    public void UnRemboursement_SeDeduitDesSorties_JamaisDesEntrees()
    {
        // Août 2026, Sorties : 271,50 € de places de foot avancées le 07/08, rendues par les
        // beaux-parents le 10/08. La catégorie doit peser 0, pas 271,50 des deux côtés.
        var t = CategoryFlowHistory.Aggregate(new[] { Depense(271.50m), Remboursement(271.50m) });

        Assert.Equal(0m, t.Income);
        Assert.Equal(0m, t.Expenses);
    }

    [Fact]
    public void UnRevenuNonMarque_ResteUneEntree()
    {
        // Enfants encaisse les allocations familiales et paie la crèche : les deux sens coexistent.
        var t = CategoryFlowHistory.Aggregate(new[] { Entree(578m), Depense(220m) });

        Assert.Equal(578m, t.Income);
        Assert.Equal(220m, t.Expenses);
        Assert.Equal(358m, CategoryFlowHistory.Net(t, offBalance: false));
    }

    [Fact]
    public void UnTransfert_VaEnMisesDeCote_EtSeSoustraitDuNet()
    {
        var t = CategoryFlowHistory.Aggregate(new[] { Transfert(830m) });

        Assert.Equal(830m, t.Savings);
        Assert.Equal(0m, t.Expenses);
        Assert.Equal(-830m, CategoryFlowHistory.Net(t, offBalance: false));
    }

    [Fact]
    public void UneCategorieHorsBilan_NeSoustraitPasSesMisesDeCote()
    {
        // Balayage du compte joint vers le livret : la contrepartie est déjà comptée ailleurs.
        var t = CategoryFlowHistory.Aggregate(new[] { Transfert(3000m) });

        Assert.Equal(0m, CategoryFlowHistory.Net(t, offBalance: true));
    }

    [Fact]
    public void UnRetraitDeTransfert_DiminueLesMisesDeCote()
    {
        var t = CategoryFlowHistory.Aggregate(new[]
        {
            Transfert(830m),
            new FlowLine(TransactionType.Income, 200m, IsTransfer: true, IsRefund: false),
        });

        Assert.Equal(630m, t.Savings);
    }

    [Fact]
    public void UnMoisVide_EstNeutre()
    {
        var t = CategoryFlowHistory.Aggregate(Array.Empty<FlowLine>());

        Assert.Equal(0m, t.Income);
        Assert.Equal(0m, t.Expenses);
        Assert.Equal(0m, t.Savings);
    }

    [Fact]
    public void LePremierMoisEntier_EstCeluiApresUneConnexionEnCoursDeMois()
    {
        // Première transaction bancaire réelle : 30/01/2026. Janvier ne couvre que deux jours.
        var premier = CategoryFlowHistory.FirstFullBankMonth(new DateTime(2026, 1, 30));

        Assert.Equal(new DateTime(2026, 2, 1), premier);
    }

    [Fact]
    public void UneConnexionLePremierDuMois_CouvreDejaCeMois()
    {
        Assert.Equal(new DateTime(2026, 2, 1), CategoryFlowHistory.FirstFullBankMonth(new DateTime(2026, 2, 1)));
    }

    [Fact]
    public void SansBanque_RienNEstComparable()
    {
        Assert.Null(CategoryFlowHistory.FirstFullBankMonth(null));
        Assert.Null(CategoryFlowHistory.FirstComparableMonth(null));
        Assert.False(CategoryFlowHistory.IsComparable(new DateTime(2026, 8, 1), null));
    }

    [Fact]
    public void LeComparatifNArrivePasAvantDouzeMoisDeBanque()
    {
        // Le point de la story : août 2026 pèse 1 165,22 € en Alimentation contre 422,60 € en août
        // 2025, où seule la carte Trade Republic était en base. Comparer les deux annoncerait
        // +176 % de courses. Le N-1 n'ouvre qu'en février 2027.
        var seuil = CategoryFlowHistory.FirstComparableMonth(new DateTime(2026, 1, 30));

        Assert.Equal(new DateTime(2027, 2, 1), seuil);
        Assert.False(CategoryFlowHistory.IsComparable(new DateTime(2026, 8, 1), seuil));
        Assert.False(CategoryFlowHistory.IsComparable(new DateTime(2027, 1, 1), seuil));
        Assert.True(CategoryFlowHistory.IsComparable(new DateTime(2027, 2, 1), seuil));
    }
}
