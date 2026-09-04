namespace FinanceApp.API.Models;

/// <summary>
/// Statut d'une échéance. Jamais stocké : dérivé de PaidAt, TransactionId et de la date du jour par
/// <see cref="Services.EcheanceStatusRules"/>. Une colonne aurait fini fausse le lendemain de la date
/// d'échéance sans qu'aucun code ne la remette à jour.
/// </summary>
public enum EcheanceStatus
{
    AVenir,
    EnRetard,
    Payee
}
