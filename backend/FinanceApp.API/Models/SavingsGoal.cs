namespace FinanceApp.API.Models;

public class SavingsGoal
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public DateTime TargetDate { get; set; }
    public decimal CurrentAmount { get; set; } = 0;
    /// <summary>Catégorie liée pour le calcul automatique du cumulé (optionnel).</summary>
    public int? CategoryId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
    public Category? Category { get; set; }
}
