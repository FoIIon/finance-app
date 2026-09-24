using FinanceApp.API.Models;
using FinanceApp.API.Services.Text;

namespace FinanceApp.API.Services.Reporting;

/// <summary>
/// Une transaction réelle réduite à ce qu'il faut pour régler une occurrence de routine. <paramref name="Date"/>
/// est le jour de la transaction dans le fuseau du ménage, pas le jour UTC, comme <see cref="Services.PaymentCandidate"/>.
/// <paramref name="RecurringTransactionId"/> : le lien posé par le provisionnement ou par le geste manuel.
/// </summary>
public sealed record SettlementCandidate(
    int Id,
    DateOnly Date,
    decimal Amount,
    TransactionType Type,
    string Description,
    string? CounterpartyName,
    int? RecurringTransactionId);

/// <summary>
/// Retrouve, pour une occurrence de récurrente, la transaction du mois qui la règle. Pur, statique, sur le
/// modèle d'<see cref="Services.EcheanceMatcher"/> : les candidats sont préparés par l'exécuteur, seules les
/// règles vivent ici, et deux passes sur les mêmes données rendent le même résultat.
///
/// Trois règles dans l'ordre, la première qui donne un candidat gagne : le lien (<c>RecurringTransactionId</c>),
/// le montant au centime (les mensualités fixes), le mot et la fourchette (l'énergie, dont le montant bouge
/// mais dont le libellé porte le fournisseur). Un faux positif cache une facture non payée derrière une coche
/// verte, et rien ne permet encore de le contester : d'où des règles étroites. Hors du mois calendaire, d'un
/// autre sens ou déjà pris, rien ne règle rien. Les règles 2 et 3 exigent en plus une transaction à dix jours
/// au plus du jour théorique, la règle 3 un mot entier du libellé qui ne soit pas un mot générique
/// (« crédit », « salaire », « épargne » marqueraient n'importe quoi).
/// </summary>
public static class RecurringSettlement
{
    /// <summary>Fourchette de la règle 3, bornes incluses : 300 et 500 règlent une récurrente à 400.</summary>
    public const decimal RangeLow = 0.75m;
    public const decimal RangeHigh = 1.25m;

    /// <summary>Un mot plus court n'identifie pas un fournisseur : « TV » ou « CBC » marqueraient n'importe quoi.</summary>
    public const int MinKeywordLength = 4;

    /// <summary>
    /// Règles 2 et 3 : la transaction est à ce nombre de jours au plus du jour théorique, borne incluse. Engie
    /// prélevé le 16 pour le 24 passe, un virement du 20 pour une occurrence du 7 ne passe pas. La règle 1 (le
    /// lien) n'a pas cette garde : c'est le ménage ou le provisionnement qui l'a posé.
    /// </summary>
    public const int MaxDaysFromDue = 10;

