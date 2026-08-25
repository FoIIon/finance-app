using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// Traduit un statut de réquisition GoCardless : CR (créée), GC (consentement donné),
/// UA (authentification en cours), GA (sélection des comptes), SA (comptes sélectionnés),
/// LN (liée), RJ (rejetée), EX (expirée), SU (suspendue).
///
/// Les états transitoires prennent PendingAuthorization, rendu en jaune côté interface avec
/// un bouton de reprise : l'autorisation n'est pas finie, ce n'est pas une erreur, et il faut
/// pouvoir la reprendre sans supprimer la connexion, une suppression détachant les
/// transactions de leur compte.
/// </summary>
public static class GoCardlessRequisitionStatus
{
    public static BankConnectionStatus Map(string status) => status switch
    {
        "LN" => BankConnectionStatus.Linked,
        "EX" or "SU" => BankConnectionStatus.Expired,
        "CR" or "GC" or "UA" or "GA" or "SA" => BankConnectionStatus.PendingAuthorization,
        _ => BankConnectionStatus.Error
    };

    public static string Describe(string status) => status switch
    {
        "EX" => "L'accès à la banque a expiré (statut EX). Relancez la connexion pour en obtenir un nouveau.",
        "SU" => "L'accès à la banque est suspendu (statut SU). Relancez la connexion.",
        "RJ" => "La banque a rejeté la demande d'accès (statut RJ). Si le refus se répète sans jamais afficher l'écran de login, l'intégration de cette banque est en cause : essayez celle du groupe bancaire.",
        "CR" or "GC" or "UA" or "GA" or "SA" => $"L'autorisation n'est pas terminée côté banque (statut {status}). Reprenez le parcours de connexion jusqu'au bout.",
        _ => $"La liaison bancaire a échoué (statut {status})."
    };
}
