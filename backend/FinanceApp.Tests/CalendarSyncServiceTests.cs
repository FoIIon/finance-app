using System.Text.Json;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Calendar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le service de fond sur une base SQLite en mémoire au vrai schéma, avec un téléchargeur simulé et une
/// protection de données éphémère. Une source malade n'empêche pas les autres, une clé perdue pose
/// KeyLost sans réessai, les occurrences ne sont remplacées qu'après un 200 et un parse valide, et rien
/// de ce qui est persisté ne contient l'adresse.
/// </summary>
public class CalendarSyncServiceTests : IDisposable
{
    private const string SecretPath = "/calendar/ical/famille%40gmail.com/private-a1b2c3SECRET/basic.ics";
    private const string SecretUrl = "https://calendar.google.com" + SecretPath;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public CalendarSyncServiceTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() => new(_options);

    private async Task<Household> SeedHouseholdAsync(string email)
    {
        using var ctx = NewContext();
        return await TestHousehold.SeedAsync(ctx, email);
    }

    private async Task<CalendarSource> SourceAsync(int dashboardId)
    {
        using var ctx = NewContext();
        return await ctx.CalendarSources.AsNoTracking().SingleAsync(s => s.DashboardId == dashboardId);
    }

    private async Task<int> OccurrencesAsync(int dashboardId)
    {
        using var ctx = NewContext();
        return await ctx.CalendarOccurrences.CountAsync(o => o.DashboardId == dashboardId);
    }

    private static void AssertNoSecret(CalendarSource source)
    {
        var json = JsonSerializer.Serialize(new { source.CalendarName, source.LastError, source.LastSyncStatus });
        Assert.DoesNotContain("SECRET", json);
        Assert.DoesNotContain("calendar.google.com", json);
        Assert.DoesNotContain("basic.ics", json);
    }

    [Fact]
    public async Task SynchronisationReussie_RemplaceLesOccurrences_PoseOkEtLeNom()
    {
        var h = await SeedHouseholdAsync("sync@test.local");
        var (sync, protection, fetcher, clock) = AgendaTestSupport.SyncService(_connection);
        fetcher.On(SecretPath, AgendaTestSupport.FamilleIcs());
        using (var ctx = NewContext())
            await AgendaTestSupport.AddSourceAsync(ctx, h.DashboardId, AgendaTestSupport.Protect(protection, SecretUrl));

        await sync.SyncAllAsync(CancellationToken.None);

        var source = await SourceAsync(h.DashboardId);
        Assert.Equal(CalendarSyncStatus.Ok, source.LastSyncStatus);
        Assert.Equal("Famille", source.CalendarName);
        Assert.Null(source.LastError);
        Assert.Equal(clock.Now.UtcDateTime, source.LastSyncAt);
        AssertNoSecret(source);

        // La fenêtre part du 9 juin 2026 (aujourd'hui moins trois mois) : le feu d'artifice du 14 juillet
        // y est, l'anniversaire d'août aussi, et tout est rattaché au dashboard.
        using var check = NewContext();
        var occurrences = await check.CalendarOccurrences.Where(o => o.DashboardId == h.DashboardId).ToListAsync();
        Assert.Equal(27, occurrences.Count);
        Assert.Contains(occurrences, o => o.Uid == "feu-artifice-0004@test.invalid");
        Assert.DoesNotContain(JsonSerializer.Serialize(occurrences.Select(o => new { o.Summary, o.Location, o.Uid })), "SECRETDESCRIPTION");
    }

    [Fact]
    public async Task DeuxiemeSynchronisation_RemplaceEnBloc_PasDAccumulation()
    {
        var h = await SeedHouseholdAsync("replace@test.local");
        var (sync, protection, fetcher, _) = AgendaTestSupport.SyncService(_connection);
        var reponse = AgendaTestSupport.FamilleIcs();
        fetcher.On(SecretPath, () => IcsFetchResult.Ok(reponse));
        using (var ctx = NewContext())
            await AgendaTestSupport.AddSourceAsync(ctx, h.DashboardId, AgendaTestSupport.Protect(protection, SecretUrl));

        await sync.SyncDashboardAsync(h.DashboardId, CancellationToken.None);
        Assert.Equal(27, await OccurrencesAsync(h.DashboardId));

        // Le même flux une deuxième fois : même compte, pas 54.
        await sync.SyncDashboardAsync(h.DashboardId, CancellationToken.None);
        Assert.Equal(27, await OccurrencesAsync(h.DashboardId));

        // Un flux réduit à un événement : tout le reste disparaît.
        reponse = """
            BEGIN:VCALENDAR
            VERSION:2.0
            PRODID:-//Test//FR
            X-WR-CALNAME:Réduit
            BEGIN:VEVENT
            UID:seul@test.invalid
            DTSTART;TZID=Europe/Brussels:20260920T100000
            DTEND;TZID=Europe/Brussels:20260920T110000
            SUMMARY:Seul
            END:VEVENT
            END:VCALENDAR
            """;
        await sync.SyncDashboardAsync(h.DashboardId, CancellationToken.None);
        Assert.Equal(1, await OccurrencesAsync(h.DashboardId));
        Assert.Equal("Réduit", (await SourceAsync(h.DashboardId)).CalendarName);
    }

