namespace FinanceApp.API.Services;

/// <summary>Un mouvement interne côté courtier, déjà classé « Virement interne ».</summary>
public record BrokerTransferLine(int Id, DateTime Date, decimal Amount, bool IsIncome, DateTime CreatedAt);

/// <summary>Un paiement carte Trade Republic, la dépense qu'un virement interne vient financer.</summary>
public record BrokerCardPayment(DateTime Date, decimal Amount);

/// <summary>Une transaction bancaire candidate au rôle d'autre jambe d'un mouvement courtier.</summary>
/// <param name="IsNeutralized">Déjà en « Virement interne ».</param>
/// <param name="IsManuallyCategorized">Catégorie posée à la main, que le code ne touche jamais.</param>
public record LateBankLeg(
    int Id,
    DateTime Date,
    decimal Amount,
    bool IsExpense,
    string? CounterpartyName,
    DateTime CreatedAt,
    bool IsNeutralized,
    bool IsManuallyCategorized);

/// <summary>
/// Les virements entre comptes de la famille que le rapprochement de l'import Trade Republic ne voit pas.
///
/// Deux trous, tous deux vécus le 24/09/2026 avec 211,53 € remboursés à la carte TR.
///
/// 1. <b>La course entre les deux syncs.</b> <see cref="InternalTransferReconciler"/> ne tourne qu'au premier
///    import d'une ligne TR. Les sept arrivées sur TR ont été importées à 13h06, les sept débits CBC qui les
///    financent à 14h44 : au moment de chercher, il n'y avait rien à trouver, et rien ne repasse ensuite.
///    <see cref="FindLateLegs"/> refait la recherche depuis le côté bancaire, mais seulement pour une jambe
///    importée APRÈS sa ligne TR. C'est ce verrou qui protège le cas du 12/08 (60,62 € au Leclerc drive,
///    débit bancaire gardé en dépense faute de paiement carte TR) : là, la jambe était en base avant.
///    Deuxième verrou : il faut qu'un paiement carte TR du même montant existe, c'est lui qui fait le
///    doublon. Sans lui, le débit bancaire est la seule trace de la dépense et doit rester.
///
/// 2. <b>Le virement entre deux comptes bancaires suivis.</b> Le compte joint Argenta a remboursé la CBC :
///    un débit d'un côté, un crédit de l'autre, aucun courtier dans l'affaire. <see cref="IsSameScopeTransfer"/>
///    le reconnaît à l'IBAN de la contrepartie.
/// </summary>
public static class LateTransferReconciler
{
    /// <summary>
    /// Écart toléré entre le paiement carte et son remboursement. Le 24/09/2026, Sébastien a remboursé
    /// des paiements des 18 et 19/09 : six jours. Deux semaines laissent la marge d'un remboursement groupé.
    /// </summary>
    public const int CardPaymentLookbackDays = 14;

