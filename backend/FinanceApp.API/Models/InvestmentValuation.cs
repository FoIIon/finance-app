namespace FinanceApp.API.Models;

/// <summary>
/// Valeur d'une ligne à une date. On n'écrase jamais une valorisation, on en empile une nouvelle :
/// c'est ce qui produit la courbe du patrimoine et ce qui empêche une correction
/// de réécrire l'historique rétroactivement.
/// </summary>
public class InvestmentValuation
{
    public int Id { get; set; }
    public int InvestmentId { get; set; }
    /// <summary>Date de la valeur (relevé, cours), jamais la date de saisie.</summary>
    public DateTime AsOf { get; set; }
    /// <summary>Cours unitaire quand il est connu. Null pour un relevé qui ne donne qu'un total.</summary>
    public decimal? UnitPrice { get; set; }
    /// <summary>Valeur totale de la ligne, en euros.</summary>
    public decimal MarketValue { get; set; }
    public ValuationSource Source { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Investment Investment { get; set; } = null!;
}
