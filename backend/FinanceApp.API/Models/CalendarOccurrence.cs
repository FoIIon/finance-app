namespace FinanceApp.API.Models;

/// <summary>
/// Une occurrence d'événement du calendrier du ménage, expansée depuis le flux ICS sur une fenêtre fixe et
/// remplacée en bloc à chaque synchronisation. Les dates locales sont celles du fuseau du ménage
/// (Household:TimeZone), l'instant de début reste en UTC pour l'unicité. DESCRIPTION n'est jamais stockée.
/// </summary>
public class CalendarOccurrence
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Uid { get; set; } = string.Empty;
    /// <summary>Début en UTC. Pour une journée entière : minuit du fuseau du ménage, converti en UTC.</summary>
    public DateTime OccurrenceStart { get; set; }
    public DateOnly LocalDate { get; set; }
    /// <summary>Null pour une journée entière.</summary>
    public TimeOnly? LocalStart { get; set; }
    public TimeOnly? LocalEnd { get; set; }
    /// <summary>Dernier jour couvert, inclus. Un DTEND iCalendar à J+2 (exclusif) donne J+1 ici.</summary>
    public DateOnly LocalEndDate { get; set; }
    public bool IsAllDay { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? Location { get; set; }
    /// <summary>Cadence de la série d'origine, None pour un événement ponctuel.</summary>
    public CalendarRecurrence Recurrence { get; set; }
    /// <summary>L'occurrence porte un RECURRENCE-ID : elle remplace une occurrence de sa série.</summary>
    public bool IsException { get; set; }

    public Dashboard Dashboard { get; set; } = null!;
}
