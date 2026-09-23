namespace FinanceApp.API.Models;

/// <summary>Issue du dernier relevé de la boîte factures. Stockée en texte. Décrit un relevé, jamais un mail.</summary>
public enum MailSyncStatus
{
    /// <summary>Source créée, aucun relevé abouti encore.</summary>
    Pending,
    /// <summary>Boîte ouverte et messages parcourus, même si un message individuel a échoué.</summary>
    Ok,
    /// <summary>Identifiants refusés par le serveur : mot de passe d'application révoqué, à régénérer.</summary>
    AuthError,
    /// <summary>Socket, DNS, délai, TLS : la boîte n'a pas pu être ouverte.</summary>
    ConnectionError,
    /// <summary>Tout le reste.</summary>
    Error
}