    /// <summary>
    /// Mots repliés (minuscules sans accents) qui ne désignent pas un fournisseur et que la règle 3 saute :
    /// « Crédit logement » cherche « logement », « Salaire Sébastien » cherche « sebastien », « Épargne perso
    /// CBC » ne cherche rien (« cbc » est trop court) et la règle 3 ne s'applique pas.
    /// </summary>
    public static readonly IReadOnlySet<string> ExcludedKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "credit", "salaire", "mensualite", "epargne", "cotisation", "acompte", "contribution", "paiement", "virement",
        "prelevement", "facture", "loyer", "assurance", "compte", "remboursement", "achat", "carte", "part", "perso",
    };

    /// <summary>
    /// Le candidat qui règle l'occurrence, ou null.
    /// </summary>
    /// <param name="r">La récurrente, avec son sens, son montant et son libellé.</param>
    /// <param name="occurrence">La date théorique de l'occurrence : son mois calendaire borne les candidats, sa distance sert aux règles 2 et 3.</param>
    /// <param name="candidates">Transactions réelles des comptes du dashboard, jamais provisionnelles.</param>
    /// <param name="alreadyClaimed">Ids déjà retenus dans cette passe : une transaction ne règle qu'une occurrence.</param>
    public static SettlementCandidate? Settle(RecurringTransaction r, DateOnly occurrence, IEnumerable<SettlementCandidate> candidates, ISet<int> alreadyClaimed)
    {
        var expected = Math.Abs(r.Amount);
        var keyword = Keyword(r.Description);

        // Le périmètre commun aux trois règles : le mois de l'occurrence, le sens de la récurrente, pas déjà pris.
        var pool = candidates
            .Where(c => !alreadyClaimed.Contains(c.Id))
            .Where(c => c.Type == r.Type)
            .Where(c => c.Date.Year == occurrence.Year && c.Date.Month == occurrence.Month)
            .ToList();

        // Règle 1 : le lien, sans condition de montant, de libellé ni de distance.
        return Best(pool.Where(c => c.RecurringTransactionId == r.Id), occurrence)
            // Règle 2 : le montant au centime, à dix jours au plus. Un candidat lié à une autre récurrente est à elle.
            ?? Best(pool.Where(c => Unlinked(c) && Near(c, occurrence) && Math.Abs(c.Amount) == expected), occurrence)
            // Règle 3 : le mot du libellé, entier, dans le libellé ou la contrepartie, le montant dans la fourchette, à dix jours au plus.
            ?? Best(pool.Where(c => Unlinked(c) && Near(c, occurrence) && keyword != null && Mentions(c, keyword) && InRange(Math.Abs(c.Amount), expected)), occurrence);
    }

    /// <summary>
    /// Le premier mot du libellé d'au moins <see cref="MinKeywordLength"/> caractères qui ne soit pas dans
    /// <see cref="ExcludedKeywords"/>, replié (minuscules sans accents), ou null s'il n'y en a pas. « Audrey 45€
    /// (Santé) » donne « audrey », « TV Proximus » donne « proximus », « ENGIE — gaz/électricité » donne « engie »,
    /// « Crédit logement » donne « logement ».
    /// </summary>
    public static string? Keyword(string? description) =>
        Words(description).FirstOrDefault(w => w.Length >= MinKeywordLength && !ExcludedKeywords.Contains(w));

    /// <summary>Les mots d'un texte replié : suites de lettres et de chiffres, tout le reste sépare.</summary>
    public static IEnumerable<string> Words(string? value)
    {
        var folded = TextFold.Fold(value);
        var start = -1;
        for (var i = 0; i <= folded.Length; i++)
        {
            var isWordChar = i < folded.Length && char.IsLetterOrDigit(folded[i]);
            if (isWordChar)
            {
                if (start < 0) start = i;
                continue;
            }
            if (start >= 0)
            {
                yield return folded[start..i];
                start = -1;
            }
        }
    }

    private static bool Unlinked(SettlementCandidate c) => c.RecurringTransactionId == null;

    private static bool Near(SettlementCandidate c, DateOnly occurrence) => DaysFrom(c, occurrence) <= MaxDaysFromDue;

    /// <summary>Mot entier : « credit » ne marque ni « creditcard » ni « accreditation ».</summary>
    private static bool Mentions(SettlementCandidate c, string keyword) =>
        Words(c.Description).Contains(keyword, StringComparer.Ordinal)
        || Words(c.CounterpartyName).Contains(keyword, StringComparer.Ordinal);

    private static bool InRange(decimal amount, decimal expected) =>
        amount >= expected * RangeLow && amount <= expected * RangeHigh;

    private static int DaysFrom(SettlementCandidate c, DateOnly occurrence) => Math.Abs(c.Date.DayNumber - occurrence.DayNumber);

    /// <summary>Le plus proche de la date théorique, puis le plus petit Id : le résultat ne dépend pas de l'ordre de lecture.</summary>
    private static SettlementCandidate? Best(IEnumerable<SettlementCandidate> matches, DateOnly occurrence) =>
        matches
            .OrderBy(c => DaysFrom(c, occurrence))
            .ThenBy(c => c.Id)
            .FirstOrDefault();
}
