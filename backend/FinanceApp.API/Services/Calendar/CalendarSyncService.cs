using System.Collections.Concurrent;
using System.Security.Cryptography;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services.Calendar;

/// <summary>
/// Synchronisation de fond des calendriers, distincte de BankSyncService : toutes les
/// Calendar:SyncIntervalMinutes minutes, un essai par dashboard, chacun dans son propre try, le jeton
/// d'arrêt propagé partout. Le déchiffrement de l'adresse se fait dans le try : une clé perdue pose
/// KeyLost et la source n'est plus réessayée tant que l'adresse n'est pas ressaisie. Les occurrences
/// d'un dashboard sont remplacées en bloc, dans une transaction, seulement après un 200 et un parse
/// valide. Le contrôleur partage le sémaphore par dashboard pour la synchronisation immédiate.
///
/// Rien de ce qui est journalisé ne contient l'adresse : ni l'exception (son message pourrait la
/// porter), ni LastError. LastSyncAt est l'instant de la dernière tentative, réussie ou non.
/// </summary>
public class CalendarSyncService : BackgroundService
{
    public const string ProtectorPurpose = "Calendar.IcsUrl";
    public const int LastErrorMaxLength = 200;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICalendarIcsFetcher _fetcher;
    private readonly IDataProtector _protector;
    private readonly IOptions<CalendarOptions> _options;
    private readonly IOptions<HouseholdOptions> _household;
    private readonly TimeProvider _clock;
    private readonly ILogger<CalendarSyncService> _logger;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks = new();

    public CalendarSyncService(
        IServiceScopeFactory scopeFactory,
        ICalendarIcsFetcher fetcher,
        IDataProtectionProvider dataProtection,
        IOptions<CalendarOptions> options,
        IOptions<HouseholdOptions> household,
        TimeProvider clock,
        ILogger<CalendarSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _fetcher = fetcher;
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _options = options;
        _household = household;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError("Erreur de la synchronisation des calendriers : {Type}.", ex.GetType().FullName);
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_options.Value.SyncIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Toutes les sources sauf celles en KeyLost, une par une : une source malade n'empêche pas les autres.</summary>
    public async Task SyncAllAsync(CancellationToken cancellationToken)
    {
        List<int> dashboardIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dashboardIds = await context.CalendarSources
                .Where(s => s.LastSyncStatus != CalendarSyncStatus.KeyLost)
                .OrderBy(s => s.DashboardId)
                .Select(s => s.DashboardId)
                .ToListAsync(cancellationToken);
        }

        foreach (var dashboardId in dashboardIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SyncDashboardAsync(dashboardId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError("Synchronisation du calendrier du dashboard {DashboardId} en échec : {Type}.", dashboardId, ex.GetType().FullName);
            }
        }
    }

    /// <summary>Synchronise un dashboard, sous son sémaphore. Laisse remonter les exceptions inattendues.</summary>
    public async Task SyncDashboardAsync(int dashboardId, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(dashboardId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SyncSourceAsync(context, dashboardId, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SyncSourceAsync(AppDbContext context, int dashboardId, CancellationToken cancellationToken)
    {
        var source = await context.CalendarSources.FirstOrDefaultAsync(s => s.DashboardId == dashboardId, cancellationToken);
        if (source == null) return;
        var nowUtc = _clock.GetUtcNow().UtcDateTime;

        string url;
        try
        {
            url = _protector.Unprotect(source.EncryptedUrl);
        }
        catch (CryptographicException)
        {
            await MarkAsync(context, source, CalendarSyncStatus.KeyLost, "Clé de chiffrement perdue : ressaisir l'adresse du calendrier.", nowUtc, cancellationToken);
            return;
        }

        var refusal = IcsUrlPolicy.Refuse(url, _options.Value.AllowedHosts, out var uri);
        if (refusal != null)
        {
            await MarkAsync(context, source, CalendarSyncStatus.Invalid, $"Adresse refusée : {refusal}", nowUtc, cancellationToken);
            return;
        }

        var fetched = await _fetcher.FetchAsync(uri!, cancellationToken);
        if (fetched.Status != CalendarSyncStatus.Ok || fetched.Ics == null)
        {
            await MarkAsync(context, source, fetched.Status, fetched.Error ?? "Téléchargement en échec.", nowUtc, cancellationToken);
            return;
        }

        var household = _household.Value;
        var todayLocal = household.TodayLocal(nowUtc);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(
            todayLocal.AddMonths(-_options.Value.WindowMonthsBack).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), household.Zone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(
            todayLocal.AddMonths(_options.Value.WindowMonthsForward).AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), household.Zone);

        IcsExpansion expansion;
        try
        {
            expansion = IcsOccurrenceExpander.Expand(fetched.Ics, fromUtc, toUtc, household.Zone, _options.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await MarkAsync(context, source, CalendarSyncStatus.Invalid, $"Flux iCalendar illisible ({ex.GetType().Name}).", nowUtc, cancellationToken);
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.CalendarOccurrences.Where(o => o.DashboardId == dashboardId).ExecuteDeleteAsync(cancellationToken);
        foreach (var occurrence in expansion.Occurrences) occurrence.DashboardId = dashboardId;
        context.CalendarOccurrences.AddRange(expansion.Occurrences);
        source.CalendarName = expansion.CalendarName ?? source.CalendarName;
        source.LastSyncAt = nowUtc;
        source.LastSyncStatus = CalendarSyncStatus.Ok;
        source.LastError = Truncate(expansion.Warning);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Pose l'issue d'une tentative sans toucher aux occurrences existantes.</summary>
    private static async Task MarkAsync(AppDbContext context, CalendarSource source, CalendarSyncStatus status, string error, DateTime nowUtc, CancellationToken cancellationToken)
    {
        source.LastSyncAt = nowUtc;
        source.LastSyncStatus = status;
        source.LastError = Truncate(error);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value) =>
        value == null ? null : value.Length <= LastErrorMaxLength ? value : value[..LastErrorMaxLength];
}
