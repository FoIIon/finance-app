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
