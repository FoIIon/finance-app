namespace FinanceApp.API.Models;

public class DashboardAccount
{
    public int DashboardId { get; set; }
    public int AccountId { get; set; }

    public Dashboard Dashboard { get; set; } = null!;
    public Account Account { get; set; } = null!;
}
