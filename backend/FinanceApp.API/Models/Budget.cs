namespace FinanceApp.API.Models;

public class Budget
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public int CategoryId { get; set; }
    public decimal Amount { get; set; }
    public BudgetPeriod Period { get; set; } = BudgetPeriod.Monthly;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
    public Category Category { get; set; } = null!;
}

public enum BudgetPeriod
{
    Monthly = 0,
    Yearly = 1,
}
