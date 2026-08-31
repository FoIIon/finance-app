using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Garde la trace d'une catégorie corrigée à la main.
///
/// Pourquoi (31/08/2026, demande de Sébastien) : chaque correction manuelle est le signe qu'une règle
/// manque ou se trompe. « Wex (sandwichs) » rangé en Restaurants alors que c'est une boulangerie, les
/// « Taxes diverses » en Frais bancaires alors que ce sont des impôts, la facture de crèche en
/// Logement : à chaque fois Sébastien a corrigé et il a fallu le lui demander pour comprendre. En
/// enregistrant la catégorie d'origine et la date, le tri suivant commence par lire ces corrections et
/// se demande pourquoi elles ont eu lieu, au lieu de repartir de zéro.
///
/// La catégorie d'origine conservée est **la première**, celle qu'une règle ou l'import avait posée.
/// Une deuxième correction met la date à jour mais n'écrase pas cette origine : c'est elle qui dit
/// quelle règle s'est trompée.
/// </summary>
public static class ManualCategoryTrace
{
    /// <summary>
    /// Applique une catégorie choisie à la main et note la trace. Ne fait rien si la catégorie ne
    /// change pas : rouvrir un écran et resauver la même ligne n'est pas une correction.
    /// </summary>
    /// <returns>Vrai si la catégorie a changé.</returns>
    public static bool Apply(Transaction transaction, int newCategoryId, DateTime now)
    {
        if (transaction.CategoryId == newCategoryId) return false;

        transaction.CategoryBeforeManualId ??= transaction.CategoryId;
        transaction.CategorySetManuallyAt = now;
        transaction.CategoryId = newCategoryId;
        return true;
    }
}
