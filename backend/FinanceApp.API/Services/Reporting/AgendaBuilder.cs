using FinanceApp.API.Models;

namespace FinanceApp.API.Services.Reporting;

/// <summary>
/// La projection par jour de l'agenda. Pure, statique, sans EF, sur le modèle de BurndownBuilder : le
/// contrôleur charge et projette, ce builder applique les règles. Toutes vivent ici et sont testées,
/// aucune n'est en React.
///
/// Règles : la routine est un événement d'une série hebdomadaire hors exception. Tout impayé, dans la
/// fenêtre ou non, est porté dans le jour courant avec sa date d'origine et ne figure plus sur son jour
/// d'origine, et le jour courant est ajouté en tête s'il est hors fenêtre. Une série hebdomadaire d'au
/// moins trois occurrences passées qui manque un jour de sa semaine produit « Pas de … » (entre sa
/// première et sa dernière occurrence connues).
/// En vue mois, les jours consécutifs sans rien, passés ou futurs, sont repliés en plages vides, jamais
/// le jour courant. L'à venir couvre trente jours glissants depuis aujourd'hui, sans routine ni manquant,
/// sans retard (ils sont dans Aujourd'hui) et sans rien de ce que la fenêtre affichée montre déjà.
/// </summary>
public static class AgendaBuilder
{
    public const int UpcomingDays = 30;
    public const int MissingMinPastOccurrences = 3;

    public static AgendaResult Build(DateOnly from, DateOnly to, DateOnly today, AgendaView view, IReadOnlyList<AgendaItem> items)
    {
        if (to < from) throw new ArgumentException("La fin de la fenêtre précède son début.", nameof(to));

        var all = items.Select(i => i.Clone()).ToList();
        foreach (var i in all)
            i.IsRoutine = i.Kind == AgendaKinds.Event && i.Recurrence == CalendarRecurrence.Weekly && !i.IsException;

        var missing = MissingOccurrences(all, from, to, today);

        // Retards portés : tout impayé est porté dans le jour courant, que sa date soit dans la fenêtre ou
        // non. Un parent qui ouvre le mois voit dans Aujourd'hui ce qu'il doit payer, comme en semaine, et
        // une échéance n'apparaît qu'une fois dans les jours : son jour d'origine ne la garde pas.
        var carried = CarryLate(all, _ => true, today);
        var placed = all.Where(i => i.Status != AgendaStatuses.Late).Concat(missing).ToLookup(i => i.Date);

        var days = new List<AgendaDay>();
        var emptyRanges = new List<AgendaEmptyRange>();
        DateOnly? emptyStart = null;
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var isToday = day == today;
            var dayItems = placed[day].Where(i => !i.IsRoutine).ToList();
            var routine = placed[day].Where(i => i.IsRoutine).ToList();
            if (isToday) dayItems.AddRange(carried);

            var isEmpty = dayItems.Count == 0 && routine.Count == 0 && !isToday;
            if (view == AgendaView.Month && isEmpty)
            {
                emptyStart ??= day;
                continue;
            }
            if (emptyStart.HasValue)
            {
                emptyRanges.Add(new AgendaEmptyRange { From = emptyStart.Value, To = day.AddDays(-1) });
                emptyStart = null;
            }
            days.Add(new AgendaDay { Date = day, IsToday = isToday, Items = SortDay(dayItems), Routine = SortRoutine(routine) });
        }
        if (emptyStart.HasValue)
            emptyRanges.Add(new AgendaEmptyRange { From = emptyStart.Value, To = to });

        if ((today < from || today > to) && carried.Count > 0)
            days.Insert(0, new AgendaDay { Date = today, IsToday = true, Items = SortDay(carried), Routine = new List<AgendaItem>() });

        var upcoming = BuildUpcoming(all, today, from, to);

