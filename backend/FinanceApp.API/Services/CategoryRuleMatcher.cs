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
    /// <summary>
    /// Vrai si le mot-clé apparaît dans le libellé, dans la contrepartie, ou — quand le mot-clé est
    /// lui-même un IBAN — dans le compte du bénéficiaire (31/08/2026). Un bénéficiaire garde son IBAN
    /// quand la banque change l'orthographe de son nom : la commune de Marche facture tantôt
    /// « Ville de Marche-en-Famenne », tantôt « ADMINISTRATION COMMUNALE DE MARCHE- », libellé vide.
    /// </summary>
    public static bool Matches(string keyword, string description, string? counterpartyName, string? counterpartyIban = null)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return false;
        return description.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || (counterpartyName != null && counterpartyName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            || MatchesIban(counterpartyIban, keyword);
    }

    /// <summary>
    /// La saisie a-t-elle la forme d'un IBAN : deux lettres, deux chiffres, six caractères au moins.
    ///
    /// Sans ce garde-fou, un mot-clé de trois lettres comme « DVV » ou « CNS » matcherait le code banque
    /// d'un IBAN étranger, et dans la recherche de transactions, taper « a » sortait tous les IBAN
    /// contenant un A (NL91ABNA…). Partagé entre les règles et la recherche pour cette raison.
    /// </summary>
    public static bool LooksLikeIban(string valeur)
    {
        var candidate = GoCardlessTransactionFields.Normalize(valeur);
        if (candidate.Length < 6) return false;

        return char.IsLetter(candidate[0]) && char.IsLetter(candidate[1])
            && char.IsDigit(candidate[2]) && char.IsDigit(candidate[3]);
    }

    /// <summary>L'IBAN ne se compare qu'à un mot-clé qui ressemble à un IBAN (LooksLikeIban).</summary>
    private static bool MatchesIban(string? counterpartyIban, string keyword)
    {
        if (counterpartyIban == null || !LooksLikeIban(keyword)) return false;

        return counterpartyIban.Contains(GoCardlessTransactionFields.Normalize(keyword), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// La première règle qui matche, dans l'ordre reçu. L'appelant trie du mot-clé le plus long au
    /// plus court (règle de nature du 27/08/2026 : « Legumes vacances » bat « Vacance »).
    /// </summary>
    public static CategoryRule? FirstMatch(IEnumerable<CategoryRule> orderedRules, string description, string? counterpartyName, string? counterpartyIban = null)
    {
        foreach (var rule in orderedRules)
        {
            if (Matches(rule.Keyword, description, counterpartyName, counterpartyIban)) return rule;
        }
        return null;
    }

    /// <summary>Ordre d'application : le mot-clé le plus long gagne, puis le plus ancien.</summary>
    public static IOrderedQueryable<CategoryRule> InApplicationOrder(IQueryable<CategoryRule> rules) =>
        rules.OrderByDescending(cr => cr.Keyword.Length).ThenBy(cr => cr.Id);
}
