using FinanceApp.API.Models;
using FinanceApp.API.Services.Reporting;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// La seule fonction qui range une transaction dans un bloc du bilan. Chaque test est une règle,
/// et les cas de précédence sont ceux qui ont divergé entre les anciens moteurs.
/// </summary>
public class BilanClassifierTests
{
    static BilanLine Ligne(TransactionType type, decimal montant, bool transfert = false, bool horsBilan = false, bool fixe = false, bool remboursement = false) =>
        new(type, montant, transfert, horsBilan, fixe, remboursement);

    [Fact]
    public void UneDepenseOrdinaire_VaEnVariable()
    {
        var e = BilanClassifier.Classify(Ligne(TransactionType.Expense, 45.20m));
        Assert.Equal(BilanBlock.Variable, e.Block);
        Assert.Equal(45.20m, e.Amount);
    }

    [Fact]
    public void UnRevenu_VaEnEntrees_EnPositif()
    {
        var e = BilanClassifier.Classify(Ligne(TransactionType.Income, 3200m));
        Assert.Equal(BilanBlock.Entrees, e.Block);
        Assert.Equal(3200m, e.Amount);
    }

    [Fact]
    public void UneChargeFixe_VaEnFixe()
    {
        var e = BilanClassifier.Classify(Ligne(TransactionType.Expense, 912.40m, fixe: true));
        Assert.Equal(BilanBlock.Fixe, e.Block);
        Assert.Equal(912.40m, e.Amount);
    }

    [Fact]
    public void UneRegularisationCrediticeFixe_ReduitLeFixe_AuLieuDeGonflerLesEntrees()
    {
        // Le remboursement d'acompte d'énergie : le résumé le comptait en revenu, le bilan en moins sur le fixe.
        var e = BilanClassifier.Classify(Ligne(TransactionType.Income, 84m, fixe: true));
        Assert.Equal(BilanBlock.Fixe, e.Block);
        Assert.Equal(-84m, e.Amount);
    }

    [Fact]
    public void UnRemboursement_ReduitLeVariable_JamaisUneEntree()
    {
        var e = BilanClassifier.Classify(Ligne(TransactionType.Income, 271.50m, remboursement: true));
        Assert.Equal(BilanBlock.Variable, e.Block);
        Assert.Equal(-271.50m, e.Amount);
    }

    [Fact]
    public void UnRemboursementSurUneChargeFixe_ReduitLeFixe()
    {
        var e = BilanClassifier.Classify(Ligne(TransactionType.Income, 30m, fixe: true, remboursement: true));
        Assert.Equal(BilanBlock.Fixe, e.Block);
        Assert.Equal(-30m, e.Amount);
    }

    [Fact]
    public void LeDrapeauRemboursementSurUneDepense_EstIgnore()
    {
        // Une dépense n'a rien à rembourser : le drapeau posé par erreur ne change pas son bloc.
        var e = BilanClassifier.Classify(Ligne(TransactionType.Expense, 12m, remboursement: true));
        Assert.Equal(BilanBlock.Variable, e.Block);
        Assert.Equal(12m, e.Amount);
    }

    [Fact]
    public void UnTransfert_VaEnMisesDeCote_MemeMarqueFixe()
    {
        // L'ordre permanent vers l'épargne est fixe par nature, mais reste une mise de côté.
        var e = BilanClassifier.Classify(Ligne(TransactionType.Expense, 400m, transfert: true, fixe: true));
        Assert.Equal(BilanBlock.MisesDeCote, e.Block);
        Assert.Equal(400m, e.Amount);
    }

    [Fact]
    public void UnRetraitDEpargne_ReduitLesMisesDeCote()
    {
        // Le retrait de 1 000 € pour la peinture, passé en revenu à l'époque.
        var e = BilanClassifier.Classify(Ligne(TransactionType.Income, 1000m, transfert: true));
        Assert.Equal(BilanBlock.MisesDeCote, e.Block);
        Assert.Equal(-1000m, e.Amount);
    }

    [Fact]
    public void UneCategorieHorsBilan_PrimeSurTout()
    {
        var e = BilanClassifier.Classify(Ligne(TransactionType.Expense, 1837.12m, transfert: true, horsBilan: true, fixe: true));
        Assert.Equal(BilanBlock.HorsBilan, e.Block);
        Assert.Equal(1837.12m, e.Amount);
    }

    [Theory]
    [InlineData(BilanBlock.Fixe, true)]
    [InlineData(BilanBlock.Variable, true)]
    [InlineData(BilanBlock.Entrees, false)]
    [InlineData(BilanBlock.MisesDeCote, false)]
    [InlineData(BilanBlock.HorsBilan, false)]
    public void SeulsFixeEtVariable_SontDesBlocsDeDepense(BilanBlock bloc, bool attendu)
    {
        Assert.Equal(attendu, new BilanEntry(bloc, 1m).IsExpenseBlock);
    }
}
