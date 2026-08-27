using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>Ce qu'une ligne de timeline Trade Republic est réellement, du point de vue du budget familial.</summary>
public enum TrLineKind
{
    /// <summary>Dépense ou recette réelle du ménage : paiement carte chez un commerçant, remboursement, intérêts.</summary>
    Flow,

    /// <summary>
    /// Mouvement entre deux comptes de la famille : alimentation de la carte depuis le compte joint,
    /// dépôt d'espèces sur le courtier. Ni dépense ni revenu, la contrepartie est déjà dans le flux bancaire.
    /// </summary>
    InternalTransfer,

    /// <summary>Achat ou vente de titres : l'argent change de forme, il ne quitte pas le patrimoine.</summary>
    Investment,

    /// <summary>Ligne à ne pas importer du tout (paiement carte refusé).</summary>
    Ignore,
}

/// <summary>
/// Range une ligne de timeline Trade Republic dans l'une des quatre familles ci-dessus.
///
/// Pourquoi ce classement existe (audit du 27/08/2026) : la carte Trade Republic tire sur le compte joint
/// à chaque paiement. La timeline TR contient donc, pour un même euro dépensé, le crédit d'alimentation
/// ET le paiement carte, alors que le flux GoCardless contient déjà le débit bancaire. Importer les trois
/// lignes en dépense/revenu comptait le même euro deux à trois fois. Sur août 2026 : 545,72 € de dépenses
/// en double, 606,26 € de faux retraits d'épargne et 720 € de faux revenu.
///
/// Le modèle retenu : le paiement carte TR est la dépense (c'est lui qui porte le nom du commerçant),
/// les deux jambes du virement sont des transferts internes, les achats de titres sont des mises de côté.
/// </summary>
public static class TradeRepublicTimelineClassifier
{
    /// <summary>
    /// Types d'évènement Trade Republic reconnus. La timeline en expose un par ligne (`eventType`).
    /// Quand il est absent ou inconnu, on retombe sur les règles de nom ci-dessous plutôt que de deviner :
    /// une ligne mal classée en dépense reste préférable à une dépense réelle escamotée en transfert.
    /// Les inconnus sont journalisés par l'appelant, pour resserrer cette table sur des données réelles.
    /// </summary>
    private static readonly (string Prefix, TrLineKind Kind)[] EventTypeMap =
    {
        // Paiement carte refusé : aucun euro n'a bougé.
        ("card_failed_transaction", TrLineKind.Ignore),
        ("card_", TrLineKind.Flow),

        // Alimentation du compte courtier, dans les deux sens.
        ("payment_inbound", TrLineKind.InternalTransfer),
        ("payment_outbound", TrLineKind.InternalTransfer),
        ("incoming_transfer", TrLineKind.InternalTransfer),
        ("outgoing_transfer", TrLineKind.InternalTransfer),

        // Exécutions d'ordre, plans d'investissement, arrondis et saveback.
        ("trade_invoice", TrLineKind.Investment),
        ("order_executed", TrLineKind.Investment),
        ("savings_plan", TrLineKind.Investment),
        ("trading_", TrLineKind.Investment),
        ("benefits_saveback", TrLineKind.Investment),
        ("benefits_spare_change", TrLineKind.Investment),

        // Intérêts et dividendes : de l'argent qui entre pour de bon.
        ("interest_payout", TrLineKind.Flow),
        ("ssp_corporate_action", TrLineKind.Flow),
    };

    /// <summary>
    /// Vrai si cet `eventType` figure dans la table ci-dessus. Sert à ne journaliser comme inconnus
    /// que les types qui le sont vraiment : sans cette distinction, tout paiement carte correctement
    /// reconnu apparaissait dans la liste des types à cartographier, et la liste ne signalait plus rien.
    ///
    /// Types observés en prod le 27/08/2026, tous deux reconnus : CARD_TRANSACTION et
    /// SSP_CORPORATE_ACTION_CASH (dividende versé en espèces).
    /// </summary>
    public static bool IsKnownEventType(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType)) return false;
        var normalized = eventType.Trim().ToLowerInvariant();
        return EventTypeMap.Any(e => normalized.StartsWith(e.Prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Classe une ligne.
    /// </summary>
    /// <param name="title">Libellé TR de la ligne.</param>
    /// <param name="eventType">`eventType` de la timeline, null si TR ne l'a pas fourni.</param>
    /// <param name="ownerNames">Noms des titulaires des comptes suivis (OwnerName des BankAccounts, nom du dashboard…).</param>
    /// <param name="instrumentNames">Noms d'instruments du portefeuille jugés non ambigus (voir <see cref="UnambiguousInstrumentNames"/>).</param>
    public static TrLineKind Classify(
        string title,
        string? eventType,
        IEnumerable<string> ownerNames,
        IEnumerable<string> instrumentNames)
    {
        if (!string.IsNullOrWhiteSpace(eventType))
        {
            var normalized = eventType.Trim().ToLowerInvariant();
            foreach (var (prefix, kind) in EventTypeMap)
            {
                if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                    return kind;
            }
        }

        // Un libellé qui nomme un titulaire d'un compte suivi désigne un mouvement interne,
        // quel qu'en soit le sens : « LIBERT - LAMBRECHT », « SEBASTIEN LIBERT ».
        if (PersonNameMatcher.MatchesAny(title, ownerNames))
            return TrLineKind.InternalTransfer;

        // Un libellé qui est exactement un instrument du portefeuille désigne un mouvement de titres.
        foreach (var instrument in instrumentNames)
        {
            if (!string.IsNullOrWhiteSpace(instrument)
                && string.Equals(title.Trim(), instrument.Trim(), StringComparison.OrdinalIgnoreCase))
                return TrLineKind.Investment;
        }

        return TrLineKind.Flow;
    }

    /// <summary>
    /// Les noms d'instruments qu'on accepte de reconnaître au libellé seul, quand TR ne donne pas d'`eventType`.
    ///
    /// Un nom d'instrument peut être aussi un nom de commerçant : « Apple » est à la fois une action du
    /// portefeuille et l'App Store, et le remboursement Apple de 1,20 € du 13/08/2026 n'est pas une vente
    /// d'action. On ne garde donc que les noms qu'aucun commerçant ne porte : les parts capitalisantes ou
    /// distribuantes, qui se signalent par leur suffixe, et les cryptos, que leur type identifie en base.
    /// Une action nommée sobrement reste ambiguë et repart dans le flux, où les règles de catégorisation
    /// la traiteront comme n'importe quelle ligne inconnue.
    /// </summary>
    public static List<string> UnambiguousInstrumentNames(IEnumerable<Investment> investments) =>
        investments
            .Where(i => i.Kind == InvestmentKind.Crypto
                     || i.Name.Contains("(Acc)", StringComparison.OrdinalIgnoreCase)
                     || i.Name.Contains("(Dist)", StringComparison.OrdinalIgnoreCase))
            .Select(i => i.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
