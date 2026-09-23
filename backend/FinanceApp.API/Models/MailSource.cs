namespace FinanceApp.API.Models;

/// <summary>
/// L'état du relevé de la boîte factures d'un dashboard : état seulement, aucun secret. L'hôte, le compte et
/// le mot de passe vivent dans la configuration (section MailIngest), jamais ici. La ligne est créée par le
/// service au premier relevé, clé (DashboardId, Address), et mise à jour ensuite. Pas d'écran de saisie.
/// LastError est borné et ne porte jamais l'objet d'un mail, une adresse hors configuration ni le mot de passe.
/// </summary>
public class MailSource
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    /// <summary>L'adresse relevée (MailIngest:User), pour l'affichage.</summary>
    public string Address { get; set; } = string.Empty;
    /// <summary>Dernière tentative (UTC), aboutie ou non.</summary>
    public DateTime? LastAttemptAt { get; set; }
    /// <summary>Dernier relevé (UTC) qui a ouvert la boîte et parcouru les messages. Ne bouge pas sur un échec de connexion.</summary>
    public DateTime? LastSyncAt { get; set; }
    public MailSyncStatus LastSyncStatus { get; set; } = MailSyncStatus.Pending;
    /// <summary>Raison courte de la dernière anomalie, 200 caractères au plus.</summary>
    public string? LastError { get; set; }
    /// <summary>Dernier dépôt d'un document (UTC), un Created de DocumentDeposit.</summary>
    public DateTime? LastDepositAt { get; set; }
    /// <summary>Cumul des Created depuis la création de la ligne.</summary>
    public int DepositedCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
}
