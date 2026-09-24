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
/// mais dont le libellé porte le fournisseur). « Salaire » ou « Audrey » seuls marqueraient n'importe quoi,
/// la fourchette est la garde. Un faux positif cache une facture non payée derrière une coche verte, d'où
/// des règles étroites : hors du mois calendaire, d'un autre sens ou déjà pris, rien ne règle rien.
/// </summary>
public static class RecurringSettlement
{
    /// <summary>Fourchette de la règle 3, bornes incluses : 300 et 500 règlent une récurrente à 400.</summary>
    public const decimal RangeLow = 0.75m;
    public const decimal RangeHigh = 1.25m;

    /// <summary>Un mot plus court n'identifie pas un fournisseur : « TV » marquerait n'importe quoi.</summary>
    public const int MinKeywordLength = 4;

    /// <summary>
    /// Le candidat qui règle l'occurrence, ou null.
    /// </summary>
    /// <param name="r">La récurrente, avec son sens, son montant et son libellé.</param>
    /// <param name="occurrence">La date théorique de l'occurrence : seul son mois calendaire compte.</param>
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

        // Règle 1 : le lien, sans condition de montant ni de libellé.
        return Best(pool.Where(c => c.RecurringTransactionId == r.Id), occurrence)
            // Règle 2 : le montant au centime. Un candidat lié à une autre récurrente est à elle.
            ?? Best(pool.Where(c => Unlinked(c) && Math.Abs(c.Amount) == expected), occurrence)
            // Règle 3 : le mot du libellé dans le libellé ou la contrepartie, et le montant dans la fourchette.
            ?? Best(pool.Where(c => Unlinked(c) && keyword != null && Mentions(c, keyword) && InRange(Math.Abs(c.Amount), expected)), occurrence);
    }

    /// <summary>
    /// Le premier mot du libellé d'au moins <see cref="MinKeywordLength"/> lettres, replié (minuscules sans
    /// accents), ou null s'il n'y en a pas. « Audrey 45€ (Santé) » donne « audrey », « TV Proximus » donne
    /// « proximus », « ENGIE — gaz/électricité » donne « engie ».
    /// </summary>
    public static string? Keyword(string? description)
    {
        var folded = TextFold.Fold(description);
        var start = -1;
        for (var i = 0; i <= folded.Length; i++)
        {
            var isLetter = i < folded.Length && char.IsLetter(folded[i]);
            if (isLetter)
            {
                if (start < 0) start = i;
                continue;
            }
            if (start >= 0)
            {
                if (i - start >= MinKeywordLength) return folded[start..i];
                start = -1;
            }
        }
        return null;
    }

    private static bool Unlinked(SettlementCandidate c) => c.RecurringTransactionId == null;

    private static bool Mentions(SettlementCandidate c, string keyword) =>
        TextFold.Fold(c.Description).Contains(keyword, StringComparison.Ordinal)
        || TextFold.Fold(c.CounterpartyName).Contains(keyword, StringComparison.Ordinal);

    private static bool InRange(decimal amount, decimal expected) =>
        amount >= expected * RangeLow && amount <= expected * RangeHigh;

    /// <summary>Le plus proche de la date théorique, puis le plus petit Id : le résultat ne dépend pas de l'ordre de lecture.</summary>
    private static SettlementCandidate? Best(IEnumerable<SettlementCandidate> matches, DateOnly occurrence) =>
        matches
            .OrderBy(c => Math.Abs(c.Date.DayNumber - occurrence.DayNumber))
            .ThenBy(c => c.Id)
            .FirstOrDefault();
}
