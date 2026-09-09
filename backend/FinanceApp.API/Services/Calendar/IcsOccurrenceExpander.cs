using FinanceApp.API.Models;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Evaluation;
using IcsCalendar = Ical.Net.Calendar;

namespace FinanceApp.API.Services.Calendar;

/// <summary>Résultat d'une expansion : nom du calendrier, occurrences, et un avertissement court si on a tronqué.</summary>
public sealed record IcsExpansion(string? CalendarName, IReadOnlyList<CalendarOccurrence> Occurrences, string? Warning);

/// <summary>
/// La seule classe du projet qui référence Ical.Net (v5). Expanse un flux ICS en occurrences sur une
/// fenêtre UTC, converties dans le fuseau du ménage. Pure : ni base, ni réseau, ni journal. DESCRIPTION
/// n'est jamais lue.
///
/// Constaté sur Ical.Net 5.2.3 : la série maître rend encore l'occurrence qu'une exception
/// (RECURRENCE-ID) remplace, on la retire ici. Une valeur flottante (sans TZID ni Z) est lue comme heure
/// du ménage, alors que la bibliothèque la traiterait en UTC. Une journée entière n'a pas d'heure et son
/// DTEND est exclusif : LocalEndDate est le dernier jour couvert, inclus.
/// </summary>
public static class IcsOccurrenceExpander
{
    public const int UidMaxLength = 300;
    public const int SummaryMaxLength = 300;
    public const int LocationMaxLength = 300;
    public const int CalendarNameMaxLength = 200;
    public const int WarningMaxLength = 200;
    /// <summary>Garde-fou d'Ical.Net contre une règle qui ne produit jamais rien (BYDAY impossible, etc.).</summary>
    private const int MaxUnmatchedIncrements = 1000;

    public static IcsExpansion Expand(string ics, DateTime fromUtc, DateTime toUtc, TimeZoneInfo household, CalendarOptions limits)
    {
        if (toUtc <= fromUtc) throw new ArgumentException("La fenêtre d'expansion est vide.", nameof(toUtc));
        var calendar = IcsCalendar.Load(ics) ?? throw new FormatException("Flux iCalendar vide.");
        var name = Truncate(calendar.Properties.Get<string>("X-WR-CALNAME"), CalendarNameMaxLength);

        var from = new CalDateTime(AsUtcKind(fromUtc), "UTC", true);
        var to = new CalDateTime(AsUtcKind(toUtc), "UTC", true);
        var options = new EvaluationOptions { MaxUnmatchedIncrementsLimit = MaxUnmatchedIncrements };

        var result = new List<CalendarOccurrence>();
        var warnings = new List<string>();
        var totalTruncated = false;

        foreach (var group in calendar.Events.Where(e => !string.IsNullOrWhiteSpace(e.Uid)).GroupBy(e => e.Uid))
        {
            if (totalTruncated) break;
            var master = group.FirstOrDefault(e => e.RecurrenceIdentifier == null);
            var exceptions = group.Where(e => e.RecurrenceIdentifier != null).ToList();
            var recurrence = MapRecurrence(master?.RecurrenceRule?.Frequency);

            var uidOccurrences = new List<CalendarOccurrence>();
            if (master != null)
            {
                // Instants remplacés par une exception : la série maître les rend encore, on les écarte.
                var replaced = new HashSet<DateTime>();
                foreach (var ex in exceptions)
                {
                    try { replaced.Add(ToUtc(ex.RecurrenceIdentifier!.StartTime, household)); }
                    catch (Exception e) when (e is not OutOfMemoryException) { warnings.Add(Warn(ex, "RECURRENCE-ID illisible")); }
                }
                uidOccurrences.AddRange(ExpandEvent(master, from, to, options, limits, household, recurrence, isException: false, warnings)
                    .Where(o => !replaced.Contains(o.OccurrenceStart)));
            }
            foreach (var ex in exceptions)
                uidOccurrences.AddRange(ExpandEvent(ex, from, to, options, limits, household, recurrence, isException: true, warnings));

            // L'index unique (DashboardId, Uid, OccurrenceStart) ne tolère pas deux occurrences au même
            // instant : un RDATE qui double une occurrence de la règle, par exemple. La première gagne.
            foreach (var o in uidOccurrences.OrderBy(o => o.OccurrenceStart).DistinctBy(o => o.OccurrenceStart))
            {
                if (result.Count >= limits.MaxOccurrences)
                {
                    totalTruncated = true;
                    warnings.Insert(0, $"Calendrier tronqué à {limits.MaxOccurrences} occurrences.");
                    break;
                }
                result.Add(o);
            }
        }

        var warning = warnings.Count == 0 ? null : Truncate(string.Join(" ", warnings.Distinct()), WarningMaxLength);
        return new IcsExpansion(name, result, warning);
    }

