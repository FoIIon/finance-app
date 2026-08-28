using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// L'unique définition de « cette règle matche cette transaction ». Avant le 28/08/2026 le prédicat
/// existait en quatre copies (sync GoCardless, sync Trade Republic, recatégorisation, routage perso)
/// et avait déjà divergé : la copie TR ne regardait que le libellé. Toute évolution du matching
/// (accents, mot entier, trim) se fait ici et nulle part ailleurs.
/// </summary>
public static class CategoryRuleMatcher
{
    /// <summary>Vrai si le mot-clé apparaît dans le libellé ou dans la contrepartie, sans casse.</summary>
    public static bool Matches(string keyword, string description, string? counterpartyName)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return false;
        return description.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (counterpartyName != null && counterpartyName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// La première règle qui matche, dans l'ordre reçu. L'appelant trie du mot-clé le plus long au
    /// plus court (règle de nature du 27/08/2026 : « Legumes vacances » bat « Vacance »).
    /// </summary>
    public static CategoryRule? FirstMatch(IEnumerable<CategoryRule> orderedRules, string description, string? counterpartyName)
    {
        foreach (var rule in orderedRules)
        {
            if (Matches(rule.Keyword, description, counterpartyName)) return rule;
        }
        return null;
    }

    /// <summary>Ordre d'application : le mot-clé le plus long gagne, puis le plus ancien.</summary>
    public static IOrderedQueryable<CategoryRule> InApplicationOrder(IQueryable<CategoryRule> rules) =>
        rules.OrderByDescending(cr => cr.Keyword.Length).ThenBy(cr => cr.Id);
}