        return new AgendaResult
        {
            View = view.ToString().ToLowerInvariant(),
            From = from,
            To = to,
            Today = today,
            Days = days,
            EmptyRanges = emptyRanges,
            Upcoming = upcoming,
        };
    }

    private static List<AgendaItem> CarryLate(IEnumerable<AgendaItem> all, Func<AgendaItem, bool> where, DateOnly today) =>
        all.Where(i => i.Status == AgendaStatuses.Late && where(i))
            .OrderBy(i => i.Date).ThenBy(i => i.Title, StringComparer.Ordinal).ThenBy(i => i.Id, StringComparer.Ordinal)
            .Select(i =>
            {
                var c = i.Clone();
                c.OriginalDate = i.Date;
                c.Date = today;
                return c;
            })
            .ToList();

    /// <summary>
    /// Trente jours glissants depuis aujourd'hui, sans routine ni manquant, et sans aucun retard : tout
    /// impayé est déjà porté dans le jour courant de <c>days</c>, toujours rendu, dans la fenêtre ou au-dessus.
    /// Rien de ce que la fenêtre affichée montre déjà n'y figure : un item daté dans [from, to] en sort, et
    /// une journée entière de plusieurs jours dont au moins un jour est dans la fenêtre en sort tout entière
    /// (elle se poursuit à l'écran, elle ne recommence pas en bas). Entièrement hors fenêtre, elle n'y figure
    /// qu'une fois, datée de son premier jour. La fenêtre annoncée (<c>From</c>, <c>To</c>) reste aujourd'hui
    /// et aujourd'hui + 29, le filtre ne la change pas.
    /// </summary>
    private static AgendaUpcoming BuildUpcoming(List<AgendaItem> all, DateOnly today, DateOnly from, DateOnly to)
    {
        var upTo = today.AddDays(UpcomingDays - 1);
        var shownIds = all.Where(i => i.Date >= from && i.Date <= to).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var items = all
            .Where(i => i.Kind != AgendaKinds.Missing && !i.IsRoutine && i.Status != AgendaStatuses.Late)
            .Where(i => i.Date >= today && i.Date <= upTo && !shownIds.Contains(i.Id))
            .OrderBy(i => i.Date).ThenBy(SortGroup).ThenBy(i => i.Start, StringComparer.Ordinal).ThenBy(i => i.Title, StringComparer.Ordinal)
            .DistinctBy(i => i.Id)
            .ToList();
        return new AgendaUpcoming { From = today, To = upTo, Items = items };
    }

    /// <summary>
    /// Pour chaque série hebdomadaire d'au moins trois occurrences passées : un item « Pas de … » sur chaque
    /// jour de la fenêtre qui tombe sur le jour de semaine de la série sans qu'aucune occurrence, normale
    /// ou exception, n'y figure. Borné à [première, dernière] occurrence connue : une série terminée ne
    /// manque pas, elle est finie. Une occurrence déplacée laisse donc un manquant sur son jour d'origine.
    /// </summary>
    private static List<AgendaItem> MissingOccurrences(List<AgendaItem> all, DateOnly from, DateOnly to, DateOnly today)
    {
        var result = new List<AgendaItem>();
        var series = all
            .Where(i => i.Kind == AgendaKinds.Event && i.SeriesKey != null && i.Recurrence == CalendarRecurrence.Weekly)
            .GroupBy(i => i.SeriesKey!);

        foreach (var group in series)
        {
            var regular = group.Where(i => !i.IsException).ToList();
            if (regular.Count(i => i.Date < today) < MissingMinPastOccurrences) continue;

            var weekday = regular.GroupBy(i => i.Date.DayOfWeek)
                .OrderByDescending(g => g.Count()).ThenBy(g => (int)g.Key)
                .First().Key;
            var template = regular.Where(i => i.Date.DayOfWeek == weekday).OrderByDescending(i => i.Date).First();
            var covered = group.Select(i => i.Date).ToHashSet();
            var first = group.Min(i => i.Date);
            var last = group.Max(i => i.Date);

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek != weekday || day < first || day > last || covered.Contains(day)) continue;
                result.Add(new AgendaItem
                {
                    Id = $"missing:{group.Key}:{day:yyyy-MM-dd}",
                    Kind = AgendaKinds.Missing,
                    Date = day,
                    Start = template.Start,
                    End = template.End,
                    IsAllDay = template.IsAllDay,
                    Title = $"Pas de {template.Title}",
                    IsRoutine = true,
                    SeriesKey = group.Key,
                    Recurrence = CalendarRecurrence.Weekly,
                });
            }
        }
        return result;
    }

    /// <summary>
    /// Retards portés, puis journée entière, puis par heure, puis échéances sans heure, puis récurrentes (réglées
    /// ou planifiées). Une récurrente réglée porte sa date réelle dans OriginalDate : ce n'est pas un retard porté,
    /// elle reste avec les récurrentes.
    /// </summary>
    private static int SortGroup(AgendaItem i)
    {
        if (i.Status == AgendaStatuses.Late && i.OriginalDate.HasValue) return 0;
        if (i.IsAllDay) return 1;
        if (i.Start != null) return 2;
        if (i.Kind == AgendaKinds.Echeance) return 3;
        if (i.Kind == AgendaKinds.Recurring) return 4;
        return 5;
    }

    /// <summary>Dans un groupe, les retards portés par date d'origine ; la date réelle d'une récurrente réglée ne trie rien, le titre suffit.</summary>
    private static List<AgendaItem> SortDay(IEnumerable<AgendaItem> items) =>
        items.OrderBy(SortGroup)
            .ThenBy(i => i.Status == AgendaStatuses.Late ? i.OriginalDate : null)
            .ThenBy(i => i.Start, StringComparer.Ordinal)
            .ThenBy(i => i.Title, StringComparer.Ordinal)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();

    private static List<AgendaItem> SortRoutine(IEnumerable<AgendaItem> items) =>
        items.OrderBy(i => i.IsAllDay ? 0 : 1)
            .ThenBy(i => i.Start, StringComparer.Ordinal)
            .ThenBy(i => i.Title, StringComparer.Ordinal)
            .ThenBy(i => i.Id, StringComparer.Ordinal)
            .ToList();
}
