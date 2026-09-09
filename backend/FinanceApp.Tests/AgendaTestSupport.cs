using System.Net;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Calendar;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinanceApp.Tests;

/// <summary>Une horloge figée : les tests de l'agenda se jouent toujours le 9 septembre 2026.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; }
    public FixedTimeProvider(DateTimeOffset now) => Now = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Un téléchargeur qui répond ce qu'on lui a dit, par hôte et chemin, et compte ses appels.</summary>
internal sealed class FakeIcsFetcher : ICalendarIcsFetcher
{
    private readonly Dictionary<string, Func<IcsFetchResult>> _byPath = new();
    public List<string> Calls { get; } = new();

    public FakeIcsFetcher On(string path, Func<IcsFetchResult> result)
    {
        _byPath[path] = result;
        return this;
    }

    public FakeIcsFetcher On(string path, string ics) => On(path, () => IcsFetchResult.Ok(ics));

    public Task<IcsFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        Calls.Add(url.AbsolutePath);
        if (!_byPath.TryGetValue(url.AbsolutePath, out var result))
            throw new InvalidOperationException("Chemin non prévu par le test.");
        return Task.FromResult(result());
    }
}

/// <summary>Un handler HTTP qui rend une réponse fixe, pour tester le téléchargeur réel sans réseau.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(_respond(request));
}

internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly HttpMessageHandler _handler;
    public string? LastName { get; private set; }
    public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
    public HttpClient CreateClient(string name)
    {
        LastName = name;
        return new HttpClient(_handler, disposeHandler: false);
    }
}

internal static class AgendaTestSupport
{
    /// <summary>Le 9 septembre 2026, 6h00 UTC, soit 8h00 à Bruxelles.</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 9, 6, 0, 0, TimeSpan.Zero);
    public static readonly DateOnly Today = new(2026, 9, 9);
    public static readonly TimeZoneInfo Brussels = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");

    public static string FamilleIcs() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "famille.ics"));

    /// <summary>Les options telles qu'appsettings.json les pose : la classe n'a pas de défaut pour AllowedHosts.</summary>
    public static CalendarOptions Options(Action<CalendarOptions>? tune = null)
    {
        var o = new CalendarOptions { AllowedHosts = new[] { "calendar.google.com" } };
        tune?.Invoke(o);
        return o;
    }

    public static HouseholdOptions Household() => new() { TimeZone = "Europe/Brussels" };

    /// <summary>Un conteneur minimal pour le service de fond : AppDbContext sur la connexion partagée.</summary>
    public static ServiceProvider Services(SqliteConnection connection)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        return services.BuildServiceProvider();
    }

    public static (CalendarSyncService Sync, IDataProtectionProvider Protection, FakeIcsFetcher Fetcher, FixedTimeProvider Clock) SyncService(
        SqliteConnection connection, FakeIcsFetcher? fetcher = null, CalendarOptions? options = null)
    {
        var protection = new EphemeralDataProtectionProvider();
        var clock = new FixedTimeProvider(Now);
        fetcher ??= new FakeIcsFetcher();
        var sync = new CalendarSyncService(
            Services(connection).GetRequiredService<IServiceScopeFactory>(),
            fetcher,
            protection,
            Microsoft.Extensions.Options.Options.Create(options ?? Options()),
            Microsoft.Extensions.Options.Options.Create(Household()),
            clock,
            NullLogger<CalendarSyncService>.Instance);
        return (sync, protection, fetcher, clock);
    }

    public static string Protect(IDataProtectionProvider protection, string url) =>
        protection.CreateProtector(CalendarSyncService.ProtectorPurpose).Protect(url);

    public static async Task<CalendarSource> AddSourceAsync(AppDbContext ctx, int dashboardId, string encryptedUrl, CalendarSyncStatus status = CalendarSyncStatus.Pending)
    {
        var source = new CalendarSource { DashboardId = dashboardId, EncryptedUrl = encryptedUrl, LastSyncStatus = status };
        ctx.CalendarSources.Add(source);
        await ctx.SaveChangesAsync();
        return source;
    }

    public static HttpResponseMessage Response(HttpStatusCode code, string body, long? contentLength = null)
    {
        var response = new HttpResponseMessage(code) { Content = new StringContent(body) };
        if (contentLength.HasValue) response.Content.Headers.ContentLength = contentLength;
        return response;
    }
}