    /// <summary>Les occurrences d'un événement sur la fenêtre. Un événement malade est tronqué ou ignoré avec un avertissement, jamais fatal.</summary>
    private static List<CalendarOccurrence> ExpandEvent(
        CalendarEvent ev, CalDateTime from, CalDateTime to, EvaluationOptions options, CalendarOptions limits,
        TimeZoneInfo household, CalendarRecurrence recurrence, bool isException, List<string> warnings)
    {
        var list = new List<CalendarOccurrence>();
        try
        {
            foreach (var occurrence in ev.GetOccurrences(from, options).TakeWhileBefore(to))
            {
                if (list.Count >= limits.MaxOccurrencesPerEvent)
                {
                    warnings.Add(Warn(ev, $"tronquée à {limits.MaxOccurrencesPerEvent} occurrences"));
                    break;
                }
                list.Add(Build(occurrence.Period, ev, recurrence, isException, household));
            }
        }
        catch (EvaluationException)
        {
            warnings.Add(Warn(ev, "règle de récurrence non évaluable"));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            warnings.Add(Warn(ev, $"ignorée ({e.GetType().Name})"));
        }
        return list;
    }

    private static CalendarOccurrence Build(Period period, CalendarEvent source, CalendarRecurrence recurrence, bool isException, TimeZoneInfo tz)
    {
        var start = period.StartTime;
        var end = period.EffectiveEndTime ?? start;
        var occurrence = new CalendarOccurrence
        {
            Uid = Truncate(source.Uid, UidMaxLength)!,
            Summary = Truncate(source.Summary, SummaryMaxLength) ?? string.Empty,
            Location = Truncate(string.IsNullOrWhiteSpace(source.Location) ? null : source.Location, LocationMaxLength),
            Recurrence = recurrence,
            IsException = isException,
        };

        if (!start.HasTime)
        {
            var startDate = start.Date;
            var endExclusive = end.Date;
            occurrence.IsAllDay = true;
            occurrence.OccurrenceStart = MidnightUtc(startDate, tz);
            occurrence.LocalDate = startDate;
            occurrence.LocalEndDate = endExclusive > startDate ? endExclusive.AddDays(-1) : startDate;
            return occurrence;
        }

        var startUtc = ToUtc(start, tz);
        var endUtc = ToUtc(end, tz);
        if (endUtc < startUtc) endUtc = startUtc;
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, tz);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(endUtc, tz);
        occurrence.IsAllDay = false;
        occurrence.OccurrenceStart = startUtc;
        occurrence.LocalDate = DateOnly.FromDateTime(localStart);
        occurrence.LocalStart = TimeOnly.FromDateTime(localStart);
        occurrence.LocalEnd = TimeOnly.FromDateTime(localEnd);
        occurrence.LocalEndDate = DateOnly.FromDateTime(localEnd);
        return occurrence;
    }

    /// <summary>L'instant UTC d'une valeur iCalendar. Date seule ou flottante : heure du ménage.</summary>
    private static DateTime ToUtc(CalDateTime dt, TimeZoneInfo tz)
    {
        if (!dt.HasTime) return MidnightUtc(dt.Date, tz);
        if (dt.IsFloating)
            return TimeZoneInfo.ConvertTimeToUtc(new DateTime(dt.Date, dt.Time!.Value, DateTimeKind.Unspecified), tz);
        return AsUtcKind(dt.AsUtc);
    }

    private static DateTime MidnightUtc(DateOnly date, TimeZoneInfo tz) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(date, TimeOnly.MinValue, DateTimeKind.Unspecified), tz);

    private static DateTime AsUtcKind(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static CalendarRecurrence MapRecurrence(FrequencyType? frequency) => frequency switch
    {
        FrequencyType.Daily => CalendarRecurrence.Daily,
        FrequencyType.Weekly => CalendarRecurrence.Weekly,
        FrequencyType.Monthly => CalendarRecurrence.Monthly,
        FrequencyType.Yearly => CalendarRecurrence.Yearly,
        _ => CalendarRecurrence.None,
    };

    private static string Warn(CalendarEvent ev, string what) =>
        $"Série « {Truncate(ev.Summary, 40) ?? "sans titre"} » {what}.";

    private static string? Truncate(string? value, int max)
    {
        if (value == null) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
