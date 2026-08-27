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
    /// <summary>
    /// Exclut la catégorie du bilan mensuel (blocs ENTRÉES / FIXE / MISES DE CÔTÉ / VARIABLE et du TOTAL).
    /// Réservé aux mouvements dont le montant du mois M ne se décide pas dans le mois M, et qu'y soustraire
    /// revient à compter le même euro deux fois : le balayage du compte joint vers le livret (« tout ce qui
    /// excède 3000 le 7 du mois », donc le reliquat de M−1) et les virements entre deux comptes suivis.
    /// Ces transactions restent visibles en ligne d'information sous le bilan, et continuent d'alimenter
    /// le solde des comptes manuels et le patrimoine. Ne PAS confondre avec une mise de côté volontaire
    /// (achat de titres, ordre permanent décidé dans le mois), qui doit rester soustraite.
    /// </summary>
    public bool ExcludeFromMonthlyReport { get; set; }
    public int? UserId { get; set; }

    public User? User { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
