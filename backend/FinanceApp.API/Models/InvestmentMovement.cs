namespace FinanceApp.API.Models;

/// <summary>
/// Achat, vente, dividende ou frais sur une ligne. Alimentée par l'import Trade Republic.
/// Table créée au lot 1 pour n'avoir qu'une migration, mais non alimentée avant le lot 4 :
/// aucun endpoint d'écriture n'existe encore.
/// </summary>
public class InvestmentMovement
{
    public int Id { get; set; }
    public int InvestmentId { get; set; }
    public MovementType Type { get; set; }
    public DateTime Date { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    /// <summary>Montant total signé, en euros.</summary>
    public decimal Amount { get; set; }
    /// <summary>Identifiant côté courtier. Unique, pour la déduplication à l'import.</summary>
    public string? ExternalId { get; set; }
    public InvestmentSource Source { get; set; } = InvestmentSource.Manual;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Investment Investment { get; set; } = null!;
}
