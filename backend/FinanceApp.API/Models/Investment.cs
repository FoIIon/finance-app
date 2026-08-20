namespace FinanceApp.API.Models;

/// <summary>
/// Une ligne détenue du patrimoine investi : un titre coté, une quantité de métal
/// ou un contrat d'assurance-vie. Rattachée au dashboard, comme SavingsGoal et ProjectEnvelope.
/// </summary>
public class Investment
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Titulaire, texte libre (ex. « Sébastien », « Audrey », « Commun »). Permet le total combiné.</summary>
    public string Holder { get; set; } = string.Empty;
    public InvestmentKind Kind { get; set; }
    public string? Isin { get; set; }
    /// <summary>Code métal, ex. XAU (or), XAG (argent).</summary>
    public string? MetalCode { get; set; }
    /// <summary>Vaut 1 par convention pour un contrat d'assurance-vie.</summary>
    public decimal Quantity { get; set; }
    public InvestmentUnit Unit { get; set; }
    /// <summary>Total réellement versé, en euros.</summary>
    public decimal CostBasis { get; set; }
    /// <summary>Sans cette date, aucun rendement annualisé n'est affiché.</summary>
    public DateTime? FirstPurchaseDate { get; set; }
    public InvestmentSource Source { get; set; } = InvestmentSource.Manual;
    /// <summary>Identifiant côté courtier, pour la réconciliation à l'import.</summary>
    public string? ExternalId { get; set; }
    public bool IsArchived { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
    public ICollection<InvestmentValuation> Valuations { get; set; } = new List<InvestmentValuation>();
    public ICollection<InvestmentMovement> Movements { get; set; } = new List<InvestmentMovement>();
}
