namespace FinanceApp.API.Models;

public class Dashboard
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CreatorId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Creator { get; set; } = null!;
    public ICollection<DashboardMember> Members { get; set; } = new List<DashboardMember>();
    public ICollection<DashboardAccount> DashboardAccounts { get; set; } = new List<DashboardAccount>();
}
