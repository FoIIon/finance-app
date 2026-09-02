using FinanceApp.API.DTOs;
using FinanceApp.API.Models;

namespace FinanceApp.API.Services.Reporting;

/// <summary>
/// Le « reste du mois » jour par jour et sa projection de fin de mois. Remplace la note manuelle
/// d'Audrey. Pure : le service charge les lignes, ce builder ne fait que compter.
///
/// Reste = entrées − dépenses, avec les blocs du bilan : un remboursement réduit les dépenses au
/// lieu de gonfler les entrées, ce qui ne change pas le reste mais aligne la courbe sur le bilan.
/// Les mises de côté et le hors bilan n'entrent pas dans le reste.
/// </summary>
public static class BurndownBuilder
{
    /// <summary>Fenêtre du rythme variable, en jours glissants.</summary>
    public const int PaceWindowDays = 14;

    /// <param name="monthLines">Toutes les transactions du mois, quel que soit leur bloc.</param>
    /// <param name="paceLines">Transactions des <see cref="PaceWindowDays"/> derniers jours, tous blocs.</param>
    /// <param name="recurrings">Récurrentes actives du dashboard, hors catégories de transfert.</param>
    /// <param name="provisionedRecurringIds">Récurrentes dont la provision du mois existe déjà en base.</param>
    public static BurndownDto Build(
        int year,
        int month,
        DateTime now,
        IReadOnlyList<ReportLine> monthLines,
        IReadOnlyList<ReportLine> paceLines,
        IReadOnlyList<RecurringTransaction> recurrings,
        IReadOnlySet<int> provisionedRecurringIds)
    {
        var lastDay = DateTime.DaysInMonth(year, month);
        var start = new DateTime(year, month, 1);
        var end = start.AddMonths(1);

        var isPast = end <= now;
        var isCurrent = start <= now && now < end;
        var isFuture = start > now;

        // Jour « aujourd'hui » dans ce mois : courant → now.Day, passé → dernier jour, futur → 0.
        var todayDay = isCurrent ? now.Day : (isPast ? lastDay : 0);

        var spentDelta = new decimal[lastDay + 1];
        var incomeDelta = new decimal[lastDay + 1];
        foreach (var x in ReportLines.Classify(monthLines))
        {
            var d = x.Line.Date.Day;
            if (d < 1 || d > lastDay) continue;
            if (x.Entry.Block == BilanBlock.Entrees) incomeDelta[d] += x.Entry.Amount;
            else if (x.Entry.IsExpenseBlock) spentDelta[d] += x.Entry.Amount;
        }

        var days = new List<BurndownDayDto>(lastDay);
        decimal cumSpent = 0, cumIncome = 0, remainingToday = 0;
        for (var d = 1; d <= lastDay; d++)
        {
            cumSpent += spentDelta[d];
            cumIncome += incomeDelta[d];
            var isFutureDay = isFuture || (isCurrent && d > todayDay);
            days.Add(new BurndownDayDto
            {
                Day = d,
                Date = start.AddDays(d - 1).ToString("yyyy-MM-dd"),
                Spent = isFutureDay ? null : cumSpent,
                Income = isFutureDay ? null : cumIncome,
                Remaining = isFutureDay ? null : cumIncome - cumSpent,
            });
            if (!isFutureDay) remainingToday = cumIncome - cumSpent;
        }

        var daysRemaining = isCurrent ? lastDay - todayDay : (isFuture ? lastDay : 0);

        // Rythme variable : dépenses du bloc Variable (sens dépense seulement, on ne nette pas les
        // remboursements ici, ils ne disent rien du rythme) sur la fenêtre glissante.
        var dailyPaceVariable = ReportLines.Classify(paceLines)
            .InBlock(BilanBlock.Variable)
            .Where(x => x.Line.Type == TransactionType.Expense)
            .Sum(x => x.Line.Amount) / PaceWindowDays;

        decimal upcomingExpenses = 0, upcomingIncome = 0;
        if (!isPast)
        {
            var fromDay = isCurrent ? todayDay + 1 : 1;
            foreach (var r in recurrings)
            {
                // Une récurrente provisionnée est déjà dans le cumul, via la provision du jour 1 ou le
                // versement réel réconcilié. La compter ici la doublerait.
                if (r.ProvisionAtMonthStart || provisionedRecurringIds.Contains(r.Id)) continue;
                foreach (var _ in RecurringOccurrenceDays(r, year, month, fromDay, lastDay))
                {
                    if (r.Type == TransactionType.Expense) upcomingExpenses += r.Amount;
                    else upcomingIncome += r.Amount;
                }
            }
        }

        var projected = isPast
            ? remainingToday
            : remainingToday - dailyPaceVariable * daysRemaining - upcomingExpenses + upcomingIncome;

        return new BurndownDto
        {
            Year = year,
            Month = month,
            Days = days,
            RemainingToday = remainingToday,
            DailyPaceVariable = dailyPaceVariable,
            UpcomingRecurringExpenses = upcomingExpenses,
            UpcomingRecurringIncome = upcomingIncome,
            RecurringIncluded = true,
            ProjectedEndOfMonth = projected,
            DaysRemaining = daysRemaining,
            IsPast = isPast,
            TodayDay = isCurrent ? todayDay : null,
        };
    }

    /// <summary>
    /// Jours du mois dans [fromDay, toDay] où une récurrente tombe, en respectant début, fin et cadence.
    /// Mensuelle : DayOfMonth, ramené au dernier jour du mois s'il le dépasse. Annuelle : anniversaire
    /// de StartDate. Hebdomadaire : tous les sept jours depuis StartDate.
    /// </summary>
    public static IEnumerable<int> RecurringOccurrenceDays(RecurringTransaction r, int year, int month, int fromDay, int toDay)
    {
        if (fromDay > toDay) yield break;
        var lastDay = DateTime.DaysInMonth(year, month);
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = new DateOnly(year, month, lastDay);
        if (r.StartDate > monthEnd) yield break;
        if (r.EndDate.HasValue && r.EndDate.Value < monthStart) yield break;

        switch (r.Frequency)
        {
            case RecurringFrequency.Monthly:
            {
                var day = Math.Min(r.DayOfMonth ?? r.StartDate.Day, lastDay);
                var occ = new DateOnly(year, month, day);
                if (day >= fromDay && day <= toDay && occ >= r.StartDate
                    && (!r.EndDate.HasValue || occ <= r.EndDate.Value))
                    yield return day;
                break;
            }
            case RecurringFrequency.Yearly:
            {
                if (r.StartDate.Month != month) break;
                var day = Math.Min(r.StartDate.Day, lastDay);
                var occ = new DateOnly(year, month, day);
                if (day >= fromDay && day <= toDay && occ >= r.StartDate
                    && (!r.EndDate.HasValue || occ <= r.EndDate.Value))
                    yield return day;
                break;
            }
            case RecurringFrequency.Weekly:
            {
                for (var day = fromDay; day <= toDay; day++)
                {
                    var occ = new DateOnly(year, month, day);
                    if (occ < r.StartDate) continue;
                    if (r.EndDate.HasValue && occ > r.EndDate.Value) continue;
                    if ((occ.DayNumber - r.StartDate.DayNumber) % 7 == 0)
                        yield return day;
                }
                break;
            }
        }
    }
}
