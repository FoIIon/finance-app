using System.Globalization;
using FinanceApp.API.Services;

namespace FinanceApp.Tests;

/// <summary>
/// Parsing des réponses Trade Republic, vérifié contre des captures réelles de l'API
/// (Fixtures/, relevées le 2026-08-23). L'API n'étant pas documentée, ces fixtures sont
/// la seule spécification de sa forme : si TR change, ces tests le révèlent avant la prod.
/// </summary>
public class TradeRepublicPortfolioParserTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void ParsePositions_lit_toutes_les_positions_de_la_capture_reelle()
    {
        var positions = TradeRepublicPortfolioParser.ParsePositions(Fixture("tr-compactPortfolioByType.json"));

        Assert.Equal(8, positions.Count);
        Assert.All(positions, p => Assert.False(string.IsNullOrEmpty(p.Isin)));
        Assert.All(positions, p => Assert.True(p.Quantity > 0));
    }

    [Fact]
    public void ParsePositions_calcule_le_prix_de_revient_en_culture_invariante()
    {
        var positions = TradeRepublicPortfolioParser.ParsePositions(Fixture("tr-compactPortfolioByType.json"));
        var world = positions.Single(p => p.Isin == "IE00BK5BQT80");

        // 145,5963 × 62,443089 ≈ 9091,48 €, quel que soit le séparateur décimal local.
        Assert.Equal(145.5963m, world.AverageBuyIn);
        Assert.Equal(62.443089m, world.Quantity);
        Assert.Equal(9091.48m, Math.Round(world.CostBasis, 2));
        Assert.Equal("FTSE All-World USD (Acc)", world.Name);
    }

    [Fact]
    public void ParsePositions_gere_les_quantites_fractionnaires_fines()
    {
        var positions = TradeRepublicPortfolioParser.ParsePositions(Fixture("tr-compactPortfolioByType.json"));
        var leveraged = positions.Single(p => p.Isin == "LU0411078552");

        // Six décimales : la perte de précision se verrait sur le prix de revient.
        Assert.Equal(0.690567m, leveraged.Quantity);
    }

    [Fact]
    public void ParseTickerLastPrice_lit_le_dernier_cours()
    {
        var price = TradeRepublicPortfolioParser.ParseTickerLastPrice(Fixture("tr-ticker.json"));
        Assert.Equal(166.06m, price);
    }

    [Fact]
    public void ParseFirstExchange_prend_la_premiere_place_de_cotation()
    {
        var exchange = TradeRepublicPortfolioParser.ParseFirstExchange(Fixture("tr-instrument.json"));
        Assert.Equal("LSX", exchange);
    }

    [Fact]
    public void ParsePositions_serie_vide_sans_categories()
    {
        Assert.Empty(TradeRepublicPortfolioParser.ParsePositions("{}"));
        Assert.Empty(TradeRepublicPortfolioParser.ParsePositions("{\"categories\":[]}"));
    }

    [Fact]
    public void ParseTickerLastPrice_null_si_absent()
    {
        Assert.Null(TradeRepublicPortfolioParser.ParseTickerLastPrice("{}"));
    }

    [Fact]
    public void ParsePositions_ignore_les_lignes_sans_prix_ou_quantite()
    {
        var json = "{\"categories\":[{\"positions\":[{\"isin\":\"X\",\"name\":\"Sans quantité\"}]}]}";
        Assert.Empty(TradeRepublicPortfolioParser.ParsePositions(json));
    }

    [Fact]
    public void DescribeCategories_resume_le_marquage_brut_de_chaque_position()
    {
        // Trace de diagnostic : Trade Republic ne documente pas comment elle marque une
        // obligation. Ce résumé, journalisé à l'import, donne les valeurs réelles au lieu
        // de les faire deviner.
        var resume = TradeRepublicPortfolioParser.DescribeCategories(Fixture("tr-compactPortfolioByType.json"));

        Assert.Contains("stocksAndETFs", resume);
        Assert.Contains("IE0007UPSEA3=fund", resume);
    }
}
