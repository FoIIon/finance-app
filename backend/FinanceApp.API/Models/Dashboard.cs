namespace FinanceApp.API.Models;

public class Dashboard
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int CreatorId { get; set; }
    /// <summary>
    /// Le dashboard personnel de l'utilisateur, créé à l'inscription. Un seul par créateur (index unique
    /// filtré). Avant le 02/09/2026 il se déduisait de « le plus ancien des dashboards du créateur », une
    /// convention qu'aucune colonne ne portait et qu'une suppression suivie d'une recréation cassait en silence.
    /// </summary>
    public bool IsPersonal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User Creator { get; set; } = null!;
    public ICollection<DashboardMember> Members { get; set; } = new List<DashboardMember>();
    public ICollection<DashboardAccount> DashboardAccounts { get; set; } = new List<DashboardAccount>();
}
