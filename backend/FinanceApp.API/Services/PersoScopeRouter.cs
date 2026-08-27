using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>Vers quel dashboard une transaction doit compter.</summary>
public enum TransactionScope
{
    /// <summary>Bilan commun du ménage (Sébastien + Audrey).</summary>
    Common,

    /// <summary>Budget personnel de Sébastien, hors du bilan commun.</summary>
    Perso,
}

/// <summary>
/// Décide si une transaction compte au Commun ou au Perso. Décision prise à l'import, comme la
/// catégorisation, jamais rejouée sur l'historique : une transaction déplacée à la main par Sébastien
/// n'est donc pas ramenée de force au Commun à la sync suivante.
///
/// Pourquoi ce routage existe (27/08/2026) : la séparation perso/commun n'existait pas en données,
/// chaque import était écrit sur l'unique compte logique commun, donc les dépenses perso de Sébastien
/// (son compte Argenta perso, ses abos payés avec la carte Trade Republic) remontaient dans le bilan
/// commun d'Audrey.
///
/// Deux règles, et une seule direction par défaut : commun.
///   1. Tout mouvement d'un compte bancaire marqué perso (BankAccount.IsPersonal) est perso.
///   2. Une dépense carte Trade Republic qui matche une règle perso (CategoryRule.RouteToPerso) est perso.
///
/// On ne déduit JAMAIS le perso de l'absence de remboursement. Sébastien rembourse parfois plusieurs
/// achats communs en un seul virement, ce qui casse tout appariement, et surtout une course commune pas
/// encore remboursée ressemblerait à un perso, ce qui la ferait disparaître du bilan commun. Le défaut
/// commun inverse le risque : au pire un perso reste visible au Commun jusqu'à l'ajout d'une règle.
/// Diagnostic complet : projects/app-finance/perso-commun-2026-08-27.md dans le repo Yen.
/// </summary>
public static class PersoScopeRouter
{
    /// <summary>
    /// Décide le périmètre d'une transaction.
    /// </summary>
    /// <param name="bankAccountIsPersonal">Le compte bancaire porteur est-il marqué perso.</param>
    /// <param name="externalId">ExternalId de la transaction. Les lignes Trade Republic portent « tr-… ».</param>
    /// <param name="type">Dépense ou revenu.</param>
    /// <param name="description">Libellé.</param>
    /// <param name="counterpartyName">Contrepartie, si connue.</param>
    /// <param name="persoRuleKeywords">Mots-clés des règles perso (CategoryRule.RouteToPerso).</param>
    public static TransactionScope Decide(
        bool bankAccountIsPersonal,
        string? externalId,
        TransactionType type,
        string description,
        string? counterpartyName,
        IEnumerable<string> persoRuleKeywords)
    {
        // 1. Un compte bancaire perso ne porte que du perso, quel que soit le sens du mouvement.
        //    C'est ce qui envoie la jambe entrante des 830 € (dotation épargne perso) côté Perso, donc
        //    jamais comptée en revenu commun, pendant que la jambe sortante reste une dépense du Commun.
        if (bankAccountIsPersonal) return TransactionScope.Perso;

        // 2. Une dépense carte Trade Republic explicitement désignée perso par une règle. On se limite
        //    aux dépenses (un revenu TR — dividende, intérêts — reste commun) et aux lignes TR, seules
        //    concernées par l'usage mixte de la carte. Un match ailleurs ne doit pas router.
        if (type == TransactionType.Expense && IsTradeRepublicLine(externalId))
        {
            foreach (var keyword in persoRuleKeywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                if (description.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    || (counterpartyName != null && counterpartyName.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                    return TransactionScope.Perso;
            }
        }

        // 3. Tout le reste est commun. Un perso non prévu par une règle reste donc visible au Commun,
        //    jamais escamoté : le sens du risque est volontaire.
        return TransactionScope.Common;
    }

    /// <summary>Vrai si la transaction vient de la timeline Trade Republic (ExternalId « tr-… »).</summary>
    public static bool IsTradeRepublicLine(string? externalId) =>
        externalId != null && externalId.StartsWith("tr-", StringComparison.Ordinal);
}
