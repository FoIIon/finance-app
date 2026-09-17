using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>Le prédicat de matching unique, partagé par l'import, la recatégorisation et le routage.</summary>
public class CategoryRuleMatcherTests
{
    [Fact]
    public void Matche_SansCasse_SurLeLibelleOuLaContrepartie()
    {
        Assert.True(CategoryRuleMatcher.Matches("colruyt", "COLRUYT MARCHE", null));
        Assert.True(CategoryRuleMatcher.Matches("Lambrecht", "Virement", "LAMBRECHT GILLE"));
        Assert.False(CategoryRuleMatcher.Matches("Delhaize", "COLRUYT MARCHE", "COLRUYT"));
    }

    [Fact]
    public void MotCleVide_NeMatcheJamais()
    {
        Assert.False(CategoryRuleMatcher.Matches("", "n'importe quoi", "n'importe qui"));
        Assert.False(CategoryRuleMatcher.Matches("   ", "n'importe quoi", null));
    }

    [Fact]
    public void FirstMatch_RespecteLOrdreRecu()
    {
        // L'appelant trie du plus long au plus court : « Legumes vacances » doit battre « Vacance ».
        var rules = new List<CategoryRule>
        {
            new() { Id = 1, Keyword = "Legumes vacances", CategoryId = 7 },
            new() { Id = 2, Keyword = "Vacance", CategoryId = 30 },
        };
        var matched = CategoryRuleMatcher.FirstMatch(rules, "LEGUMES VACANCES CAMPING", null);
        Assert.Equal(7, matched!.CategoryId);
    }

    [Fact]
    public void FirstMatch_NullSiRien()
    {
        var rules = new List<CategoryRule> { new() { Id = 1, Keyword = "Colruyt", CategoryId = 7 } };
        Assert.Null(CategoryRuleMatcher.FirstMatch(rules, "STEONE", "STEONE"));
    }

    [Fact]
    public void UneRegleNumeriqueCourte_NeMatchePas_SurLaCommunicationAjouteeAuLibelle()
    {
        // À l'import, la communication structurée servie à part est ajoutée au libellé stocké (+++123/4567/89002+++),
        // mais les règles tournent sur le libellé tel que la banque le sert. Une règle « 4567 » qui vise un
        // commerçant ne doit pas attraper ce virement à cause de sa communication : le test fixe la raison de
        // la séparation faite dans BankSyncService entre bankDescription et la description enrichie.
        var rules = new List<CategoryRule> { new() { Id = 1, Keyword = "4567", CategoryId = 7 } };
        const string bankDescription = "Virement école";
        var enriched = StructuredCommunication.WithStructuredRemittance(bankDescription, "+++123/4567/89002+++");

        Assert.Equal("Virement école +++123/4567/89002+++", enriched);
        Assert.Null(CategoryRuleMatcher.FirstMatch(rules, bankDescription, null));
        Assert.NotNull(CategoryRuleMatcher.FirstMatch(rules, enriched, null));
    }

    [Fact]
    public void InApplicationOrder_PlusLongDAbord_PuisPlusAncien()
    {
        var rules = new List<CategoryRule>
        {
            new() { Id = 3, Keyword = "Orange", CategoryId = 1 },
            new() { Id = 1, Keyword = "ORANGE BELGIUM", CategoryId = 2 },
            new() { Id = 2, Keyword = "Amazon", CategoryId = 3 },
        }.AsQueryable();

        var ordered = CategoryRuleMatcher.InApplicationOrder(rules).Select(r => r.Id).ToList();
        Assert.Equal(new[] { 1, 2, 3 }, ordered);
    }
}
