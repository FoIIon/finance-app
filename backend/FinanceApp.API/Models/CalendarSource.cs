namespace FinanceApp.API.Models;

/// <summary>
/// L'adresse ICS privée d'un calendrier Google partagé, une par dashboard, dans sa propre table (jamais
/// une colonne sur Dashboard). L'adresse est un secret : chiffrée par IDataProtectionProvider (purpose
/// « Calendar.IcsUrl »), elle ne sort jamais de cette ligne, ni vers un DTO, ni vers un journal, ni vers
/// LastError. Le seul repère renvoyé au frontend est CalendarName.
/// </summary>
public class CalendarSource
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string EncryptedUrl { get; set; } = string.Empty;
    /// <summary>X-WR-CALNAME du flux, relu à chaque synchronisation.</summary>
    public string? CalendarName { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public CalendarSyncStatus LastSyncStatus { get; set; } = CalendarSyncStatus.Pending;
    /// <summary>Raison courte de la dernière anomalie. Jamais une URL, jamais un fragment de l'URL.</summary>
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
}
