using FinanceApp.API.Models;

namespace FinanceApp.API.Services.Reporting;

/// <summary>
/// Trois projections indépendantes vers <see cref="AgendaItem"/>, chacune ignorante des deux autres.
/// Pures, statiques, testées seules. Rien ici ne touche au bilan : une échéance, une occurrence de
/// calendrier ou une récurrente projetée reste hors de BilanClassifier.
/// </summary>
public static class AgendaProjectors
{
    /// <summary>Au-delà, une journée entière de plusieurs jours n'est plus répétée jour par jour.</summary>
    public const int MaxAllDaySpanDays = 31;

    /// <summary>
    /// Une occurrence de calendrier par jour couvert : une journée entière de plusieurs jours apparaît
    /// chaque jour, sous le même id (l'id désigne l'occurrence, la date le jour).
    /// </summary>
    public static List<AgendaItem> FromCalendar(IEnumerable<CalendarOccurrence> occurrences)
    {
        var result = new List<AgendaItem>();
        foreach (var o in occurrences)
        {
            var id = $"event:{o.Uid}:{DateTime.SpecifyKind(o.OccurrenceStart, DateTimeKind.Utc):yyyy-MM-dd'T'HH:mm:ss'Z'}";
            var lastDay = o.IsAllDay && o.LocalEndDate > o.LocalDate
                ? Min(o.LocalEndDate, o.LocalDate.AddDays(MaxAllDaySpanDays - 1))
                : o.LocalDate;
            for (var day = o.LocalDate; day <= lastDay; day = day.AddDays(1))
            {
                result.Add(new AgendaItem
                {
                    Id = id,
                    Kind = AgendaKinds.Event,
                    Date = day,
                    Start = o.IsAllDay ? null : o.LocalStart?.ToString("HH:mm"),
                    End = o.IsAllDay ? null : o.LocalEnd?.ToString("HH:mm"),
                    IsAllDay = o.IsAllDay,
                    Title = o.Summary,
                    Location = o.Location,
                    SeriesKey = o.Uid,
                    Recurrence = o.Recurrence,
                    IsException = o.IsException,
                });
            }
        }
        return result;
    }

    /// <summary>Le statut vient d'EcheanceStatusRules, calculé pour la date du jour du ménage.</summary>
    public static List<AgendaItem> FromEcheances(IEnumerable<Echeance> echeances, DateOnly today) =>
        echeances.Select(e => new AgendaItem
        {
            Id = $"echeance:{e.Id}",
            Kind = AgendaKinds.Echeance,
            Date = e.DueDate,
            Title = e.Label,
            Amount = e.Amount,
            Status = Services.EcheanceStatusRules.Of(e, today) switch
            {
                EcheanceStatus.Payee => AgendaStatuses.Paid,
                EcheanceStatus.EnRetard => AgendaStatuses.Late,
                _ => AgendaStatuses.Due,
            },
            EcheanceId = e.Id,
            TransactionId = e.TransactionId,
        }).ToList();

    /// <summary>
    /// Une occurrence par jour rendu par BurndownBuilder.RecurringOccurrenceDays, mois par mois sur la
    /// fenêtre, toutes planifiées. Lecture seule : ce sont les prêts, l'énergie, les assurances déjà en base.
    /// </summary>
    public static List<AgendaItem> FromRecurring(IEnumerable<RecurringTransaction> actives, DateOnly from, DateOnly to) =>
        FromRecurring(actives, from, to, Array.Empty<SettlementCandidate>());

    /// <summary>
    /// Même projection, puis chaque occurrence cherche dans les candidats la transaction de son mois qui la
    /// règle (<see cref="RecurringSettlement.Settle"/>). Réglée : statut paid, montant réel, date réelle dans
    /// OriginalDate, la date de l'item reste la date théorique parce que la routine est le plan. Les récurrentes
    /// sont parcourues par Id croissant et leurs occurrences par date croissante, une transaction retenue ne
    /// règle rien d'autre : le résultat ne dépend pas de l'ordre de lecture.
    /// </summary>
    public static List<AgendaItem> FromRecurring(IEnumerable<RecurringTransaction> actives, DateOnly from, DateOnly to, IEnumerable<SettlementCandidate> candidates)
    {
        var result = new List<AgendaItem>();
        if (to < from) return result;
        var recurrings = actives.Where(r => r.IsActive).OrderBy(r => r.Id).ToList();
        if (recurrings.Count == 0) return result;

        var pool = candidates as IReadOnlyCollection<SettlementCandidate> ?? candidates.ToList();
        var claimed = new HashSet<int>();

        foreach (var r in recurrings)
        {
            foreach (var date in OccurrenceDates(r, from, to))
            {
                var item = new AgendaItem
                {
                    Id = $"recurring:{r.Id}:{date:yyyy-MM-dd}",
                    Kind = AgendaKinds.Recurring,
                    Date = date,
                    Title = r.Description,
                    Amount = r.Amount,
                    Status = AgendaStatuses.Planned,
                };

                var settled = pool.Count == 0 ? null : RecurringSettlement.Settle(r, date, pool, claimed);
                if (settled != null)
                {
                    claimed.Add(settled.Id);
                    item.Status = AgendaStatuses.Paid;
                    item.TransactionId = settled.Id;
                    item.Amount = Math.Abs(settled.Amount);
                    item.OriginalDate = settled.Date;
                }
                result.Add(item);
            }
        }
        return result;
    }

    /// <summary>Les dates d'occurrence d'une récurrente sur la fenêtre, mois par mois, en ordre croissant.</summary>
    private static IEnumerable<DateOnly> OccurrenceDates(RecurringTransaction r, DateOnly from, DateOnly to)
    {
        for (var month = new DateOnly(from.Year, from.Month, 1); month <= to; month = month.AddMonths(1))
        {
            var lastDay = DateTime.DaysInMonth(month.Year, month.Month);
            var fromDay = month.Year == from.Year && month.Month == from.Month ? from.Day : 1;
            var toDay = month.Year == to.Year && month.Month == to.Month ? to.Day : lastDay;
            foreach (var day in BurndownBuilder.RecurringOccurrenceDays(r, month.Year, month.Month, fromDay, toDay))
                yield return new DateOnly(month.Year, month.Month, day);
        }
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
}
