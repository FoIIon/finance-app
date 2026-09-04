using FinanceApp.API.Models;

namespace FinanceApp.API.Services;

/// <summary>
/// La règle qui décide du statut d'une échéance. Pure, sans base, testée seule. Aucune colonne en base
/// ne porte ce statut : il se recalcule à chaque lecture avec la date du jour.
/// </summary>
public static class EcheanceStatusRules
{
    /// <summary>
    /// Payée dès qu'un paiement est prouvé (PaidAt posé à la main ou transaction liée). Sinon en retard
    /// une fois la date d'échéance passée : le jour même, elle est encore à venir.
    /// </summary>
    public static EcheanceStatus Of(Echeance e, DateOnly today)
    {
        if (e.PaidAt.HasValue || e.TransactionId.HasValue) return EcheanceStatus.Payee;
        return e.DueDate < today ? EcheanceStatus.EnRetard : EcheanceStatus.AVenir;
    }

    public static bool IsPaid(Echeance e) => e.PaidAt.HasValue || e.TransactionId.HasValue;
}
