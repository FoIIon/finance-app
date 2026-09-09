using System.Text.Json.Serialization;
using FinanceApp.API.Models;

namespace FinanceApp.API.Services.Reporting;

/// <summary>Vue demandée à l'agenda : sept jours glissants depuis l'ancre, ou le mois civil qui la contient.</summary>
public enum AgendaView
{
    Week,
    Month
}

/// <summary>Les valeurs de <see cref="AgendaItem.Kind"/>, figées par le contrat JSON.</summary>
public static class AgendaKinds
{
    public const string Event = "event";
    public const string Echeance = "echeance";
    public const string Recurring = "recurring";
    public const string Missing = "missing";
}

/// <summary>Les valeurs de <see cref="AgendaItem.Status"/>, figées par le contrat JSON. Dérivées à la requête, jamais stockées.</summary>
public static class AgendaStatuses
{
    public const string Late = "late";
    public const string Due = "due";
    public const string Paid = "paid";
    public const string Planned = "planned";
}

/// <summary>
/// Une ligne de l'agenda, quelle que soit sa source. Contrat figé avec le frontend : ne rien renommer.
/// Les propriétés marquées JsonIgnore servent aux règles d'AgendaBuilder (routine, occurrence manquante)
/// et ne sortent pas sur le fil.
/// </summary>
public sealed class AgendaItem
{
    /// <summary>event:&lt;uid&gt;:&lt;startUtcIso&gt; | echeance:&lt;id&gt; | recurring:&lt;id&gt;:&lt;yyyy-MM-dd&gt; | missing:&lt;uid&gt;:&lt;yyyy-MM-dd&gt;</summary>
    public string Id { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    /// <summary>HH:mm dans le fuseau du ménage, null sans heure.</summary>
    public string? Start { get; set; }
    public string? End { get; set; }
    public bool IsAllDay { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Location { get; set; }
    public decimal? Amount { get; set; }
    public string? Status { get; set; }
    /// <summary>Posée seulement sur un retard porté dans le jour courant : la date d'origine.</summary>
    public DateOnly? OriginalDate { get; set; }
    public int? EcheanceId { get; set; }
    public int? TransactionId { get; set; }
    public bool IsRoutine { get; set; }

    /// <summary>Uid de la série pour un événement, clé du regroupement des occurrences manquantes.</summary>
    [JsonIgnore] public string? SeriesKey { get; set; }
    [JsonIgnore] public CalendarRecurrence Recurrence { get; set; }
    [JsonIgnore] public bool IsException { get; set; }

    public AgendaItem Clone() => (AgendaItem)MemberwiseClone();
}

public sealed class AgendaDay
{
    public DateOnly Date { get; set; }
    public bool IsToday { get; set; }
    public List<AgendaItem> Items { get; set; } = new();
    public List<AgendaItem> Routine { get; set; } = new();
}

public sealed class AgendaEmptyRange
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
}

public sealed class AgendaUpcoming
{
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public List<AgendaItem> Items { get; set; } = new();
}

/// <summary>État de la source de calendrier tel que le frontend le voit. Jamais l'adresse, même chiffrée.</summary>
public sealed class AgendaCalendarStatus
{
    public bool Connected { get; set; }
    public string? CalendarName { get; set; }
    /// <summary>Dernière synchronisation réussie. Null tant qu'aucune n'a abouti.</summary>
    public DateTime? LastSyncAt { get; set; }
    /// <summary>Dernière tentative, réussie ou non.</summary>
    public DateTime? LastAttemptAt { get; set; }
    public string? LastSyncStatus { get; set; }
    public string? LastError { get; set; }

    public static AgendaCalendarStatus From(CalendarSource? source) => source == null
        ? new AgendaCalendarStatus { Connected = false }
        : new AgendaCalendarStatus
        {
            Connected = true,
            CalendarName = source.CalendarName,
            LastSyncAt = Utc(source.LastSyncAt),
            LastAttemptAt = Utc(source.LastAttemptAt),
            LastSyncStatus = source.LastSyncStatus.ToString(),
            LastError = source.LastError,
        };

    private static DateTime? Utc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;
}

public sealed class AgendaResult
{
    public string View { get; set; } = string.Empty;
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
    public DateOnly Today { get; set; }
    public string TimeZone { get; set; } = string.Empty;
    public AgendaCalendarStatus Calendar { get; set; } = new();
    public List<AgendaDay> Days { get; set; } = new();
    public List<AgendaEmptyRange> EmptyRanges { get; set; } = new();
    public AgendaUpcoming Upcoming { get; set; } = new();
}
