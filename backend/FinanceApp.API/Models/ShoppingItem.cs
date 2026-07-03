namespace FinanceApp.API.Models;

/// <summary>
/// Article de la liste « à acheter » : mini-backlog d'achats prévus avec estimation.
/// </summary>
public class ShoppingItem
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Label { get; set; } = string.Empty;
    /// <summary>Coût estimé de l'article. null = pas d'estimation.</summary>
    public decimal? EstimatedCost { get; set; }
    public bool IsDone { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
}