    [Fact]
    public async Task UneSourceQuiLeve_LaisseLesAutresSeSynchroniser()
    {
        // B est créé avant A : son dashboard a le plus petit identifiant et passe en premier.
        var b = await SeedHouseholdAsync("b@test.local");
        var a = await SeedHouseholdAsync("a@test.local");
        var (sync, protection, fetcher, _) = AgendaTestSupport.SyncService(_connection);
        fetcher.On(SecretPath, AgendaTestSupport.FamilleIcs());
        fetcher.On("/calendar/ical/b/basic.ics", () => throw new InvalidOperationException("panne simulée"));
        using (var ctx = NewContext())
        {
            await AgendaTestSupport.AddSourceAsync(ctx, b.DashboardId, AgendaTestSupport.Protect(protection, "https://calendar.google.com/calendar/ical/b/basic.ics"));
            await AgendaTestSupport.AddSourceAsync(ctx, a.DashboardId, AgendaTestSupport.Protect(protection, SecretUrl));
        }

        await sync.SyncAllAsync(CancellationToken.None);

        Assert.Equal(2, fetcher.Calls.Count);
        Assert.Equal(CalendarSyncStatus.Ok, (await SourceAsync(a.DashboardId)).LastSyncStatus);
        Assert.Equal(27, await OccurrencesAsync(a.DashboardId));
        // La source malade garde son état : rien n'a été écrit dans le doute.
        Assert.Equal(CalendarSyncStatus.Pending, (await SourceAsync(b.DashboardId)).LastSyncStatus);
        Assert.Equal(0, await OccurrencesAsync(b.DashboardId));
    }

    [Fact]
    public async Task EchecHttp_PoseHttpError_SansToucherAuxOccurrences()
    {
        var h = await SeedHouseholdAsync("http@test.local");
        var (sync, protection, fetcher, _) = AgendaTestSupport.SyncService(_connection);
        var reponse = IcsFetchResult.Ok(AgendaTestSupport.FamilleIcs());
        fetcher.On(SecretPath, () => reponse);
        using (var ctx = NewContext())
            await AgendaTestSupport.AddSourceAsync(ctx, h.DashboardId, AgendaTestSupport.Protect(protection, SecretUrl));

        await sync.SyncDashboardAsync(h.DashboardId, CancellationToken.None);
        Assert.Equal(27, await OccurrencesAsync(h.DashboardId));

        reponse = IcsFetchResult.Fail(CalendarSyncStatus.HttpError, "HTTP 404.");
        await sync.SyncDashboardAsync(h.DashboardId, CancellationToken.None);
        var source = await SourceAsync(h.DashboardId);
        Assert.Equal(CalendarSyncStatus.HttpError, source.LastSyncStatus);
        Assert.Equal("HTTP 404.", source.LastError);
        Assert.Equal("Famille", source.CalendarName);
        Assert.Equal(27, await OccurrencesAsync(h.DashboardId));
        AssertNoSecret(source);

        reponse = IcsFetchResult.Fail(CalendarSyncStatus.Invalid, "Le contenu n'est pas un flux iCalendar.");
        await sync.SyncDashboardAsync(h.DashboardId, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.Invalid, (await SourceAsync(h.DashboardId)).LastSyncStatus);
        Assert.Equal(27, await OccurrencesAsync(h.DashboardId));
    }

    [Fact]
    public async Task FluxIllisible_PoseInvalid_SansToucherAuxOccurrences()
    {
        var h = await SeedHouseholdAsync("invalid@test.local");
        var (sync, protection, fetcher, _) = AgendaTestSupport.SyncService(_connection);
        // Le téléchargeur a laissé passer un « BEGIN:VCALENDAR » suivi d'un composant jamais fermé
        // (flux coupé) : Ical.Net lève une SerializationException au chargement.
        fetcher.On(SecretPath, "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:x\r\nDTSTART:20260901T100000\r\n");
        using (var ctx = NewContext())
            await AgendaTestSupport.AddSourceAsync(ctx, h.DashboardId, AgendaTestSupport.Protect(protection, SecretUrl));

        await sync.SyncDashboardAsync(h.DashboardId, CancellationToken.None);

        var source = await SourceAsync(h.DashboardId);
        Assert.Equal(CalendarSyncStatus.Invalid, source.LastSyncStatus);
        Assert.StartsWith("Flux iCalendar illisible", source.LastError);
        Assert.Equal(0, await OccurrencesAsync(h.DashboardId));
        AssertNoSecret(source);
    }

