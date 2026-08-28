namespace FinanceApp.API.Models;

/// <summary>
/// Valeur réelle du portefeuille Trade Republic d'un dashboard à une date, telle que Trade
/// Republic la sert pour son propre graphe (série agrégée). Contrairement aux valorisations par
/// ligne reconstituées (quantité actuelle × cours ancien), cette série tient compte des quantités
/// détenues ce jour-là, des positions vendues depuis et des pertes réalisées. C'est elle qui
/// porte la courbe du patrimoine dès qu'elle existe.
/// </summary>
public class PortfolioValuation
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    /// <summary>Date de la valeur (jour de bourse), jamais la date d'import.</summary>
    public DateTime AsOf { get; set; }
    /// <summary>Valeur totale des positions Trade Republic, hors espèces, en euros.</summary>
    public decimal MarketValue { get; set; }
    public ValuationSource Source { get; set; } = ValuationSource.TradeRepublic;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
}
