using FinanceApp.API.Models;
using FinanceApp.API.Services.Reporting;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>Toutes les règles de l'écran Agenda vivent dans AgendaBuilder. Aucune n'est en React, donc chacune a son test ici.</summary>
public class AgendaBuilderTests
{
    private static readonly DateOnly Today = new(2026, 9, 9); // mercredi
    private static readonly DateOnly WeekFrom = Today;
    private static readonly DateOnly WeekTo = Today.AddDays(6);

    private static AgendaItem Event(string uid, DateOnly date, string? start = "16:45", CalendarRecurrence recurrence = CalendarRecurrence.None, bool exception = false, string title = "Danse", bool allDay = false) => new()
    {
        Id = $"event:{uid}:{date:yyyy-MM-dd}T00:00:00Z", Kind = AgendaKinds.Event, Date = date, Start = allDay ? null : start, End = allDay ? null : "17:45",
        IsAllDay = allDay, Title = title, SeriesKey = uid, Recurrence = recurrence, IsException = exception,
    };

    private static AgendaItem Echeance(int id, DateOnly date, string status, decimal? amount = 100m, string title = "Taxe") => new()
    {
        Id = $"echeance:{id}", Kind = AgendaKinds.Echeance, Date = date, Title = title, Amount = amount, Status = status, EcheanceId = id,
    };

    private static AgendaItem Recurring(int id, DateOnly date, decimal amount = 1250m, string title = "Prêt") => new()
    {
        Id = $"recurring:{id}:{date:yyyy-MM-dd}", Kind = AgendaKinds.Recurring, Date = date, Title = title, Amount = amount, Status = AgendaStatuses.Planned,
    };

    /// <summary>Quatre jeudis hebdomadaires : 20 et 27 août, 3 septembre, 17 septembre. Le 10 manque.</summary>
    private static List<AgendaItem> DanseAvecTrou() => new()
    {
        Event("danse", new DateOnly(2026, 8, 20), recurrence: CalendarRecurrence.Weekly),
        Event("danse", new DateOnly(2026, 8, 27), recurrence: CalendarRecurrence.Weekly),
        Event("danse", new DateOnly(2026, 9, 3), recurrence: CalendarRecurrence.Weekly),
        Event("danse", new DateOnly(2026, 9, 17), recurrence: CalendarRecurrence.Weekly),
    };

    private static AgendaDay Day(AgendaResult r, DateOnly date) => Assert.Single(r.Days, d => d.Date == date);

    [Fact]
    public void VueSemaine_SeptJoursTousRendus_MemeVides()
    {
        var r = AgendaBuilder.Build(WeekFrom, WeekTo, Today, AgendaView.Week, Array.Empty<AgendaItem>());
        Assert.Equal("week", r.View);
        Assert.Equal(7, r.Days.Count);
        Assert.Empty(r.EmptyRanges);
        Assert.Equal(WeekFrom, r.Days[0].Date);
        Assert.True(r.Days[0].IsToday);
        Assert.All(r.Days.Skip(1), d => Assert.False(d.IsToday));
    }

    [Fact]
    public void Routine_DeriveeDeLaSerieHebdomadaire_PasDUneListeDeLibelles()
    {
        var items = new List<AgendaItem>
        {
            Event("danse", new DateOnly(2026, 9, 10), recurrence: CalendarRecurrence.Weekly),
            Event("danse", new DateOnly(2026, 9, 12), recurrence: CalendarRecurrence.Weekly, exception: true, title: "Danse (déplacée)"),
            Event("dentiste", new DateOnly(2026, 9, 11), title: "Dentiste"),
            Event("anniv", new DateOnly(2026, 9, 13), recurrence: CalendarRecurrence.Yearly, title: "Anniversaire", allDay: true),
        };
        var r = AgendaBuilder.Build(WeekFrom, WeekTo, Today, AgendaView.Week, items);

        var jeudi = Day(r, new DateOnly(2026, 9, 10));
        Assert.Empty(jeudi.Items);
        Assert.True(Assert.Single(jeudi.Routine).IsRoutine);

        // Une exception n'est pas de la routine : elle sort du pli, on doit la voir.
        var samedi = Day(r, new DateOnly(2026, 9, 12));
        Assert.Empty(samedi.Routine);
        Assert.False(Assert.Single(samedi.Items).IsRoutine);

        Assert.False(Assert.Single(Day(r, new DateOnly(2026, 9, 11)).Items).IsRoutine);
        Assert.False(Assert.Single(Day(r, new DateOnly(2026, 9, 13)).Items).IsRoutine);
    }

