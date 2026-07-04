namespace FinanceApp.API.Models;

public class RecurringTransaction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int DashboardId { get; set; }
    public int? AccountId { get; set; }
    public int? CategoryId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty; // "Income" | "Expense"
    public string Frequency { get; set; } = string.Empty; // "Weekly" | "Monthly" | "Yearly"
    public int? DayOfMonth { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>Matérialiser une transaction provisionnelle au 1er du mois (montant = moyenne des
    /// derniers versements réels), réconciliée automatiquement à l'arrivée du versement réel.
    /// Pensé pour le salaire versé en fin de mois. Nécessite une catégorie.</summary>
    public bool ProvisionAtMonthStart { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Dashboard Dashboard { get; set; } = null!;
    public Account? Account { get; set; }
    public Category? Category { get; set; }
}
