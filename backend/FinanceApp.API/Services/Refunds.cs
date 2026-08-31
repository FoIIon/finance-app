using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Ce qu'est un remboursement pour le bilan, en un seul endroit.
///
/// Pourquoi (31/08/2026) : Sébastien avance 271,50 € de places de foot le 07/08, ses beaux-parents le
/// remboursent le 10/08. Les deux lignes vivent dans Sorties et s'annulent, mais le bilan comptait la
/// dépense au bloc VARIABLE et le remboursement en ENTRÉES, donc les deux blocs gonflaient de 271,50
/// chacun. Le TOTAL restait juste, la lecture non.
///
/// Un remboursement ne compte donc jamais en entrée : il s'impute en négatif sur le bloc de dépense de
/// sa catégorie (FIXE si la ligne est marquée charge fixe, VARIABLE sinon).
///
/// Le drapeau se pose à la main, ligne par ligne, et **seulement sur un revenu**. Aucune règle ne le
/// devine : la contre-épreuve est que les allocations familiales (578 à 691 € par mois, rangées en
/// Enfants) sont un vrai revenu du ménage, pas un remboursement, alors qu'elles ressemblent en base à
/// tous les autres revenus assis dans une catégorie de dépense — 68 lignes pour 16 638 € sur le commun.
/// </summary>
public static class Refunds
{
    /// <summary>
    /// La ligne est-elle un remboursement à imputer sur une catégorie de dépense. Le drapeau posé par
    /// erreur sur une dépense est ignoré : une dépense n'a rien à rembourser.
    /// </summary>
    public static bool Applies(TransactionType type, bool isRefund) =>
        isRefund && type == TransactionType.Income;
}
