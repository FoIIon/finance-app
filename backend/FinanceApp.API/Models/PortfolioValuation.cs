namespace FinanceApp.API.Models;

/// <summary>
/// Valeur du portefeuille Trade Republic d'un dashboard à une date. Contrairement aux
/// valorisations par ligne reconstituées (quantité actuelle × cours ancien), cette série tient
/// compte des quantités détenues ce jour-là, des positions vendues depuis et des pertes
/// réalisées. C'est elle qui porte la courbe du patrimoine dès qu'elle existe.
///
/// Source Reconstructed (28/08/2026) : Trade Republic a refusé les topics d'agrégat
/// (portfolioAggregateHistory, portfolioAggregateHistoryLight : BAD_SUBSCRIPTION_TYPE), la
/// série est donc rebâtie depuis la timeline complète et les cours, voir
/// InvestmentCalculator.ReconstructPortfolioHistory.
/// </summary>
public class PortfolioValuation
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    /// <summary>Date de la valeur (jour de bourse), jamais la date d'import.</summary>
    public DateTime AsOf { get; set; }
    /// <summary>Valeur totale des positions Trade Republic, hors espèces, en euros.</summary>
    public decimal MarketValue { get; set; }
    /// <summary>
    /// Capital investi net à cette date (achats moins ventes, cumulés depuis le premier ordre).
    /// L'écart valeur − investi net est le résultat total, pertes et gains réalisés compris.
    /// Null quand la source ne le connaît pas.
    /// </summary>
    public decimal? Invested { get; set; }
    public ValuationSource Source { get; set; } = ValuationSource.TradeRepublic;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
}