    [Fact]
    public void RetardAnterieurALaFenetre_PorteDansAujourdhui_AvecSaDateDOrigine_EnTete()
    {
        var items = new List<AgendaItem>
        {
            Echeance(1, new DateOnly(2026, 9, 2), AgendaStatuses.Late, title: "Taxe déchets"),
            Echeance(2, new DateOnly(2026, 8, 12), AgendaStatuses.Late, title: "Ostéo"),
            Echeance(3, new DateOnly(2026, 9, 1), AgendaStatuses.Paid, title: "Payée, reste dans le passé"),
            Event("dentiste", Today, start: "08:00", title: "Dentiste"),
            Echeance(4, Today, AgendaStatuses.Due, title: "Du jour"),
        };
        var r = AgendaBuilder.Build(WeekFrom, WeekTo, Today, AgendaView.Week, items);
        var today = Day(r, Today);

        Assert.Equal(4, today.Items.Count);
        Assert.Equal("echeance:2", today.Items[0].Id);
        Assert.Equal(new DateOnly(2026, 8, 12), today.Items[0].OriginalDate);
        Assert.Equal(Today, today.Items[0].Date);
        Assert.Equal("late", today.Items[0].Status);
        Assert.Equal("echeance:1", today.Items[1].Id);
        Assert.Equal(new DateOnly(2026, 9, 2), today.Items[1].OriginalDate);
        Assert.Equal("event:dentiste:2026-09-09T00:00:00Z", today.Items[2].Id);
        Assert.Equal("echeance:4", today.Items[3].Id);
        Assert.Null(today.Items[3].OriginalDate);
        // La payée du 1er septembre n'est ni portée ni dans la semaine.
        Assert.DoesNotContain(r.Days.SelectMany(d => d.Items), i => i.Id == "echeance:3");
    }

    [Fact]
    public void VueMois_RetardDansLaFenetreAvantAujourdhui_PorteDansAujourdhui_AbsentDeSonJourDOrigine()
    {
        // Le 4 septembre est dans le mois affiché et avant aujourd'hui. En semaine il serait porté, en mois
        // il restait à sa date : deux comportements pour la même échéance. Il est porté dans les deux vues.
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);
        var items = new List<AgendaItem>
        {
            Echeance(1, new DateOnly(2026, 9, 4), AgendaStatuses.Late, title: "Taxe déchets"),
            Echeance(2, new DateOnly(2026, 9, 4), AgendaStatuses.Paid, title: "Payée, reste à sa date"),
            Event("dentiste", new DateOnly(2026, 9, 15), title: "Dentiste"),
        };
        var r = AgendaBuilder.Build(from, to, Today, AgendaView.Month, items);

        var today = Day(r, Today);
        var porte = Assert.Single(today.Items);
        Assert.Equal("echeance:1", porte.Id);
        Assert.Equal("late", porte.Status);
        Assert.Equal(new DateOnly(2026, 9, 4), porte.OriginalDate);
        Assert.Equal(Today, porte.Date);

