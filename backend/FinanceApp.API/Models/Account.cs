namespace FinanceApp.API.Models;

public class Account
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Le compte logique « Perso » de l'utilisateur, cible du routage PersoScopeRouter. Un seul par
    /// utilisateur (index unique filtré). Identifie le compte à la place de son nom, qui reste libre.
    /// </summary>
    public bool IsPersonalScope { get; set; }
    /// <summary>
    /// Le compte logique principal de l'utilisateur, créé à l'inscription : cible des imports bancaires
    /// communs. Un seul par utilisateur (index unique filtré). Remplace la convention « le plus ancien
    /// compte non perso », que rien ne garantissait.
    /// </summary>
    public bool IsPrimary { get; set; }

    public User User { get; set; } = null!;
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<DashboardAccount> DashboardAccounts { get; set; } = new List<DashboardAccount>();
}
