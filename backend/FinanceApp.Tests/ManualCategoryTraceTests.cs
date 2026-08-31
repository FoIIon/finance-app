using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// La trace d'une catégorie corrigée à la main. Elle existe parce que chaque correction de Sébastien
/// signalait une règle absente ou fausse — la boulangerie « Wex (sandwichs) » rangée en Restaurants,
/// les « Taxes diverses » en Frais bancaires, la crèche en Logement — et qu'il fallait le lui demander
/// pour le savoir.
/// </summary>
public class ManualCategoryTraceTests
{
    private static Transaction Ligne(int categoryId) => new() { CategoryId = categoryId };

    [Fact]
    public void UneCorrection_GardeLaCategorieDorigineEtLaDate()
    {
        // Wex (sandwichs) : la règle l'avait mis en Restaurants (11), Sébastien corrige en Alimentation (1).
        var t = Ligne(11);
        var quand = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc);

        Assert.True(ManualCategoryTrace.Apply(t, 1, quand));

        Assert.Equal(1, t.CategoryId);
        Assert.Equal(11, t.CategoryBeforeManualId);
        Assert.Equal(quand, t.CategorySetManuallyAt);
    }

    [Fact]
    public void DeuxiemeCorrection_NEcrasePasLOrigine()
    {
        // C'est la première catégorie qui dit quelle règle s'est trompée. Une hésitation ensuite
        // (Alimentation puis Restaurants) ne doit pas effacer cette information.
        var t = Ligne(11);
        var premiere = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc);
        var seconde = new DateTime(2026, 9, 1, 18, 30, 0, DateTimeKind.Utc);

        ManualCategoryTrace.Apply(t, 1, premiere);
        ManualCategoryTrace.Apply(t, 26, seconde);

        Assert.Equal(26, t.CategoryId);
        Assert.Equal(11, t.CategoryBeforeManualId);
        Assert.Equal(seconde, t.CategorySetManuallyAt);
    }

    [Fact]
    public void MemeCategorie_NEstPasUneCorrection()
    {
        // Rouvrir un écran et resauver la même ligne ne dit rien sur les règles.
        var t = Ligne(11);

        Assert.False(ManualCategoryTrace.Apply(t, 11, DateTime.UtcNow));

        Assert.Null(t.CategoryBeforeManualId);
        Assert.Null(t.CategorySetManuallyAt);
    }
}