        var le4 = Day(r, new DateOnly(2026, 9, 4));
        Assert.Equal("echeance:2", Assert.Single(le4.Items).Id);
        Assert.Null(le4.Items[0].OriginalDate);
        Assert.Equal(1, r.Days.SelectMany(d => d.Items).Count(i => i.Id == "echeance:1"));
    }

    [Fact]
    public void VueMois_JourPasseQuiNAvaitQueLeRetard_DevientVide_RejointLesPlagesVides()
    {
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);
        var items = new List<AgendaItem> { Echeance(1, new DateOnly(2026, 9, 4), AgendaStatuses.Late) };
        var r = AgendaBuilder.Build(from, to, Today, AgendaView.Month, items);

        Assert.Equal(new[] { Today }, r.Days.Select(d => d.Date).ToArray());
        Assert.Equal("echeance:1", Assert.Single(r.Days[0].Items).Id);
        Assert.Equal(new[] { (1, 8), (10, 30) }, r.EmptyRanges.Select(e => (e.From.Day, e.To.Day)).ToArray());
        Assert.Contains(r.EmptyRanges, e => e.From <= new DateOnly(2026, 9, 4) && new DateOnly(2026, 9, 4) <= e.To);
    }

    [Fact]
    public void VueMois_JourPasseAvecRetardEtEvenement_GardeLEvenement_PerdLeRetard()
    {
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);
        var items = new List<AgendaItem>
        {
            Echeance(1, new DateOnly(2026, 9, 4), AgendaStatuses.Late),
            Event("dentiste", new DateOnly(2026, 9, 4), title: "Dentiste"),
        };
        var r = AgendaBuilder.Build(from, to, Today, AgendaView.Month, items);

        var le4 = Day(r, new DateOnly(2026, 9, 4));
        Assert.Equal("Dentiste", Assert.Single(le4.Items).Title);
        Assert.Equal("echeance:1", Assert.Single(Day(r, Today).Items).Id);
        Assert.DoesNotContain(r.EmptyRanges, e => e.From <= new DateOnly(2026, 9, 4) && new DateOnly(2026, 9, 4) <= e.To);
    }

    [Fact]
    public void VueSemaine_RetardDansLaFenetreAvantAujourdhui_PorteCommeEnVueMois()
    {
        // Semaine du 7 au 13 septembre, aujourd'hui le 9 : le retard du 8 est dans la fenêtre. Même règle.
        var from = new DateOnly(2026, 9, 7);
        var to = new DateOnly(2026, 9, 13);
        var items = new List<AgendaItem> { Echeance(1, new DateOnly(2026, 9, 8), AgendaStatuses.Late) };
        var r = AgendaBuilder.Build(from, to, Today, AgendaView.Week, items);

        Assert.Equal(7, r.Days.Count);
        Assert.Empty(Day(r, new DateOnly(2026, 9, 8)).Items);
        var porte = Assert.Single(Day(r, Today).Items);
        Assert.Equal(new DateOnly(2026, 9, 8), porte.OriginalDate);
        Assert.Equal(Today, porte.Date);
    }

    [Fact]
    public void AVenir_RetardDansLaFenetre_PorteUneSeuleFois_DansLesJours_PasDansLAVenir()
    {
        // Le mois affiché contient aujourd'hui : le retard est porté dans Aujourd'hui, l'échéance du 12 est
        // sur son jour. L'à venir ne répète ni l'un ni l'autre, chaque item n'apparaît qu'une fois à l'écran.
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);
        var items = new List<AgendaItem>
        {
            Echeance(1, new DateOnly(2026, 9, 4), AgendaStatuses.Late, title: "Retard"),
            Echeance(2, new DateOnly(2026, 9, 12), AgendaStatuses.Due, title: "À venir"),
        };
        var r = AgendaBuilder.Build(from, to, Today, AgendaView.Month, items);

        Assert.Empty(r.Upcoming.Items);
        Assert.Equal(1, r.Days.SelectMany(d => d.Items).Count(i => i.Id == "echeance:1"));
        Assert.Equal(1, r.Days.SelectMany(d => d.Items).Count(i => i.Id == "echeance:2"));
    }

    [Fact]
    public void AujourdhuiHorsFenetre_LesRetardsSontRendusDansUnJourAujourdhui_AjouteEnTete()
    {
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 31);
        var items = new List<AgendaItem>
        {
            Echeance(1, new DateOnly(2026, 9, 2), AgendaStatuses.Late),
            Echeance(2, new DateOnly(2026, 10, 15), AgendaStatuses.Due),
        };
        var r = AgendaBuilder.Build(from, to, Today, AgendaView.Month, items);

        Assert.Equal(Today, r.Days[0].Date);
        Assert.True(r.Days[0].IsToday);
        Assert.Equal("echeance:1", Assert.Single(r.Days[0].Items).Id);
        Assert.Equal(new DateOnly(2026, 9, 2), r.Days[0].Items[0].OriginalDate);
        Assert.Equal(new DateOnly(2026, 10, 15), r.Days[1].Date);
        Assert.False(r.Days[1].IsToday);
        Assert.DoesNotContain(r.EmptyRanges, e => e.From <= Today && Today <= e.To);
    }

    [Fact]
    public void RetardEntreLaFenetreEtAujourdhui_EstPorteDansAujourdhui()
    {
        // On regarde juillet, on est le 9 septembre : un retard d'août n'est ni dans le mois affiché ni
        // dans le futur, il serait sur aucun écran. Il est porté dans le jour d'aujourd'hui, ajouté en
        // tête, et le retard de juillet l'y rejoint : un impayé est porté quelle que soit sa date.
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 31);
        var items = new List<AgendaItem>
        {
            Echeance(1, new DateOnly(2026, 8, 12), AgendaStatuses.Late, title: "Retard d'août"),
            Echeance(2, new DateOnly(2026, 7, 20), AgendaStatuses.Late, title: "Retard de juillet"),
            Echeance(3, new DateOnly(2026, 7, 20), AgendaStatuses.Paid, title: "Payée de juillet"),
        };
        var r = AgendaBuilder.Build(from, to, Today, AgendaView.Month, items);

        Assert.Equal(Today, r.Days[0].Date);
        Assert.True(r.Days[0].IsToday);
        Assert.Equal(new[] { "echeance:2", "echeance:1" }, r.Days[0].Items.Select(i => i.Id).ToArray());
        Assert.Equal(new DateOnly(2026, 7, 20), r.Days[0].Items[0].OriginalDate);
        Assert.Equal(new DateOnly(2026, 8, 12), r.Days[0].Items[1].OriginalDate);
        Assert.All(r.Days[0].Items, i => Assert.Equal(Today, i.Date));
        var juillet = Day(r, new DateOnly(2026, 7, 20));
        Assert.Equal("echeance:3", Assert.Single(juillet.Items).Id);
        Assert.DoesNotContain(r.Days.Skip(1).SelectMany(d => d.Items), i => i.Status == AgendaStatuses.Late);
    }

    [Fact]
    public void JourneeEntiereDePlusieursJours_UnItemParJourDansDays_UnSeulDansUpcoming()
    {
        // Trois jours de vacances : le projecteur rend trois items au même id, un par jour.
        var vacances = new[] { 10, 11, 12 }.Select(day => new AgendaItem
        {
            Id = "event:vacances@test:2026-09-09T22:00:00Z", Kind = AgendaKinds.Event, Date = new DateOnly(2026, 9, day),
            IsAllDay = true, Title = "Vacances", SeriesKey = "vacances@test",
        }).ToList();
        var r = AgendaBuilder.Build(WeekFrom, WeekTo, Today, AgendaView.Week, vacances);

        Assert.Equal("Vacances", Assert.Single(Day(r, new DateOnly(2026, 9, 10)).Items).Title);
        Assert.Equal("Vacances", Assert.Single(Day(r, new DateOnly(2026, 9, 11)).Items).Title);
        Assert.Equal("Vacances", Assert.Single(Day(r, new DateOnly(2026, 9, 12)).Items).Title);
        Assert.Equal(3, r.Days.Sum(d => d.Items.Count));
        // Les trois jours sont dans la semaine : l'à venir n'en répète aucun.
        Assert.Empty(r.Upcoming.Items);

        // Vus depuis une autre semaine, les trois jours ne donnent qu'une ligne d'à venir, datée du premier.
        var ailleurs = AgendaBuilder.Build(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), Today, AgendaView.Week, vacances);
        var seul = Assert.Single(ailleurs.Upcoming.Items);
        Assert.Equal("event:vacances@test:2026-09-09T22:00:00Z", seul.Id);
        Assert.Equal(new DateOnly(2026, 9, 10), seul.Date);
    }

    [Fact]
    public void JourneeEntiereQuiChevaucheLaFinDeLaFenetre_RenduesDansLesJours_AbsenteDeLAVenir()
    {
        // Vacances du 26 au 29 septembre, semaine du 21 au 27 : les 26 et 27 sont dans la semaine, l'événement
        // se poursuit à l'écran, il ne recommence pas en bas daté du 28.
        var vacances = new[] { 26, 27, 28, 29 }.Select(day => new AgendaItem
        {
            Id = "event:vacances@test:2026-09-25T22:00:00Z", Kind = AgendaKinds.Event, Date = new DateOnly(2026, 9, day),
            IsAllDay = true, Title = "Vacances", SeriesKey = "vacances@test",
        }).ToList();
        var r = AgendaBuilder.Build(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 27), Today, AgendaView.Week, vacances);

        Assert.Equal("Vacances", Assert.Single(Day(r, new DateOnly(2026, 9, 26)).Items).Title);
        Assert.Equal("Vacances", Assert.Single(Day(r, new DateOnly(2026, 9, 27)).Items).Title);
        Assert.Equal(2, r.Days.Sum(d => d.Items.Count));
        Assert.Empty(r.Upcoming.Items);
    }

    [Fact]
    public void AujourdhuiHorsFenetre_SansRetard_PasDeJourAjoute()
    {
        var r = AgendaBuilder.Build(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), Today, AgendaView.Month, Array.Empty<AgendaItem>());
        Assert.Empty(r.Days);
        var range = Assert.Single(r.EmptyRanges);
        Assert.Equal(new DateOnly(2026, 10, 1), range.From);
        Assert.Equal(new DateOnly(2026, 10, 31), range.To);
    }

    [Fact]
    public void OccurrenceAttendueEtAbsente_ProduitUnManquant_DansLaRoutine()
    {
        var r = AgendaBuilder.Build(WeekFrom, WeekTo, Today, AgendaView.Week, DanseAvecTrou());

        var jeudi = Day(r, new DateOnly(2026, 9, 10));
        Assert.Empty(jeudi.Items);
        var manquant = Assert.Single(jeudi.Routine);
        Assert.Equal("missing:danse:2026-09-10", manquant.Id);
        Assert.Equal("missing", manquant.Kind);
        Assert.Equal("Pas de Danse", manquant.Title);
        Assert.True(manquant.IsRoutine);
        Assert.Equal("16:45", manquant.Start);
        Assert.DoesNotContain(r.Upcoming.Items, i => i.Kind == "missing");
    }

    [Fact]
    public void Manquant_SeulementEntreLaPremiereEtLaDerniereOccurrenceConnue()
    {
        // Semaine du 17 au 23 septembre : le 17 a sa séance, le 24 est après la dernière connue, rien ne manque.
        var r = AgendaBuilder.Build(new DateOnly(2026, 9, 17), new DateOnly(2026, 9, 23), Today, AgendaView.Week, DanseAvecTrou());
        Assert.DoesNotContain(r.Days.SelectMany(d => d.Routine), i => i.Kind == "missing");

        var r2 = AgendaBuilder.Build(new DateOnly(2026, 9, 24), new DateOnly(2026, 9, 30), Today, AgendaView.Week, DanseAvecTrou());
        Assert.DoesNotContain(r2.Days.SelectMany(d => d.Routine), i => i.Kind == "missing");
    }

    [Fact]
    public void Manquant_ExigeTroisOccurrencesPassees()
    {
        var deuxSeulement = DanseAvecTrou().Where(i => i.Date != new DateOnly(2026, 8, 20)).ToList();
        var r = AgendaBuilder.Build(WeekFrom, WeekTo, Today, AgendaView.Week, deuxSeulement);
        Assert.Empty(Day(r, new DateOnly(2026, 9, 10)).Routine);
    }

    [Fact]
    public void Manquant_UneExceptionCeJourLa_CouvreLeJour()
    {
        var items = DanseAvecTrou();
        items.Add(Event("danse", new DateOnly(2026, 9, 10), start: "18:00", recurrence: CalendarRecurrence.Weekly, exception: true, title: "Danse (décalée)"));
        var r = AgendaBuilder.Build(WeekFrom, WeekTo, Today, AgendaView.Week, items);
        var jeudi = Day(r, new DateOnly(2026, 9, 10));
        Assert.Empty(jeudi.Routine);
        Assert.Equal("Danse (décalée)", Assert.Single(jeudi.Items).Title);
    }

    [Fact]
    public void VueMois_JoursVidesConsecutifsReplies_JamaisAujourdhui_JoursAvecItemsRendus()
    {
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);
        var items = new List<AgendaItem>
        {
            Event("dentiste", new DateOnly(2026, 9, 15), title: "Dentiste"),
            Echeance(1, new DateOnly(2026, 9, 3), AgendaStatuses.Paid, title: "Passée mais payée, on la montre"),
            Event("danse", new DateOnly(2026, 9, 24), recurrence: CalendarRecurrence.Weekly),
        };
        var r = AgendaBuilder.Build(from, to, Today, AgendaView.Month, items);

        Assert.Equal("month", r.View);
        Assert.Equal(new[] { 3, 9, 15, 24 }, r.Days.Select(d => d.Date.Day).ToArray());
        Assert.True(Day(r, Today).IsToday);
        Assert.Empty(Day(r, Today).Items);
        // Un jour avec seulement de la routine n'est pas vide.
        Assert.Empty(Day(r, new DateOnly(2026, 9, 24)).Items);
        Assert.Single(Day(r, new DateOnly(2026, 9, 24)).Routine);

        Assert.Equal(new[] { (1, 2), (4, 8), (10, 14), (16, 23), (25, 30) },
            r.EmptyRanges.Select(e => (e.From.Day, e.To.Day)).ToArray());
    }

    [Fact]
    public void TriDansUnJour_RetardsPuisJourneeEntierePuisHeurePuisEcheancesPuisRecurrentes()
    {
        var items = new List<AgendaItem>
        {
            Recurring(1, Today, title: "Prêt"),
            Echeance(1, Today, AgendaStatuses.Due, title: "Taxe"),
            Event("b", Today, start: "14:00", title: "Après-midi"),
            Event("a", Today, start: "08:30", title: "Matin"),
            Event("c", Today, allDay: true, title: "Journée entière"),
            Echeance(2, new DateOnly(2026, 9, 1), AgendaStatuses.Late, title: "Retard"),
        };
        var r = AgendaBuilder.Build(WeekFrom, WeekTo, Today, AgendaView.Week, items);
        var titres = Day(r, Today).Items.Select(i => i.Title).ToArray();
        Assert.Equal(new[] { "Retard", "Journée entière", "Matin", "Après-midi", "Taxe", "Prêt" }, titres);
    }

    [Fact]
    public void AVenir_TrenteJoursGlissantsDepuisAujourdhui_SansRoutineNiManquantNiRetard_IndependantDeLaVue()
    {
        var items = DanseAvecTrou();
        items.Add(Echeance(1, Today.AddDays(29), AgendaStatuses.Due, title: "Dans la fenêtre"));
        items.Add(Echeance(2, Today.AddDays(30), AgendaStatuses.Due, title: "Juste après"));
        items.Add(Echeance(3, new DateOnly(2026, 8, 12), AgendaStatuses.Late, title: "Retard porté"));
        items.Add(Recurring(1, Today.AddDays(10)));
        items.Add(Event("dentiste", Today.AddDays(3), title: "Dentiste"));

        var r = AgendaBuilder.Build(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), Today, AgendaView.Month, items);

        Assert.Equal(Today, r.Upcoming.From);
        Assert.Equal(Today.AddDays(29), r.Upcoming.To);
        var ids = r.Upcoming.Items.Select(i => i.Id).ToList();
        Assert.Equal(new[] { "event:dentiste:2026-09-12T00:00:00Z", $"recurring:1:{Today.AddDays(10):yyyy-MM-dd}", "echeance:1" }, ids);
        Assert.DoesNotContain(r.Upcoming.Items, i => i.IsRoutine || i.Kind == "missing" || i.Id == "echeance:2" || i.Status == AgendaStatuses.Late);

        // Le retard n'est pas dans l'à venir : il est porté dans le jour Aujourd'hui ajouté en tête, avec sa date d'origine.
        var aujourdhui = r.Days[0];
        Assert.True(aujourdhui.IsToday);
        var porte = Assert.Single(aujourdhui.Items, i => i.Id == "echeance:3");
        Assert.Equal(new DateOnly(2026, 8, 12), porte.OriginalDate);
        Assert.Equal(Today, porte.Date);
    }

    [Fact]
    public void AVenir_SemaineCourante_RienDeLaSemaine_LesJoursSuivantsRestent_RetardPorteAbsent()
    {
        // Semaine du 9 au 15, aujourd'hui le 9. Ce que la semaine montre déjà ne revient pas en bas.
        var items = new List<AgendaItem>
        {
            Echeance(1, new DateOnly(2026, 8, 12), AgendaStatuses.Late, title: "Retard porté dans Aujourd'hui"),
            Echeance(2, Today, AgendaStatuses.Due, title: "Du jour"),
            Echeance(3, new DateOnly(2026, 9, 12), AgendaStatuses.Due, title: "Dans la semaine"),
            Event("dentiste", new DateOnly(2026, 9, 15), title: "Dernier jour de la semaine"),
            Recurring(1, new DateOnly(2026, 9, 16), title: "J+7, premier jour hors semaine"),
            Echeance(4, Today.AddDays(29), AgendaStatuses.Due, title: "J+29"),
            Echeance(5, Today.AddDays(30), AgendaStatuses.Due, title: "J+30, hors fenêtre"),
        };
        var r = AgendaBuilder.Build(WeekFrom, WeekTo, Today, AgendaView.Week, items);

        Assert.Equal(new[] { "recurring:1:2026-09-16", "echeance:4" }, r.Upcoming.Items.Select(i => i.Id).ToArray());
        Assert.Equal(Today, r.Upcoming.From);
        Assert.Equal(Today.AddDays(29), r.Upcoming.To);
        // Le retard porté est dans Aujourd'hui, une seule fois, et nulle part ailleurs.
        Assert.Equal("echeance:1", Day(r, Today).Items[0].Id);
        Assert.DoesNotContain(r.Upcoming.Items, i => i.Id == "echeance:1");
    }

    [Fact]
    public void AVenir_MoisSuivant_LesItemsDuMoisAbsents_CeuxDIciLaFinDuMoisPresents_RetardPorteAbsent()
    {
        // On regarde octobre le 9 septembre : Aujourd'hui n'est pas dans la fenêtre, il est ajouté en tête de
        // days avec le retard porté. L'à venir garde ce qui reste de septembre, perd ce qu'octobre affiche déjà,
        // et ne porte jamais un retard : Aujourd'hui est le seul endroit où un impayé apparaît.
        var from = new DateOnly(2026, 10, 1);
        var to = new DateOnly(2026, 10, 31);
        var items = new List<AgendaItem>
        {
            Echeance(1, new DateOnly(2026, 9, 2), AgendaStatuses.Late, title: "Retard porté"),
            Echeance(2, Today, AgendaStatuses.Due, title: "Du jour"),
            Echeance(3, new DateOnly(2026, 9, 27), AgendaStatuses.Due, title: "Fin septembre"),
            Recurring(1, new DateOnly(2026, 10, 1), title: "Loyer, dans le mois affiché"),
            Echeance(4, new DateOnly(2026, 10, 8), AgendaStatuses.Due, title: "J+29, dans le mois affiché"),
        };
        var r = AgendaBuilder.Build(from, to, Today, AgendaView.Month, items);

        Assert.Equal(new[] { "echeance:2", "echeance:3" }, r.Upcoming.Items.Select(i => i.Id).ToArray());
        Assert.Equal(Today, r.Upcoming.From);
        Assert.Equal(Today.AddDays(29), r.Upcoming.To);

        var aujourdhui = r.Days[0];
        Assert.True(aujourdhui.IsToday);
        var porte = Assert.Single(aujourdhui.Items);
        Assert.Equal("echeance:1", porte.Id);
        Assert.Equal(new DateOnly(2026, 9, 2), porte.OriginalDate);
        Assert.Equal(Today, porte.Date);
    }

    [Fact]
    public void AVenir_MoisCourant_SeulsLesItemsDuMoisSuivantJusquAJPlus29Restent()
    {
        var from = new DateOnly(2026, 9, 1);
        var to = new DateOnly(2026, 9, 30);
        var items = new List<AgendaItem>
        {
            Echeance(1, new DateOnly(2026, 9, 2), AgendaStatuses.Late, title: "Retard porté, déjà dans Aujourd'hui"),
            Echeance(2, new DateOnly(2026, 9, 27), AgendaStatuses.Due, title: "Dans le mois"),
            Recurring(1, new DateOnly(2026, 10, 1), title: "Loyer"),
            Echeance(3, new DateOnly(2026, 10, 8), AgendaStatuses.Due, title: "J+29"),
            Echeance(4, new DateOnly(2026, 10, 9), AgendaStatuses.Due, title: "J+30, hors fenêtre"),
        };
        var r = AgendaBuilder.Build(from, to, Today, AgendaView.Month, items);

        Assert.Equal(new[] { "recurring:1:2026-10-01", "echeance:3" }, r.Upcoming.Items.Select(i => i.Id).ToArray());
        Assert.Equal(new DateOnly(2026, 10, 8), r.Upcoming.To);
    }

    [Fact]
    public void PasDeDoubleComptage_UneRecurrenteEtUneEcheance_MemeJourMemeMontant_DeuxItemsDistincts()
    {
        var jour = new DateOnly(2026, 9, 11);
        var items = new List<AgendaItem>
        {
            Recurring(7, jour, 1250m, "Prêt hypothécaire"),
            Echeance(12, jour, AgendaStatuses.Due, 1250m, "Prêt hypothécaire"),
        };
        var r = AgendaBuilder.Build(WeekFrom, WeekTo, Today, AgendaView.Week, items);
        var day = Day(r, jour);

        Assert.Equal(2, day.Items.Count);
        Assert.Equal(new[] { "echeance:12", "recurring:7:2026-09-11" }, day.Items.Select(i => i.Id).ToArray());
        Assert.Equal(new[] { "echeance", "recurring" }, day.Items.Select(i => i.Kind).ToArray());
        Assert.All(day.Items, i => Assert.Equal(1250m, i.Amount));
        // Tous deux sont dans la semaine affichée : l'à venir ne les répète pas.
        Assert.Empty(r.Upcoming.Items);
    }

    [Fact]
    public void LesItemsFournis_NeSontPasModifies()
    {
        var late = Echeance(1, new DateOnly(2026, 9, 2), AgendaStatuses.Late);
        var routine = Event("danse", new DateOnly(2026, 9, 10), recurrence: CalendarRecurrence.Weekly);
        AgendaBuilder.Build(WeekFrom, WeekTo, Today, AgendaView.Week, new[] { late, routine });
        Assert.Equal(new DateOnly(2026, 9, 2), late.Date);
        Assert.Null(late.OriginalDate);
        Assert.False(routine.IsRoutine);
    }

    [Fact]
    public void FenetreInversee_EstRefusee()
    {
        Assert.Throws<ArgumentException>(() => AgendaBuilder.Build(WeekTo, WeekFrom, Today, AgendaView.Week, Array.Empty<AgendaItem>()));
    }
}
