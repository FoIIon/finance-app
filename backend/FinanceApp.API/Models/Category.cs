namespace FinanceApp.API.Models;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    /// <summary>Marque les catégories qui ne sont PAS des vraies dépenses (transferts internes : épargne, comptes joints, etc.). Exclues des stats dépenses.</summary>
    public bool IsTransfer { get; set; }
    /// <summary>Marque les charges fixes récurrentes (prêt, énergie, crèche, assurances…). Utilisé par le bilan mensuel pour séparer le bloc FIXE du bloc VARIABLE.</summary>
    public bool IsFixed { get; set; }
    public int? UserId { get; set; }

    public User? User { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
