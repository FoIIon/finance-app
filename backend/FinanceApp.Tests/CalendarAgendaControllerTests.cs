using System.Text.Json;
using FinanceApp.API.Controllers;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Calendar;
using FinanceApp.API.Services.Reporting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Les deux contrôleurs du lot, appelés tels quels sur une base en mémoire. Deux ménages : A ne voit rien
/// de B (404, jamais 403). L'adresse ICS ne sort d'aucune réponse, une adresse refusée rend 400 sans
/// être répétée, et l'agenda rend le contrat attendu.
/// </summary>
public class CalendarAgendaControllerTests : IDisposable
{
    private const string SecretPath = "/calendar/ical/famille%40gmail.com/private-a1b2c3SECRET/basic.ics";
    private const string SecretUrl = "https://calendar.google.com" + SecretPath;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly CalendarSyncService _sync;
    private readonly IDataProtectionProvider _protection;
    private readonly FakeIcsFetcher _fetcher;
    private readonly FixedTimeProvider _clock;
    private Household _a = null!;
    private Household _b = null!;

    public CalendarAgendaControllerTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
        (_sync, _protection, _fetcher, _clock) = AgendaTestSupport.SyncService(_connection);
        _fetcher.On(SecretPath, AgendaTestSupport.FamilleIcs());
        using var ctx = NewContext();
        _a = TestHousehold.SeedAsync(ctx, "a@test.local").GetAwaiter().GetResult();
        _b = TestHousehold.SeedAsync(ctx, "b@test.local").GetAwaiter().GetResult();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() => new(_options);

    private CalendarController Calendar(AppDbContext ctx, int userId) =>
        new(ctx, _protection, _sync, Options.Create(AgendaTestSupport.Options()), _clock) { ControllerContext = TestHousehold.As(userId) };

    private AgendaController Agenda(AppDbContext ctx, int userId)
    {
        var household = Options.Create(AgendaTestSupport.Household());
        return new AgendaController(ctx, household, _clock, new RecurringLinkService(ctx, household)) { ControllerContext = TestHousehold.As(userId) };
    }

    private static T Ok<T>(ActionResult<T> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<T>(ok.Value);
    }

    private static string Serialized(object value) => JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static void AssertNoSecret(string json)
    {
        Assert.DoesNotContain("SECRET", json);
        Assert.DoesNotContain("calendar.google.com", json);
        Assert.DoesNotContain("basic.ics", json);
        Assert.DoesNotContain("EncryptedUrl", json);
        Assert.DoesNotContain("encryptedUrl", json);
    }

    [Fact]
    public async Task SansSource_GetRendDeconnecte()
    {
        using var ctx = NewContext();
        var status = Ok(await Calendar(ctx, _a.UserId).GetSource(_a.DashboardId));
        Assert.False(status.Connected);
        Assert.Null(status.CalendarName);
        Assert.Null(status.LastSyncStatus);
    }

    [Fact]
    public async Task PutAdresseRefusee_400_SansLaRepeter_RienEnBase()
    {
        using var ctx = NewContext();
        var result = await Calendar(ctx, _a.UserId).PutSource(_a.DashboardId, new CalendarSourceUrlDto { Url = "http://calendar.google.com/private-SECRET/basic.ics" }, CancellationToken.None);
        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var message = Assert.IsType<string>(bad.Value);
        Assert.DoesNotContain("SECRET", message);
        Assert.DoesNotContain("http://", message);
        Assert.Equal(0, await ctx.CalendarSources.CountAsync());
        Assert.Empty(_fetcher.Calls);
    }

    [Fact]
    public async Task PutAdresseValide_ChiffreSynchroniseEtRendLeStatut_SansLAdresse()
    {
        using var ctx = NewContext();
        var status = Ok(await Calendar(ctx, _a.UserId).PutSource(_a.DashboardId, new CalendarSourceUrlDto { Url = SecretUrl }, CancellationToken.None));

        Assert.True(status.Connected);
        Assert.Equal("Famille", status.CalendarName);
        Assert.Equal("Ok", status.LastSyncStatus);
        Assert.Null(status.LastError);
        Assert.Equal(AgendaTestSupport.Now.UtcDateTime, status.LastSyncAt);
        Assert.Equal(AgendaTestSupport.Now.UtcDateTime, status.LastAttemptAt);
        Assert.Equal(DateTimeKind.Utc, status.LastSyncAt!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, status.LastAttemptAt!.Value.Kind);
        AssertNoSecret(Serialized(status));
        Assert.Contains("\"lastAttemptAt\":\"2026-09-09T06:00:00Z\"", Serialized(status));

        using var check = NewContext();
        var source = await check.CalendarSources.SingleAsync();
        Assert.NotEqual(SecretUrl, source.EncryptedUrl);
        Assert.DoesNotContain("SECRET", source.EncryptedUrl);
        Assert.Equal(SecretUrl, _protection.CreateProtector(CalendarSyncService.ProtectorPurpose).Unprotect(source.EncryptedUrl));
        Assert.Equal(35,await check.CalendarOccurrences.CountAsync(o => o.DashboardId == _a.DashboardId));
        Assert.Single(_fetcher.Calls);
    }

