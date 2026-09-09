namespace FinanceApp.API.Models;

/// <summary>
/// Issue de la dernière synchronisation d'une source de calendrier. Stockée en texte. C'est le seul
/// statut persisté du lot Agenda, et il décrit une synchro, jamais une échéance ni un événement.
/// </summary>
public enum CalendarSyncStatus
{
    /// <summary>Source enregistrée, aucune synchronisation aboutie encore.</summary>
    Pending,
    Ok,
    /// <summary>Réponse HTTP autre que 200, délai dépassé ou réseau injoignable.</summary>
    HttpError,
    /// <summary>Contenu qui n'est pas un iCalendar lisible, ou trop volumineux.</summary>
    Invalid,
    /// <summary>La clé de chiffrement de l'adresse a changé : l'adresse doit être ressaisie.</summary>
    KeyLost
}
