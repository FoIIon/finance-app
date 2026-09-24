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

    private static RecurringTransaction Engie(int id = 1, decimal amount = 400m) => new()
    {
        Id = id, Description = "ENGIE — gaz/électricité", Amount = amount, Type = TransactionType.Expense,
        Frequency = RecurringFrequency.Monthly, DayOfMonth = 24, StartDate = new DateOnly(2025, 1, 24), IsActive = true,
    };

    private static SettlementCandidate Tx(int id, DateOnly date, decimal amount, string description = "ENGIE ELECTRABEL", int? recurringId = null) =>
        new(id, date, amount, TransactionType.Expense, description, null, recurringId);

    [Fact]
    public void FromRecurring_AvecCandidats_LOccurrenceRegleePorteLeStatutLeMontantEtLaDateReels_SaDateResteTheorique()
    {
        var items = AgendaProjectors.FromRecurring(new[] { Engie() }, new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31),
            new[] { Tx(5, new DateOnly(2026, 9, 16), 357.06m) });

        Assert.Equal(2, items.Count);
        var septembre = Assert.Single(items, i => i.Date.Month == 9);
        Assert.Equal("recurring:1:2026-09-24", septembre.Id);
        Assert.Equal(new DateOnly(2026, 9, 24), septembre.Date);
        Assert.Equal("paid", septembre.Status);
        Assert.Equal(5, septembre.TransactionId);
        Assert.Equal(357.06m, septembre.Amount);
        Assert.Equal(new DateOnly(2026, 9, 16), septembre.OriginalDate);
        Assert.Equal("ENGIE — gaz/électricité", septembre.Title);

        // Octobre n'a rien : planifiée, montant prévu, sans transaction ni date réelle.
        var octobre = Assert.Single(items, i => i.Date.Month == 10);
        Assert.Equal("planned", octobre.Status);
        Assert.Null(octobre.TransactionId);
        Assert.Equal(400m, octobre.Amount);
        Assert.Null(octobre.OriginalDate);
    }

    [Fact]
    public void FromRecurring_SansCandidats_LAncienneSignatureRendToutPlanifie()
    {
        var items = AgendaProjectors.FromRecurring(new[] { Engie() }, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));
        var seul = Assert.Single(items);
        Assert.Equal("planned", seul.Status);
        Assert.Null(seul.TransactionId);
    }

    [Fact]
    public void FromRecurring_UneTransactionNeRegleQuUneRecurrente_ParIdCroissant()
    {
        // Deux récurrentes à 400 le même mois, une seule transaction à 400 : la plus petite par Id la prend,
        // l'autre reste planifiée, quel que soit l'ordre de lecture des récurrentes.
        var a = Engie(id: 1);
        var b = Engie(id: 2);
        b.Description = "Assurance";
        var candidats = new[] { Tx(9, new DateOnly(2026, 9, 20), 400m, "Domiciliation") };

        var items = AgendaProjectors.FromRecurring(new[] { b, a }, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), candidats);

        Assert.Equal("paid", Assert.Single(items, i => i.Id.StartsWith("recurring:1:")).Status);
        Assert.Equal("planned", Assert.Single(items, i => i.Id.StartsWith("recurring:2:")).Status);
    }

    [Fact]
    public void FromRecurring_Hebdomadaire_LesOccurrencesDuMoisEntierReclamentDansLOrdre_MemeHorsFenetre()
    {
        var menage = new RecurringTransaction { Id = 8, Description = "Ménage", Amount = 60m, Type = TransactionType.Expense, Frequency = RecurringFrequency.Weekly, StartDate = new DateOnly(2026, 9, 1), IsActive = true };
        // Mardis de septembre : 1, 8, 15, 22, 29. Deux virements, les 9 et 16. L'occurrence du 1er, hors de la
        // fenêtre affichée, réclame d'abord (le 9, à huit jours), celle du 8 prend le 16, les suivantes n'ont rien.
        var candidats = new[] { Tx(21, new DateOnly(2026, 9, 16), 60m, "Ménage"), Tx(20, new DateOnly(2026, 9, 9), 60m, "Ménage") };

        var items = AgendaProjectors.FromRecurring(new[] { menage }, new DateOnly(2026, 9, 8), new DateOnly(2026, 9, 30), candidats)
            .OrderBy(i => i.Date).ToList();

        Assert.Equal(4, items.Count);
        Assert.Equal(new DateOnly(2026, 9, 8), items[0].Date);
        Assert.Equal(21, items[0].TransactionId);
        Assert.Equal("planned", items[1].Status);
        Assert.Equal("planned", items[2].Status);
        Assert.Equal("planned", items[3].Status);
        // Seules les occurrences de la fenêtre sont rendues.
        Assert.DoesNotContain(items, i => i.Date < new DateOnly(2026, 9, 8));
    }

    [Fact]
    public void FromRecurring_VueSemaineEtVueMois_ReglentLaMemeOccurrence()
    {
        var menage = new RecurringTransaction { Id = 8, Description = "Ménage", Amount = 60m, Type = TransactionType.Expense, Frequency = RecurringFrequency.Weekly, StartDate = new DateOnly(2026, 9, 1), IsActive = true };
        // Un seul virement, le 9. Les occurrences réclament par date croissante : le 1er (huit jours) le prend,
        // quelle que soit la fenêtre. La semaine du 8 ne le donne pas au 8, elle le voit déjà pris par le 1er.
        var candidats = new[] { Tx(20, new DateOnly(2026, 9, 9), 60m, "Ménage") };

        var mois = AgendaProjectors.FromRecurring(new[] { menage }, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), candidats);
        var semaine = AgendaProjectors.FromRecurring(new[] { menage }, new DateOnly(2026, 9, 8), new DateOnly(2026, 9, 14), candidats);

        var regleeMois = Assert.Single(mois, i => i.Status == "paid");
        Assert.Equal(new DateOnly(2026, 9, 1), regleeMois.Date);
        Assert.Equal(20, regleeMois.TransactionId);
        var le8 = Assert.Single(semaine);
        Assert.Equal(new DateOnly(2026, 9, 8), le8.Date);
        Assert.Equal("planned", le8.Status);
        Assert.Null(le8.TransactionId);
        Assert.Equal(Assert.Single(mois, i => i.Date == new DateOnly(2026, 9, 8)).Status, le8.Status);
    }

    [Fact]
    public void FromRecurring_UnCandidatLieALaRecurrente_ReglePlutotQueLeMontant()
    {
        var candidats = new[] { Tx(30, new DateOnly(2026, 9, 24), 400m, "Carte"), Tx(31, new DateOnly(2026, 9, 2), 12m, "Lien", recurringId: 1) };
        var seul = Assert.Single(AgendaProjectors.FromRecurring(new[] { Engie() }, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), candidats));
        Assert.Equal(31, seul.TransactionId);
        Assert.Equal(12m, seul.Amount);
        Assert.Equal(new DateOnly(2026, 9, 2), seul.OriginalDate);
    }

    [Fact]
    public void FromRecurring_FenetreInversee_RendVide()
    {
        var r = new RecurringTransaction { Id = 1, Frequency = RecurringFrequency.Monthly, DayOfMonth = 1, StartDate = new DateOnly(2025, 1, 1), IsActive = true };
        Assert.Empty(AgendaProjectors.FromRecurring(new[] { r }, new DateOnly(2026, 10, 1), new DateOnly(2026, 9, 1)));
    }
}