    [Fact]
    public async Task PutUneDeuxiemeFois_RemplaceLaSource_SansDoublon()
    {
        using var ctx = NewContext();
        Ok(await Calendar(ctx, _a.UserId).PutSource(_a.DashboardId, new CalendarSourceUrlDto { Url = SecretUrl }, CancellationToken.None));
        _fetcher.On("/calendar/ical/autre/basic.ics", AgendaTestSupport.FamilleIcs());
        Ok(await Calendar(ctx, _a.UserId).PutSource(_a.DashboardId, new CalendarSourceUrlDto { Url = "https://calendar.google.com/calendar/ical/autre/basic.ics" }, CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(1, await check.CalendarSources.CountAsync());
        Assert.Equal(35,await check.CalendarOccurrences.CountAsync());
        Assert.Equal(new[] { SecretPath, "/calendar/ical/autre/basic.ics" }, _fetcher.Calls);
    }

    [Fact]
    public async Task Delete_EffaceSourceEtOccurrences_204_MemeSansSource()
    {
        using (var ctx = NewContext())
        {
            Ok(await Calendar(ctx, _a.UserId).PutSource(_a.DashboardId, new CalendarSourceUrlDto { Url = SecretUrl }, CancellationToken.None));
            Assert.IsType<NoContentResult>(await Calendar(ctx, _a.UserId).DeleteSource(_a.DashboardId, CancellationToken.None));
        }
        using var check = NewContext();
        Assert.Equal(0, await check.CalendarSources.CountAsync());
        Assert.Equal(0, await check.CalendarOccurrences.CountAsync());
        Assert.IsType<NoContentResult>(await Calendar(check, _a.UserId).DeleteSource(_a.DashboardId, CancellationToken.None));
    }

    [Fact]
    public async Task Refresh_SansSource404_AvecSourceResynchronise()
    {
        using var ctx = NewContext();
        Assert.IsType<NotFoundResult>((await Calendar(ctx, _a.UserId).Refresh(_a.DashboardId, CancellationToken.None)).Result);

        Ok(await Calendar(ctx, _a.UserId).PutSource(_a.DashboardId, new CalendarSourceUrlDto { Url = SecretUrl }, CancellationToken.None));
        _clock.Now = AgendaTestSupport.Now.AddHours(1);
        var status = Ok(await Calendar(ctx, _a.UserId).Refresh(_a.DashboardId, CancellationToken.None));
        Assert.Equal(AgendaTestSupport.Now.AddHours(1).UtcDateTime, status.LastSyncAt);
        Assert.Equal(AgendaTestSupport.Now.AddHours(1).UtcDateTime, status.LastAttemptAt);
        Assert.Equal(2, _fetcher.Calls.Count);

        // Un rafraîchissement en échec : la tentative avance, le succès reste, et l'agenda expose les deux.
        _fetcher.On(SecretPath, () => IcsFetchResult.Fail(CalendarSyncStatus.HttpError, "HTTP 503."));
        _clock.Now = AgendaTestSupport.Now.AddHours(2);
        var echec = Ok(await Calendar(ctx, _a.UserId).Refresh(_a.DashboardId, CancellationToken.None));
        Assert.Equal("HttpError", echec.LastSyncStatus);
        Assert.Equal(AgendaTestSupport.Now.AddHours(1).UtcDateTime, echec.LastSyncAt);
        Assert.Equal(AgendaTestSupport.Now.AddHours(2).UtcDateTime, echec.LastAttemptAt);
        var agenda = Ok(await Agenda(ctx, _a.UserId).Get(_a.DashboardId, null, null, CancellationToken.None));
        Assert.Equal(AgendaTestSupport.Now.AddHours(1).UtcDateTime, agenda.Calendar.LastSyncAt);
        Assert.Equal(AgendaTestSupport.Now.AddHours(2).UtcDateTime, agenda.Calendar.LastAttemptAt);
    }

    [Fact]
    public async Task HorsPerimetre_ToutRend404()
    {
        using var ctx = NewContext();
        Ok(await Calendar(ctx, _b.UserId).PutSource(_b.DashboardId, new CalendarSourceUrlDto { Url = SecretUrl }, CancellationToken.None));

        var a = Calendar(ctx, _a.UserId);
        Assert.IsType<NotFoundResult>((await a.GetSource(_b.DashboardId)).Result);
        Assert.IsType<NotFoundResult>((await a.PutSource(_b.DashboardId, new CalendarSourceUrlDto { Url = SecretUrl }, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(await a.DeleteSource(_b.DashboardId, CancellationToken.None));
        Assert.IsType<NotFoundResult>((await a.Refresh(_b.DashboardId, CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await Agenda(ctx, _a.UserId).Get(_b.DashboardId, null, null, CancellationToken.None)).Result);

        // B a toujours sa source et ses occurrences.
        Assert.Equal(1, await ctx.CalendarSources.CountAsync(s => s.DashboardId == _b.DashboardId));
        Assert.Equal(35,await ctx.CalendarOccurrences.CountAsync(o => o.DashboardId == _b.DashboardId));
    }

    [Fact]
    public async Task Agenda_VueInconnue_400()
    {
        using var ctx = NewContext();
        Assert.IsType<BadRequestObjectResult>((await Agenda(ctx, _a.UserId).Get(_a.DashboardId, "year", null, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Agenda_SemaineParDefaut_FondCalendrierEcheancesEtRecurrentes_AuFuseauDuMenage()
    {
        using var ctx = NewContext();
        Ok(await Calendar(ctx, _a.UserId).PutSource(_a.DashboardId, new CalendarSourceUrlDto { Url = SecretUrl }, CancellationToken.None));
        ctx.Echeances.AddRange(
            new Echeance { DashboardId = _a.DashboardId, Label = "Taxe déchets", DueDate = new DateOnly(2026, 9, 2), Amount = 95m, CreatedByUserId = _a.UserId },
            new Echeance { DashboardId = _a.DashboardId, Label = "Assurance auto", DueDate = new DateOnly(2026, 9, 12), Amount = 612.33m, CreatedByUserId = _a.UserId },
            new Echeance { DashboardId = _a.DashboardId, Label = "Dans un mois", DueDate = new DateOnly(2026, 10, 8), Amount = 10m, CreatedByUserId = _a.UserId },
            new Echeance { DashboardId = _a.DashboardId, Label = "Trop loin", DueDate = new DateOnly(2026, 10, 9), Amount = 10m, CreatedByUserId = _a.UserId });
        ctx.RecurringTransactions.Add(new RecurringTransaction
        {
            UserId = _a.UserId, DashboardId = _a.DashboardId, Description = "Prêt hypothécaire", Amount = 1250m, Type = TransactionType.Expense,
            Frequency = RecurringFrequency.Monthly, DayOfMonth = 11, StartDate = new DateOnly(2025, 1, 11), IsActive = true,
        });
        await ctx.SaveChangesAsync();

        // 23h30 UTC le 8 septembre : déjà le 9 à Bruxelles.
        _clock.Now = new DateTimeOffset(2026, 9, 8, 23, 30, 0, TimeSpan.Zero);
        var result = Ok(await Agenda(ctx, _a.UserId).Get(_a.DashboardId, null, null, CancellationToken.None));

        Assert.Equal("week", result.View);
        Assert.Equal(new DateOnly(2026, 9, 9), result.Today);
        Assert.Equal(new DateOnly(2026, 9, 9), result.From);
        Assert.Equal(new DateOnly(2026, 9, 15), result.To);
        Assert.Equal("Europe/Brussels", result.TimeZone);
        Assert.True(result.Calendar.Connected);
        Assert.Equal("Famille", result.Calendar.CalendarName);
        Assert.Equal(7, result.Days.Count);
        Assert.Empty(result.EmptyRanges);

        var today = result.Days[0];
        Assert.True(today.IsToday);
        var retard = Assert.Single(today.Items);
        Assert.Equal("late", retard.Status);
        Assert.Equal(new DateOnly(2026, 9, 2), retard.OriginalDate);
        Assert.Equal("Taxe déchets", retard.Title);

        var jeudi = result.Days[1];
        Assert.Equal(new DateOnly(2026, 9, 10), jeudi.Date);
        Assert.Empty(jeudi.Items);
        var danse = Assert.Single(jeudi.Routine);
        Assert.Equal("Danse Alice", danse.Title);
        Assert.Equal("16:45", danse.Start);
        Assert.True(danse.IsRoutine);

        var vendredi = result.Days[2];
        var pret = Assert.Single(vendredi.Items);
        Assert.Equal("recurring", pret.Kind);
        Assert.Equal("planned", pret.Status);
        Assert.Equal(1250m, pret.Amount);

        var samedi = result.Days[3];
        var assurance = Assert.Single(samedi.Items);
        Assert.Equal("echeance", assurance.Kind);
        Assert.Equal("due", assurance.Status);

        var mardi = result.Days[6];
        var reunion = Assert.Single(mardi.Items);
        Assert.Equal("Réunion parents d'élèves", reunion.Title);
        Assert.Equal("19:30", reunion.Start);
        Assert.Equal("École du village", reunion.Location);

        Assert.Equal(new DateOnly(2026, 9, 9), result.Upcoming.From);
        Assert.Equal(new DateOnly(2026, 10, 8), result.Upcoming.To);
        Assert.Contains(result.Upcoming.Items, i => i.Title == "Dans un mois");
        Assert.DoesNotContain(result.Upcoming.Items, i => i.Title == "Trop loin");
        Assert.DoesNotContain(result.Upcoming.Items, i => i.IsRoutine);

        var json = Serialized(result);
        AssertNoSecret(json);
        Assert.DoesNotContain("SECRETDESCRIPTION", json);
        Assert.DoesNotContain("seriesKey", json);
        Assert.Contains("\"isAllDay\"", json);
        Assert.Contains("\"originalDate\":\"2026-09-02\"", json);
        Assert.Contains("\"start\":\"16:45\"", json);
    }

    [Fact]
    public async Task Agenda_VueMois_AvecAncre_RepliLesJoursVides()
    {
        using var ctx = NewContext();
        Ok(await Calendar(ctx, _a.UserId).PutSource(_a.DashboardId, new CalendarSourceUrlDto { Url = SecretUrl }, CancellationToken.None));

        // Synchronisé le 9 septembre, consulté le 15 octobre : la série de danse a cinq jeudis passés.
        _clock.Now = new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
        var result = Ok(await Agenda(ctx, _a.UserId).Get(_a.DashboardId, "month", null, CancellationToken.None));

        Assert.Equal("month", result.View);
        Assert.Equal(new DateOnly(2026, 10, 1), result.From);
        Assert.Equal(new DateOnly(2026, 10, 31), result.To);
        Assert.Equal(new DateOnly(2026, 10, 15), result.Today);
        // Le 3 octobre porte la séance déplacée (exception, donc hors routine), le 10 le week-end et la piscine.
        var le3 = Assert.Single(result.Days, d => d.Date == new DateOnly(2026, 10, 3));
        Assert.Equal("Danse Alice (déplacée)", Assert.Single(le3.Items).Title);
        var le10 = Assert.Single(result.Days, d => d.Date == new DateOnly(2026, 10, 10));
        Assert.Equal("Week-end chez les cousins", Assert.Single(le10.Items).Title);
        Assert.Equal("Piscine Hugo", Assert.Single(le10.Routine).Title);
        // Le 1er octobre : la série y attendait une séance, elle a été déplacée, l'agenda le dit.
        var le1 = Assert.Single(result.Days, d => d.Date == new DateOnly(2026, 10, 1));
        Assert.Empty(le1.Items);
        Assert.Equal("Pas de Danse Alice", Assert.Single(le1.Routine).Title);
        // Les jeudis d'octobre autour du changement d'heure gardent 16:45.
        Assert.Equal("16:45", Assert.Single(Assert.Single(result.Days, d => d.Date == new DateOnly(2026, 10, 22)).Routine).Start);
        Assert.Equal("16:45", Assert.Single(Assert.Single(result.Days, d => d.Date == new DateOnly(2026, 10, 29)).Routine).Start);
        // Aujourd'hui est rendu même vide, et n'est dans aucune plage repliée.
        var today = Assert.Single(result.Days, d => d.IsToday);
        Assert.Equal(new DateOnly(2026, 10, 15), today.Date);
        Assert.Empty(today.Items);
        Assert.NotEmpty(result.EmptyRanges);
        Assert.DoesNotContain(result.EmptyRanges, e => e.From <= today.Date && today.Date <= e.To);
        Assert.Contains(result.EmptyRanges, e => e.From == new DateOnly(2026, 10, 4) && e.To == new DateOnly(2026, 10, 7));
    }
}
