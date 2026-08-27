namespace FinanceApp.API.Models;

public class CategoryRule
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Keyword { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    /// <summary>Les transactions matchées sont marquées charge fixe (Transaction.IsFixed).</summary>
    public bool MarkAsFixed { get; set; }

    /// <summary>
    /// Route la transaction matchée vers le dashboard Perso (au lieu du Commun). Réservé aux dépenses
    /// carte Trade Republic qui sont des achats personnels de Sébastien (abos Anthropic, Orange…),
    /// qu'il ne rembourse pas depuis le compte commun. Une dépense TR est commune par défaut : on ne
    /// tente jamais de déduire le perso du remboursement, ce qui escamoterait une dépense commune pas
    /// encore remboursée. Voir PersoScopeRouter.
    /// </summary>
    public bool RouteToPerso { get; set; }

    public User User { get; set; } = null!;
    public Category Category { get; set; } = null!;
}