    /// <summary>
    /// Les jambes bancaires à passer en « Virement interne » parce qu'elles sont arrivées après la ligne
    /// TR qu'elles financent, chacune avec l'Id de cette ligne TR.
    /// </summary>
    /// <remarks>
    /// Seules les lignes où l'argent ENTRE chez TR sont traitées, donc seuls des débits bancaires peuvent être
    /// neutralisés. Une sortie de TR suivie d'un crédit bancaire du même montant au nom d'un titulaire
    /// (l'apport d'Audrey, par exemple) escamoterait un vrai revenu.
    /// Chaque ligne TR réclame d'abord une jambe déjà neutralisée : si elle a été rapprochée à son import,
    /// elle ne doit pas en neutraliser une seconde. Seules les lignes restées orphelines vont chercher
    /// parmi les dépenses, avec les trois verrous de <see cref="InternalTransferReconciler.FindMirror"/>.
    /// </remarks>
    public static IReadOnlyList<(int LegId, int BrokerLineId)> FindLateLegs(
        IEnumerable<BrokerTransferLine> brokerLines,
        IReadOnlyCollection<LateBankLeg> bankLegs,
        IEnumerable<BrokerCardPayment> cardPayments,
        IEnumerable<string> ownerNames)
    {
        var owners = ownerNames.ToList();
        var payments = cardPayments.ToList();
        var lines = brokerLines.Where(l => l.IsIncome).OrderBy(l => l.Date).ThenBy(l => l.Id).ToList();
        var claimed = new HashSet<int>();

        static TransferLeg AsLeg(LateBankLeg l) => new(l.Id, l.Date, l.Amount, l.IsExpense, l.CounterpartyName);

        var neutralized = bankLegs.Where(l => l.IsNeutralized).Select(AsLeg).ToList();
        var orphans = new List<BrokerTransferLine>();
        foreach (var line in lines)
        {
            var mirror = InternalTransferReconciler.FindMirror(line.Amount, line.Date, line.IsIncome, neutralized, owners, claimed);
            if (mirror != null) claimed.Add(mirror.Id);
            else orphans.Add(line);
        }

        var result = new List<(int, int)>();
        foreach (var line in orphans)
        {
            var financesACardPayment = payments.Any(p =>
                p.Amount == line.Amount
                && p.Date.Date <= line.Date.Date.AddDays(InternalTransferReconciler.MaxDayGap)
                && p.Date.Date >= line.Date.Date.AddDays(-CardPaymentLookbackDays));
            if (!financesACardPayment) continue;

            var candidates = bankLegs
                .Where(l => !l.IsNeutralized && !l.IsManuallyCategorized && l.CreatedAt > line.CreatedAt)
                .Select(AsLeg);
            var mirror = InternalTransferReconciler.FindMirror(line.Amount, line.Date, line.IsIncome, candidates, owners, claimed);
            if (mirror == null) continue;
            claimed.Add(mirror.Id);
            result.Add((mirror.Id, line.Id));
        }
        return result;
    }

    /// <summary>
    /// Vrai si la contrepartie est un autre compte bancaire suivi du même périmètre (commun vers commun,
    /// perso vers perso).
    /// </summary>
    /// <param name="ownIban">IBAN du compte qui porte la transaction.</param>
    /// <param name="ownIsPersonal">Périmètre de ce compte.</param>
    /// <param name="counterpartyIban">IBAN de la contrepartie servi par la banque.</param>
    /// <param name="trackedAccounts">IBAN et périmètre des comptes bancaires synchronisés de l'utilisateur,
    /// hors courtier : un virement vers TR reste l'affaire de <see cref="FindLateLegs"/> et de ses verrous.</param>
    /// <param name="matchedRuleKeyword">Mot-clé de la règle qui a matché. Une règle posée sur cet IBAN précis
    /// est un choix explicite de l'utilisateur et l'emporte.</param>
    /// <remarks>
    /// Trois exclusions, chacune voulue.
    /// <list type="bullet">
    /// <item>Le compte lui-même : la CBC sert l'IBAN du compte en contrepartie de ses paiements Bancontact
    /// (Colruyt, Wex), ce ne sont pas des virements.</item>
    /// <item>Les comptes manuels, absents de <paramref name="trackedAccounts"/> : le balayage vers le livret
    /// Argenta doit rester en « Épargne », c'est lui qui fait le solde du livret.</item>
    /// <item>Le changement de périmètre : l'ordre permanent vers le compte perso et les remboursements du
    /// perso vers le joint sont des apports, avec leurs propres catégories.</item>
    /// </list>
    /// </remarks>
    public static bool IsSameScopeTransfer(
        string? ownIban,
        bool ownIsPersonal,
        string? counterpartyIban,
        IEnumerable<(string Iban, bool IsPersonal)> trackedAccounts,
        string? matchedRuleKeyword = null)
    {
        if (string.IsNullOrWhiteSpace(counterpartyIban)) return false;
        if (BankAccountReconciler.SameIban(ownIban, counterpartyIban)) return false;
        if (BankAccountReconciler.SameIban(matchedRuleKeyword, counterpartyIban)) return false;

        return trackedAccounts.Any(a =>
            a.IsPersonal == ownIsPersonal && BankAccountReconciler.SameIban(a.Iban, counterpartyIban));
    }
}
