namespace FinanceApp.API.Models;

public class DashboardMember
{
    public int DashboardId { get; set; }
    public int UserId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
    public User User { get; set; } = null!;
}
