namespace FinanceApp.API.Services;

/// <summary>Une dépense déjà en base, réduite à ce qu'il faut pour prouver une échéance.</summary>
public sealed record PaymentCandidate(int Id, decimal Amount, DateTime Date, string? CounterpartyIban, string? StructuredCommunication);

/// <summary>
/// Retrouve, pour une échéance, le virement qui la règle. Pur, statique, sur le modèle de
/// <see cref="InternalTransferReconciler.FindMirror"/> : les candidats sont préparés par l'exécuteur,
/// seules les règles métier vivent ici.
///
/// Deux clés, jamais le libellé ni le montant seul. La communication structurée est la clé forte :
/// c'est l'émetteur de la facture qui l'a choisie, elle suffit même quand le montant n'est pas encore
/// connu. L'IBAN du bénéficiaire avec le montant au centime est la clé ordinaire, plus étroite dans le
/// temps parce que deux factures du même fournisseur au même montant existent (deux enfants à la même
/// école). Un faux positif marque payée une facture qui ne l'est pas, ce qui coûte un rappel de
/// paiement, d'où des règles étroites et un tri qui ne dépend pas de l'ordre de lecture.
/// </summary>
public static class EcheanceMatcher
{
    /// <summary>Fenêtre de la clé forte : une facture payée très en avance ou très en retard reste la même facture.</summary>
    public const int StrongDaysBefore = 90;
    public const int StrongDaysAfter = 180;

    /// <summary>Fenêtre de la clé ordinaire, plus courte : sans communication, le montant ne distingue pas deux factures successives.</summary>
    public const int OrdinaryDaysBefore = 45;
    public const int OrdinaryDaysAfter = 60;

    /// <summary>
    /// Le candidat qui règle l'échéance, ou null.
    /// </summary>
    /// <param name="dueDate">Date limite de l'échéance.</param>
    /// <param name="amount">Montant attendu, null quand la facture n'est pas encore arrivée.</param>
    /// <param name="counterpartyIban">IBAN du bénéficiaire, normalisé, ou null.</param>
    /// <param name="structuredCommunication">Douze chiffres, ou null.</param>
    /// <param name="candidates">Dépenses réelles du dashboard, non liées à une autre échéance.</param>
    /// <param name="alreadyClaimed">Ids déjà rapprochés dans cette passe : une transaction ne prouve qu'une échéance.</param>
    /// <remarks>Une échéance dont l'utilisateur a refusé le rapprochement (AutoMatchRefusedAt) n'arrive
    /// pas ici : l'exécuteur l'écarte avant de charger un candidat.</remarks>
    public static PaymentCandidate? FindPayment(
        DateOnly dueDate,
        decimal? amount,
        string? counterpartyIban,
        string? structuredCommunication,
        IEnumerable<PaymentCandidate> candidates,
        ISet<int> alreadyClaimed)
    {
        // Règle 1 : sans IBAN ni communication, rien ne prouve un paiement. Le montant seul rapprocherait
        // n'importe quelle dépense du même prix, le libellé n'importe quel virement du même nom.
        var hasIban = !string.IsNullOrEmpty(counterpartyIban);
        var hasCommunication = !string.IsNullOrEmpty(structuredCommunication);
        if (!hasIban && !hasCommunication) return null;

        return candidates
            // Règle 2 : déjà pris dans la passe, une transaction ne prouve qu'une échéance.
            .Where(c => !alreadyClaimed.Contains(c.Id))
            .Select(c => (Candidate: c, Strong: MatchesStrongKey(c, amount, structuredCommunication, dueDate)))
            .Where(x => x.Strong || MatchesOrdinaryKey(x.Candidate, amount, counterpartyIban, dueDate))
            // Règle 5 : la clé forte avant l'ordinaire, puis le plus proche de la date limite, puis le
            // plus petit Id. Deux passes sur les mêmes données rendent le même résultat.
            .OrderByDescending(x => x.Strong)
            .ThenBy(x => DaysFromDue(x.Candidate, dueDate))
            .ThenBy(x => x.Candidate.Id)
            .Select(x => x.Candidate)
            .FirstOrDefault();
    }

    /// <summary>Règle 3 : même communication structurée, même montant si l'échéance en a un, dans la fenêtre large.</summary>
    private static bool MatchesStrongKey(PaymentCandidate c, decimal? amount, string? structuredCommunication, DateOnly dueDate)
    {
        // Vide vaut null des deux côtés : la sentinelle du rattrapage n'est pas une clé.
        if (string.IsNullOrEmpty(structuredCommunication) || string.IsNullOrEmpty(c.StructuredCommunication)) return false;
        if (c.StructuredCommunication != structuredCommunication) return false;
        if (amount.HasValue && c.Amount != amount.Value) return false;
        return InWindow(c, dueDate, StrongDaysBefore, StrongDaysAfter);
    }

    /// <summary>Règle 4 : même IBAN et même montant au centime, montant connu obligatoire, dans la fenêtre courte.</summary>
    private static bool MatchesOrdinaryKey(PaymentCandidate c, decimal? amount, string? counterpartyIban, DateOnly dueDate)
    {
        if (string.IsNullOrEmpty(counterpartyIban) || !amount.HasValue) return false;
        if (c.CounterpartyIban != counterpartyIban || c.Amount != amount.Value) return false;
        return InWindow(c, dueDate, OrdinaryDaysBefore, OrdinaryDaysAfter);
    }

    private static bool InWindow(PaymentCandidate c, DateOnly dueDate, int daysBefore, int daysAfter)
    {
        var date = DateOnly.FromDateTime(c.Date);
        return date >= dueDate.AddDays(-daysBefore) && date <= dueDate.AddDays(daysAfter);
    }

    private static int DaysFromDue(PaymentCandidate c, DateOnly dueDate) =>
        Math.Abs(DateOnly.FromDateTime(c.Date).DayNumber - dueDate.DayNumber);
}
