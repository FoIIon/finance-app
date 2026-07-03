namespace FinanceApp.API.Models;

/// <summary>
/// Enveloppe projet : budget cible pluri-mois (vacances camping, ski) financé sur les primes.
/// Distinct des SavingsGoals (objectifs d'épargne) et des Budgets par catégorie.
/// Les dépenses s'y rattachent via Transaction.ProjectEnvelopeId.
/// </summary>
public class ProjectEnvelope
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = "🏖️";
    /// <summary>Budget cible du projet. null = pas de cible fixée.</summary>
    public decimal? TargetBudget { get; set; }
    /// <summary>Note de financement libre, ex. « prime fin d'année 1626 € ».</summary>
    public string? FundingNote { get; set; }
    public bool IsArchived { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
}
