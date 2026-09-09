using FinanceApp.API.Models;
using FinanceApp.API.Services.Reporting;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>Les trois projecteurs vers AgendaItem, chacun seul, sans base.</summary>
public class AgendaProjectorsTests
{
    private static readonly DateOnly Today = new(2026, 9, 9);

    [Fact]
    public void FromCalendar_IdHeuresEtSerie()
    {
        var occurrence = new CalendarOccurrence
        {
            Uid = "danse@test", OccurrenceStart = new DateTime(2026, 9, 10, 14, 45, 0, DateTimeKind.Utc),
            LocalDate = new DateOnly(2026, 9, 10), LocalStart = new TimeOnly(16, 45), LocalEnd = new TimeOnly(17, 45),
            LocalEndDate = new DateOnly(2026, 9, 10), Summary = "Danse Alice", Location = "Salle des fêtes",
            Recurrence = CalendarRecurrence.Weekly, IsException = false,
        };
        var item = Assert.Single(AgendaProjectors.FromCalendar(new[] { occurrence }));
        Assert.Equal("event:danse@test:2026-09-10T14:45:00Z", item.Id);
        Assert.Equal("event", item.Kind);
        Assert.Equal(new DateOnly(2026, 9, 10), item.Date);
        Assert.Equal("16:45", item.Start);
        Assert.Equal("17:45", item.End);
        Assert.False(item.IsAllDay);
        Assert.Equal("Danse Alice", item.Title);
        Assert.Equal("Salle des fêtes", item.Location);
        Assert.Null(item.Amount);
        Assert.Null(item.Status);
        Assert.Equal("danse@test", item.SeriesKey);
        Assert.Equal(CalendarRecurrence.Weekly, item.Recurrence);
        Assert.False(item.IsRoutine);
    }

    [Fact]
    public void FromCalendar_JourneeEntiereSurTroisJours_UnItemParJour_MemeId()
    {
        var occurrence = new CalendarOccurrence
        {
            Uid = "vac@test", OccurrenceStart = new DateTime(2026, 10, 9, 22, 0, 0, DateTimeKind.Utc), IsAllDay = true,
            LocalDate = new DateOnly(2026, 10, 10), LocalEndDate = new DateOnly(2026, 10, 12), Summary = "Vacances",
        };
        var items = AgendaProjectors.FromCalendar(new[] { occurrence });
        Assert.Equal(3, items.Count);
        Assert.Equal(new[] { 10, 11, 12 }, items.Select(i => i.Date.Day).ToArray());
        Assert.Single(items.Select(i => i.Id).Distinct());
        Assert.All(items, i => Assert.True(i.IsAllDay));
        Assert.All(items, i => Assert.Null(i.Start));
    }

    [Fact]
    public void FromCalendar_JourneeEntiereInterminable_EstBornee()
    {
        var occurrence = new CalendarOccurrence
        {
            Uid = "long@test", IsAllDay = true, LocalDate = new DateOnly(2026, 1, 1), LocalEndDate = new DateOnly(2026, 12, 31), Summary = "Année",
        };
        Assert.Equal(AgendaProjectors.MaxAllDaySpanDays, AgendaProjectors.FromCalendar(new[] { occurrence }).Count);
    }

    [Fact]
    public void FromEcheances_StatutDerive_MontantEtLiens()
    {
        var items = AgendaProjectors.FromEcheances(new[]
        {
            new Echeance { Id = 1, Label = "Taxe déchets", DueDate = new DateOnly(2026, 9, 2), Amount = 95m },
            new Echeance { Id = 2, Label = "Assurance", DueDate = new DateOnly(2026, 9, 30), Amount = 612.33m },
            new Echeance { Id = 3, Label = "Prêt", DueDate = new DateOnly(2026, 9, 2), Amount = 1250m, TransactionId = 340 },
            new Echeance { Id = 4, Label = "Ostéo", DueDate = new DateOnly(2026, 9, 9), Amount = null },
        }, Today);

        Assert.Equal(4, items.Count);
        Assert.All(items, i => Assert.Equal("echeance", i.Kind));
        Assert.All(items, i => Assert.Null(i.Start));
        Assert.Equal("echeance:1", items[0].Id);
        Assert.Equal("late", items[0].Status);
        Assert.Equal(95m, items[0].Amount);
        Assert.Equal(1, items[0].EcheanceId);
        Assert.Equal("due", items[1].Status);
        Assert.Equal("paid", items[2].Status);
        Assert.Equal(340, items[2].TransactionId);
        // Le jour même : encore à venir, et un montant inconnu reste null.
        Assert.Equal("due", items[3].Status);
        Assert.Null(items[3].Amount);
    }

    [Fact]
    public void FromRecurring_UneOccurrenceParJourRendu_MoisParMois()
    {
        var pret = new RecurringTransaction { Id = 7, Description = "Prêt hypothécaire", Amount = 1250m, Type = TransactionType.Expense, Frequency = RecurringFrequency.Monthly, DayOfMonth = 5, StartDate = new DateOnly(2025, 1, 5), IsActive = true };
        var hebdo = new RecurringTransaction { Id = 8, Description = "Ménage", Amount = 60m, Type = TransactionType.Expense, Frequency = RecurringFrequency.Weekly, StartDate = new DateOnly(2026, 9, 1), IsActive = true };
        var inactive = new RecurringTransaction { Id = 9, Description = "Ancien abonnement", Amount = 9.99m, Frequency = RecurringFrequency.Monthly, DayOfMonth = 10, StartDate = new DateOnly(2025, 1, 10), IsActive = false };

        // Du 9 septembre au 8 octobre : le 5 septembre est avant la fenêtre, le 5 octobre dedans.
        var items = AgendaProjectors.FromRecurring(new[] { pret, hebdo, inactive }, new DateOnly(2026, 9, 9), new DateOnly(2026, 10, 8));

        var prets = items.Where(i => i.Id.StartsWith("recurring:7:")).ToList();
        Assert.Single(prets);
        Assert.Equal("recurring:7:2026-10-05", prets[0].Id);
        Assert.Equal(new DateOnly(2026, 10, 5), prets[0].Date);
        Assert.Equal("planned", prets[0].Status);
        Assert.Equal(1250m, prets[0].Amount);
        Assert.Equal("Prêt hypothécaire", prets[0].Title);

        // Mardis : 15, 22, 29 septembre, 6 octobre.
        var menages = items.Where(i => i.Id.StartsWith("recurring:8:")).Select(i => i.Date).ToList();
        Assert.Equal(new[] { new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 22), new DateOnly(2026, 9, 29), new DateOnly(2026, 10, 6) }, menages);

        Assert.DoesNotContain(items, i => i.Id.StartsWith("recurring:9:"));
    }

    [Fact]
    public void FromRecurring_FenetreInversee_RendVide()
    {
        var r = new RecurringTransaction { Id = 1, Frequency = RecurringFrequency.Monthly, DayOfMonth = 1, StartDate = new DateOnly(2025, 1, 1), IsActive = true };
        Assert.Empty(AgendaProjectors.FromRecurring(new[] { r }, new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1)));
    }
}
