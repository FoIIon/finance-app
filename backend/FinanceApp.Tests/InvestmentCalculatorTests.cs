using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Xunit;

namespace FinanceApp.Tests;

public class InvestmentCalculatorTests
{
    [Fact]
    public void ComputeUnitCost_Security_DivisesCostBasisByQuantity()
    {
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.Security, 1000m, 8m);
        Assert.Equal(125m, result);
    }

    [Fact]
    public void ComputeUnitCost_InsuranceContract_ReturnsNull()
    {
        // Un contrat a une quantité de 1 par convention : le PRU se confondrait
        // avec le montant versé sans rien apprendre.
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.InsuranceContract, 5000m, 1m);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeUnitCost_ZeroQuantity_ReturnsNull()
    {
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.Metal, 3000m, 0m);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeUnitCost_FractionalQuantity_KeepsPrecision()
    {
        // Les quantités Trade Republic descendent à six décimales.
        var result = InvestmentCalculator.ComputeUnitCost(InvestmentKind.Security, 100m, 0.512345m);
        Assert.Equal(195.18m, Math.Round(result!.Value, 2));
    }

    [Fact]
    public void ComputeGain_PositiveGain_ReturnsAmountAndPercent()
    {
        var (amount, percent) = InvestmentCalculator.ComputeGain(1000m, 1250m);
        Assert.Equal(250m, amount);
        Assert.Equal(25m, percent);
    }

    [Fact]
    public void ComputeGain_Loss_ReturnsNegativeValues()
    {
        var (amount, percent) = InvestmentCalculator.ComputeGain(1000m, 800m);
        Assert.Equal(-200m, amount);
        Assert.Equal(-20m, percent);
    }

    [Fact]
    public void ComputeGain_NoValuation_ReturnsNulls()
    {
        var (amount, percent) = InvestmentCalculator.ComputeGain(1000m, null);
        Assert.Null(amount);
        Assert.Null(percent);
    }

    [Fact]
    public void ComputeGain_ZeroCostBasis_ReturnsAmountButNoPercent()
    {
        // Une ligne reçue en donation a un coût nul : le pourcentage n'a pas de sens,
        // le gain en euros si.
        var (amount, percent) = InvestmentCalculator.ComputeGain(0m, 500m);
        Assert.Equal(500m, amount);
        Assert.Null(percent);
    }

    [Fact]
    public void ComputeCagr_NoFirstPurchaseDate_ReturnsNull()
    {
        // Règle non négociable de la spec : pas de date d'entrée, pas de rendement.
        // Une case vide vaut mieux qu'un chiffre reposant sur une hypothèse invisible.
        var result = InvestmentCalculator.ComputeCagr(1000m, 1500m, null, new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCagr_HoldingShorterThanOneYear_ReturnsNull()
    {
        // Annualiser six mois de détention produit un chiffre spectaculaire et faux.
        var result = InvestmentCalculator.ComputeCagr(
            1000m, 1200m, new DateTime(2026, 3, 1), new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCagr_TwoYearsDoubling_ReturnsAboutFortyOnePercent()
    {
        // 1000 qui devient 2000 sur deux ans. L'année conventionnelle de 365,25 jours donne 41,45 %.
        var result = InvestmentCalculator.ComputeCagr(
            1000m, 2000m, new DateTime(2024, 7, 28), new DateTime(2026, 7, 28));
        Assert.NotNull(result);
        Assert.Equal(41.45m, Math.Round(result!.Value, 2));
    }

    [Fact]
    public void ComputeCagr_NoValuation_ReturnsNull()
    {
        var result = InvestmentCalculator.ComputeCagr(1000m, null, new DateTime(2020, 1, 1), new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void ComputeCagr_ZeroOrNegativeCostBasis_ReturnsNull()
    {
        var result = InvestmentCalculator.ComputeCagr(0m, 500m, new DateTime(2020, 1, 1), new DateTime(2026, 7, 28));
        Assert.Null(result);
    }

    [Fact]
    public void IsStale_ManualWithinThirtyDays_IsFresh()
    {
        var now = new DateTime(2026, 7, 28);
        Assert.False(InvestmentCalculator.IsStale(ValuationSource.Manual, now.AddDays(-29), now));
    }

    [Fact]
    public void IsStale_ManualBeyondThirtyDays_IsStale()
    {
        var now = new DateTime(2026, 7, 28);
        Assert.True(InvestmentCalculator.IsStale(ValuationSource.Manual, now.AddDays(-31), now));
    }

    [Fact]
    public void IsStale_AutomaticBeyondFortyEightHours_IsStale()
    {
        var now = new DateTime(2026, 7, 28);
        Assert.True(InvestmentCalculator.IsStale(ValuationSource.SpotApi, now.AddHours(-49), now));
        Assert.False(InvestmentCalculator.IsStale(ValuationSource.SpotApi, now.AddHours(-47), now));
    }

    private static readonly DateTime Jan = new(2026, 1, 15);
    private static readonly DateTime Feb = new(2026, 2, 15);
    private static readonly DateTime Mar = new(2026, 3, 15);

    private static PortfolioLine Line(decimal costBasis, bool isArchived, params (DateTime AsOf, decimal MarketValue)[] valuations) =>
        new(costBasis, isArchived, valuations);

    [Fact]
    public void ComputePortfolioHistory_CarriesLastKnownValueBetweenMeasures()
    {
        // A valorisée en janvier et mars, B en février : le point de février doit
        // contenir A reportée, pas seulement B.
        var history = InvestmentCalculator.ComputePortfolioHistory(new[]
        {
            Line(1000m, false, (Jan, 1100m), (Mar, 1200m)),
            Line(500m, false, (Feb, 550m)),
        });

        Assert.Equal(3, history.Count);
        Assert.Equal(new PortfolioHistoryPoint(Jan, 1100m, 1000m, 1), history[0]);
        Assert.Equal(new PortfolioHistoryPoint(Feb, 1650m, 1500m, 2), history[1]);
        Assert.Equal(new PortfolioHistoryPoint(Mar, 1750m, 1500m, 2), history[2]);
    }

    [Fact]
    public void ComputePortfolioHistory_LineWithoutValuation_NeverAppears()
    {
        // Ni dans Value ni dans Invested : compter son coût sans sa valeur
        // creuserait un écart Value-Invested fictif.
        var history = InvestmentCalculator.ComputePortfolioHistory(new[]
        {
            Line(1000m, false, (Jan, 1100m)),
            Line(9999m, false),
        });

        var point = Assert.Single(history);
        Assert.Equal(1100m, point.Value);
        Assert.Equal(1000m, point.Invested);
        Assert.Equal(1, point.LinesIncluded);
    }

    [Fact]
    public void ComputePortfolioHistory_InvestedGrowsWhenALineGetsItsFirstValuation()
    {
        // Invested suit le même ensemble de lignes que Value : il augmente à la date
        // où une ligne reçoit sa première mesure, pas avant.
        var history = InvestmentCalculator.ComputePortfolioHistory(new[]
        {
            Line(1000m, false, (Jan, 1000m), (Feb, 1000m)),
            Line(2000m, false, (Feb, 2100m)),
        });

        Assert.Equal(1000m, history[0].Invested);
        Assert.Equal(3000m, history[1].Invested);
        Assert.Equal(2, history[1].LinesIncluded);
    }

    [Fact]
    public void ComputePortfolioHistory_ArchivedLine_StopsAfterItsLastValuation()
    {
        // Une ligne archivée contribue jusqu'à sa dernière mesure, puis sort de la
        // courbe : Value ET Invested baissent au point suivant.
        var history = InvestmentCalculator.ComputePortfolioHistory(new[]
        {
            Line(1000m, true, (Jan, 1100m), (Feb, 1150m)),
            Line(500m, false, (Jan, 500m), (Mar, 600m)),
        });

        Assert.Equal(new PortfolioHistoryPoint(Jan, 1600m, 1500m, 2), history[0]);
        Assert.Equal(new PortfolioHistoryPoint(Feb, 1650m, 1500m, 2), history[1]);
        Assert.Equal(new PortfolioHistoryPoint(Mar, 600m, 500m, 1), history[2]);
    }

    [Fact]
    public void ComputePortfolioHistory_NonArchivedLine_IsCarriedBeyondItsLastValuation()
    {
        // Le report de dernière valeur ne s'arrête que pour les lignes archivées.
        var history = InvestmentCalculator.ComputePortfolioHistory(new[]
        {
            Line(1000m, false, (Jan, 1100m)),
            Line(500m, false, (Mar, 600m)),
        });

        Assert.Equal(new PortfolioHistoryPoint(Mar, 1700m, 1500m, 2), history[1]);
    }

    [Fact]
    public void ComputePortfolioHistory_DatesAreSortedAndDeduplicated()
    {
        // Les valorisations arrivent dans le désordre, la série sort triée.
        var history = InvestmentCalculator.ComputePortfolioHistory(new[]
        {
            Line(1000m, false, (Mar, 1200m), (Jan, 1100m)),
            Line(500m, false, (Jan, 500m)),
        });

        Assert.Equal(2, history.Count);
        Assert.Equal(Jan, history[0].AsOf);
        Assert.Equal(Mar, history[1].AsOf);
    }

    [Fact]
    public void ComputePortfolioHistory_NoValuationsAtAll_ReturnsEmptySeries()
    {
        var history = InvestmentCalculator.ComputePortfolioHistory(new[]
        {
            Line(1000m, false),
            Line(500m, true),
        });

        Assert.Empty(history);
    }

    [Fact]
    public void ComputePortfolioHistory_SameDayOnTwoLines_ProducesASinglePoint()
    {
        var history = InvestmentCalculator.ComputePortfolioHistory(new[]
        {
            Line(1000m, false, (Feb, 1100m)),
            Line(500m, false, (Feb, 550m)),
        });

        var point = Assert.Single(history);
        Assert.Equal(new PortfolioHistoryPoint(Feb, 1650m, 1500m, 2), point);
    }

    // ---------------------------------------------------------------- série réelle Trade Republic

    private static PortfolioHistoryPoint Pt(string date, decimal value, decimal? invested = null, int lines = 1) =>
        new(DateTime.Parse(date), value, invested, lines);

    [Fact]
    public void MergeWithPortfolioSeries_SansSerieTr_RenvoieLaReconstitution()
    {
        var all = new List<PortfolioHistoryPoint> { Pt("2026-08-20", 1000m, 900m), Pt("2026-08-21", 1010m, 900m) };
        var result = InvestmentCalculator.MergeWithPortfolioSeries([], [], all);
        Assert.Equal(all, result);
    }

    [Fact]
    public void MergeWithPortfolioSeries_LaSerieTrPorteLaCourbe_InvestiInconnu()
    {
        // Trois ans de série TR, aucune autre ligne : la courbe est la série TR telle quelle,
        // et Investi est null, on ne l'invente pas.
        var tr = new List<(DateTime, decimal, decimal?)>
        {
            (DateTime.Parse("2023-01-02"), 500m, null),
            (DateTime.Parse("2024-01-02"), 800m, null),
            (DateTime.Parse("2025-01-02"), 700m, null),
        };
        var all = new List<PortfolioHistoryPoint> { Pt("2025-01-02", 950m, 900m) };

        var result = InvestmentCalculator.MergeWithPortfolioSeries(tr, [], all);

        Assert.Equal(3, result.Count);
        Assert.Equal(500m, result[0].Value);
        Assert.Equal(700m, result[2].Value);
        Assert.All(result, p => Assert.Null(p.Invested));
    }

    [Fact]
    public void MergeWithPortfolioSeries_AjouteLaDerniereValeurDesAutresLignes()
    {
        // L'or (ligne manuelle) valorisé le 15 : les points TR du 10 n'en ont rien, ceux du 20
        // l'ajoutent, à sa dernière valeur connue.
        var tr = new List<(DateTime, decimal, decimal?)>
        {
            (DateTime.Parse("2026-08-10"), 1000m, null),
            (DateTime.Parse("2026-08-20"), 1100m, null),
        };
        var autres = new List<PortfolioHistoryPoint> { Pt("2026-08-15", 200m, 180m) };
        var all = new List<PortfolioHistoryPoint> { Pt("2026-08-15", 200m, 180m), Pt("2026-08-20", 1300m, 1080m, 3) };

        var result = InvestmentCalculator.MergeWithPortfolioSeries(tr, autres, all);

        Assert.Equal(2, result.Count);
        Assert.Equal(1000m, result[0].Value);
        Assert.Equal(0, result[0].LinesIncluded);
        Assert.Equal(1300m, result[1].Value);
        Assert.Equal(1, result[1].LinesIncluded);
    }

    [Fact]
    public void MergeWithPortfolioSeries_ApresLeDernierPointTr_LaReconstitutionPrendLeRelais()
    {
        // Série TR arrêtée hier, snapshot du jour pris ce matin : le point du jour vient de la
        // reconstitution, avec son Investi.
        var tr = new List<(DateTime, decimal, decimal?)> { (DateTime.Parse("2026-08-27"), 1000m, null) };
        var all = new List<PortfolioHistoryPoint> { Pt("2026-08-27", 1005m, 900m), Pt("2026-08-28", 1020m, 900m) };

        var result = InvestmentCalculator.MergeWithPortfolioSeries(tr, [], all);

        Assert.Equal(2, result.Count);
        Assert.Equal(1000m, result[0].Value);
        Assert.Equal(DateTime.Parse("2026-08-28"), result[1].AsOf);
        Assert.Equal(1020m, result[1].Value);
        Assert.Equal(900m, result[1].Invested);
    }

    // ---------------------------------------------------------------- reconstruction depuis la timeline

    private static InvestmentCalculator.TimelineMovement Mv(string isin, string date, decimal amount) =>
        new(isin, DateTime.Parse(date), amount);

    private static IReadOnlyDictionary<string, IReadOnlyList<(DateTime AsOf, decimal Close)>> Prices(
        params (string Isin, (string Date, decimal Close)[] Serie)[] series) =>
        series.ToDictionary(
            s => s.Isin,
            s => (IReadOnlyList<(DateTime, decimal)>)s.Serie.Select(p => (DateTime.Parse(p.Date), p.Close)).ToList());

    [Fact]
    public void Reconstruct_AchatPuisHausse_ValeurSuitLeCours_InvestiFixe()
    {
        // 100 € d'ETF à 10 € le 1er : 10 parts. Le cours passe à 12 le 3 : valeur 120, investi 100.
        var prices = Prices(("ETF", new[] { ("2026-01-01", 10m), ("2026-01-02", 11m), ("2026-01-03", 12m) }));
        var (points, fills, sansCours) = InvestmentCalculator.ReconstructPortfolioHistory(
            [Mv("ETF", "2026-01-01", -100m)], prices, DateTime.Parse("2026-01-03"));

        Assert.Empty(sansCours);
        Assert.Single(fills);
        Assert.Equal(10m, fills[0].Quantity);
        Assert.Equal(3, points.Count);
        Assert.Equal((100m, 100m), (points[0].Value, points[0].Invested));
        Assert.Equal((120m, 100m), (points[2].Value, points[2].Invested));
    }

    [Fact]
    public void Reconstruct_VenteAPerte_PeseSurLeResultatTotal()
    {
        // 100 € achetés à 10, tout vendu à 8 (80 € encaissés) : plus rien en portefeuille,
        // investi net 20 €, valeur 0. L'écart −20 € est la perte réalisée, que la plus-value
        // latente des lignes ouvertes ne montrerait jamais.
        var prices = Prices(("ETF", new[] { ("2026-01-01", 10m), ("2026-01-05", 8m), ("2026-01-06", 9m) }));
        var (points, _, _) = InvestmentCalculator.ReconstructPortfolioHistory(
            [Mv("ETF", "2026-01-01", -100m), Mv("ETF", "2026-01-05", 80m)], prices, DateTime.Parse("2026-01-06"));

        var dernier = points[^1];
        Assert.Equal(0m, dernier.Value);
        Assert.Equal(20m, dernier.Invested);
    }

    [Fact]
    public void Reconstruct_PositionVendueNeComptePlusApres_MaisComptaitAvant()
    {
        var prices = Prices(
            ("A", new[] { ("2026-01-01", 10m), ("2026-01-10", 10m) }),
            ("B", new[] { ("2026-01-01", 5m), ("2026-01-10", 5m) }));
        var (points, _, _) = InvestmentCalculator.ReconstructPortfolioHistory(
            [Mv("A", "2026-01-01", -100m), Mv("B", "2026-01-01", -50m), Mv("B", "2026-01-10", 50m)],
            prices, DateTime.Parse("2026-01-10"));

        Assert.Equal(150m, points[0].Value);
        Assert.Equal(100m, points[^1].Value);
        Assert.Equal(100m, points[^1].Invested);
    }

    [Fact]
    public void Reconstruct_IsinSansCours_CompteDansInvestiPasDansValeur_EtSignale()
    {
        var prices = Prices(("A", new[] { ("2026-01-01", 10m) }));
        var (points, _, sansCours) = InvestmentCalculator.ReconstructPortfolioHistory(
            [Mv("A", "2026-01-01", -100m), Mv("ZZ", "2026-01-01", -40m)], prices, DateTime.Parse("2026-01-01"));

        Assert.Equal(["ZZ"], sansCours);
        Assert.Equal(100m, points[0].Value);
        Assert.Equal(140m, points[0].Invested);
    }

    [Fact]
    public void Reconstruct_SansMouvement_RienARebatir()
    {
        var (points, fills, sansCours) = InvestmentCalculator.ReconstructPortfolioHistory(
            [], Prices(), DateTime.Parse("2026-01-01"));
        Assert.Empty(points);
        Assert.Empty(fills);
        Assert.Empty(sansCours);
    }

    [Fact]
    public void MergeWithPortfolioSeries_InvestiDeLaSerie_AdditionneCeluiDesAutresLignes()
    {
        var tr = new List<(DateTime, decimal, decimal?)> { (DateTime.Parse("2026-08-20"), 1000m, 900m) };
        var autres = new List<PortfolioHistoryPoint> { Pt("2026-08-15", 200m, 180m) };
        var result = InvestmentCalculator.MergeWithPortfolioSeries(tr, autres, autres);

        Assert.Single(result);
        Assert.Equal(1200m, result[0].Value);
        Assert.Equal(1080m, result[0].Invested);
        Assert.True(result[0].Reconstructed);
    }

    [Fact]
    public void ReconstructCalibrated_PositionAnterieure_EntreEnOuvertureAuPremierJour()
    {
        // Timeline : un seul achat de 100 € à 10 le 01/01 (10 parts). Mais TR dit qu'on en détient 30 :
        // 20 parts achetées avant la timeline. Elles entrent le 01/01 à 10 €, soit 200 €, sans plus-value.
        var prices = Prices(("ETF", new[] { ("2025-12-20", 9m), ("2026-01-01", 10m), ("2026-01-05", 12m) }));
        var held = new Dictionary<string, decimal> { ["ETF"] = 30m };
        var (points, fills, _, openings) = InvestmentCalculator.ReconstructPortfolioHistoryCalibrated(
            [Mv("ETF", "2026-01-01", -100m)], prices, held, DateTime.Parse("2026-01-05"));

        Assert.Single(openings);
        Assert.True(openings[0].IsOpening);
        Assert.Equal(-200m, openings[0].Amount);
        Assert.Equal(DateTime.Parse("2026-01-01"), openings[0].Date);
        Assert.Equal((300m, 300m), (points[0].Value, points[0].Invested));
        Assert.Equal((360m, 300m), (points[^1].Value, points[^1].Invested));
        Assert.Equal(30m, fills.Sum(f => f.Quantity));
    }

    [Fact]
    public void ReconstructCalibrated_EcartNegatif_CorrigeVersLaQuantiteReelle()
    {
        // Approximation par le cours : 10,5 parts rebâties pour 10 détenues. L'ouverture est une
        // correction négative, la valeur finale colle à la quantité réelle.
        var prices = Prices(("ETF", new[] { ("2026-01-01", 10m), ("2026-01-05", 20m) }));
        var held = new Dictionary<string, decimal> { ["ETF"] = 10m };
        var (points, fills, _, openings) = InvestmentCalculator.ReconstructPortfolioHistoryCalibrated(
            [Mv("ETF", "2026-01-01", -105m)], prices, held, DateTime.Parse("2026-01-05"));

        Assert.Single(openings);
        Assert.Equal(5m, openings[0].Amount);
        Assert.Equal(10m, fills.Sum(f => f.Quantity));
        Assert.Equal(200m, points[^1].Value);
    }

    [Fact]
    public void ReconstructCalibrated_QuantiteDejaJuste_AucuneOuverture()
    {
        var prices = Prices(("ETF", new[] { ("2026-01-01", 10m) }));
        var held = new Dictionary<string, decimal> { ["ETF"] = 10m };
        var (_, _, _, openings) = InvestmentCalculator.ReconstructPortfolioHistoryCalibrated(
            [Mv("ETF", "2026-01-01", -100m)], prices, held, DateTime.Parse("2026-01-01"));
        Assert.Empty(openings);
    }
}