    [Fact]
    public async Task ClePerdue_PoseKeyLost_EtNEstPlusReessayee()
    {
        var h = await SeedHouseholdAsync("keylost@test.local");
        var (sync, _, fetcher, _) = AgendaTestSupport.SyncService(_connection);
        fetcher.On(SecretPath, AgendaTestSupport.FamilleIcs());
        // Chiffré par une autre clé : Unprotect lève CryptographicException.
        var autreCle = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider();
        using (var ctx = NewContext())
            await AgendaTestSupport.AddSourceAsync(ctx, h.DashboardId, AgendaTestSupport.Protect(autreCle, SecretUrl));

        await sync.SyncAllAsync(CancellationToken.None);
        var source = await SourceAsync(h.DashboardId);
        Assert.Equal(CalendarSyncStatus.KeyLost, source.LastSyncStatus);
        Assert.Contains("ressaisir", source.LastError);
        Assert.Empty(fetcher.Calls);
        AssertNoSecret(source);

        await sync.SyncAllAsync(CancellationToken.None);
        Assert.Empty(fetcher.Calls);

        // Un rafraîchissement explicite retente quand même, et retombe en KeyLost.
        await sync.SyncDashboardAsync(h.DashboardId, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.KeyLost, (await SourceAsync(h.DashboardId)).LastSyncStatus);
        Assert.Empty(fetcher.Calls);
    }

    [Fact]
    public async Task AdresseDevenueInterdite_PoseInvalid_SansLaRepeter()
    {
        var h = await SeedHouseholdAsync("policy@test.local");
        var (sync, protection, fetcher, _) = AgendaTestSupport.SyncService(_connection);
        using (var ctx = NewContext())
            await AgendaTestSupport.AddSourceAsync(ctx, h.DashboardId, AgendaTestSupport.Protect(protection, "https://autre.example/private-SECRET/basic.ics"));

        await sync.SyncDashboardAsync(h.DashboardId, CancellationToken.None);

        var source = await SourceAsync(h.DashboardId);
        Assert.Equal(CalendarSyncStatus.Invalid, source.LastSyncStatus);
        Assert.StartsWith("Adresse refusée", source.LastError);
        Assert.DoesNotContain("autre.example", source.LastError);
        Assert.DoesNotContain("SECRET", source.LastError);
        Assert.Empty(fetcher.Calls);
    }

    [Fact]
    public async Task AvertissementDeTroncature_FinitDansLastError_Borne()
    {
        var h = await SeedHouseholdAsync("trunc@test.local");
        var (sync, protection, fetcher, _) = AgendaTestSupport.SyncService(_connection, options: AgendaTestSupport.Options(o => o.MaxOccurrencesPerEvent = 3));
        fetcher.On(SecretPath, AgendaTestSupport.FamilleIcs());
        using (var ctx = NewContext())
            await AgendaTestSupport.AddSourceAsync(ctx, h.DashboardId, AgendaTestSupport.Protect(protection, SecretUrl));

        await sync.SyncDashboardAsync(h.DashboardId, CancellationToken.None);

        var source = await SourceAsync(h.DashboardId);
        Assert.Equal(CalendarSyncStatus.Ok, source.LastSyncStatus);
        Assert.NotNull(source.LastError);
        Assert.Contains("tronquée", source.LastError);
        Assert.True(source.LastError!.Length <= CalendarSyncService.LastErrorMaxLength);
    }

    [Fact]
    public async Task SansSource_RienNeSePasse()
    {
        var h = await SeedHouseholdAsync("none@test.local");
        var (sync, _, fetcher, _) = AgendaTestSupport.SyncService(_connection);
        await sync.SyncDashboardAsync(h.DashboardId, CancellationToken.None);
        await sync.SyncAllAsync(CancellationToken.None);
        Assert.Empty(fetcher.Calls);
    }

    [Fact]
    public async Task Annulation_RemonteAuLieuDEtreAvalee()
    {
        var h = await SeedHouseholdAsync("cancel@test.local");
        var (sync, protection, fetcher, _) = AgendaTestSupport.SyncService(_connection);
        fetcher.On(SecretPath, AgendaTestSupport.FamilleIcs());
        using (var ctx = NewContext())
            await AgendaTestSupport.AddSourceAsync(ctx, h.DashboardId, AgendaTestSupport.Protect(protection, SecretUrl));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sync.SyncAllAsync(cts.Token));
    }
}
