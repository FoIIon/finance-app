namespace FinanceApp.API.Models;

/// <summary>Par où le document est arrivé. Stocké en texte, Upload pour toute ligne antérieure au lot mail.</summary>
public enum DocumentSource
{
    /// <summary>Déposé par un membre du dashboard, depuis le formulaire.</summary>
    Upload,
    /// <summary>Pièce jointe d'un mail relevé par MailIngestService, sans utilisateur.</summary>
    Mail
}
