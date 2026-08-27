namespace FinanceApp.API.Services;

/// <summary>Une transaction bancaire déjà en base, réduite à ce qu'il faut pour la rapprocher d'un mouvement courtier.</summary>
public record TransferLeg(int Id, DateTime Date, decimal Amount, bool IsExpense, string? CounterpartyName);

/// <summary>
/// Retrouve, pour un mouvement interne côté Trade Republic, le débit bancaire qui en est l'autre jambe.
///
/// Le problème (audit du 27/08/2026) : quand la carte TR tire 171,70 € sur le compte joint, GoCardless
/// livre un débit de 171,70 € dont la contrepartie est Sébastien lui-même, et TR livre le crédit
/// correspondant. Les deux lignes sont le même euro. Tant que le débit reste catégorisé en dépense,
/// le paiement carte le compte une seconde fois.
///
/// Le rapprochement est volontairement étroit. Un faux positif escamote une dépense réelle du bilan,
/// ce qui est bien plus grave qu'un faux négatif, lequel laisse simplement un doublon visible à trier
/// à la main. D'où les trois verrous cumulés : montant au centime, trois jours d'écart au plus, et
/// contrepartie qui nomme un titulaire de la famille.
/// </summary>
public static class InternalTransferReconciler
{
    /// <summary>
    /// Écart de date toléré. La banque débite le jour du paiement ou le lendemain ouvré, un week-end
    /// ou un férié pousse à trois jours. Au-delà, deux montants identiques n'ont plus de raison d'être
    /// la même opération.
    /// </summary>
    public const int MaxDayGap = 3;

    /// <summary>
    /// Cherche la jambe bancaire d'un mouvement interne côté courtier.
    /// </summary>
    /// <param name="amount">Montant du mouvement TR, en valeur absolue.</param>
    /// <param name="date">Date du mouvement TR.</param>
    /// <param name="trLineIsIncome">Vrai si l'argent entre chez TR (donc la jambe bancaire est un débit).</param>
    /// <param name="candidates">Transactions bancaires du périmètre, hors lignes courtier.</param>
    /// <param name="ownerNames">Noms des titulaires des comptes suivis.</param>
    /// <param name="alreadyClaimed">Ids déjà rapprochés dans cette passe, pour qu'un débit ne serve pas deux fois.</param>
    /// <returns>La jambe trouvée, ou null si rien ne satisfait les trois conditions.</returns>
    public static TransferLeg? FindMirror(
        decimal amount,
        DateTime date,
        bool trLineIsIncome,
        IEnumerable<TransferLeg> candidates,
        IEnumerable<string> ownerNames,
        ISet<int> alreadyClaimed)
    {
        if (amount <= 0m) return null;

        var owners = ownerNames.ToList();

        return candidates
            .Where(c => !alreadyClaimed.Contains(c.Id))
            // L'argent qui entre chez TR est sorti de la banque, et réciproquement.
            .Where(c => c.IsExpense == trLineIsIncome)
            .Where(c => c.Amount == amount)
            .Where(c => Math.Abs((c.Date.Date - date.Date).TotalDays) <= MaxDayGap)
            .Where(c => PersonNameMatcher.MatchesAny(c.CounterpartyName, owners))
            // Le plus proche dans le temps, et à égalité le plus ancien en base : le résultat ne
            // doit pas dépendre de l'ordre de lecture, sinon deux syncs classent différemment.
            .OrderBy(c => Math.Abs((c.Date.Date - date.Date).TotalDays))
            .ThenBy(c => c.Id)
            .FirstOrDefault();
    }
}
