namespace FinanceApp.API.Models;

public enum BankConnectionStatus
{
    Linked,
    Expired,
    Error,
    PendingTwoFactor,
    /// <summary>
    /// Autorisation entamée mais pas menée à son terme côté banque (statuts GoCardless
    /// CR, GC, UA, GA, SA). Distinct de PendingTwoFactor, réservé à Trade Republic, dont
    /// la purge des connexions périmées ne doit pas ramasser une connexion GoCardless.
    /// </summary>
    PendingAuthorization
}
