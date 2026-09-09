using System.ComponentModel.DataAnnotations;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Calendar;
using FinanceApp.API.Services.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Controllers;

/// <summary>Corps de PUT api/calendar/source : l'adresse ICS privée. Elle n'est jamais renvoyée.</summary>
public sealed class CalendarSourceUrlDto
{
    [Required]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// La source de calendrier d'un dashboard. Appartenance vérifiée par membre du dashboard avant toute
/// lecture, 404 hors périmètre (patron d'EcheanceController). L'adresse est validée par IcsUrlPolicy,
/// chiffrée par IDataProtectionProvider, et ne figure dans aucune réponse, même chiffrée : le DTO ne
/// porte que le nom du calendrier et l'état de la dernière synchronisation.
/// </summary>
[ApiController]
[Route("api/calendar")]
[Authorize]
public class CalendarController : ApiControllerBase
{
    private readonly AppDbContext _context;
    private readonly IDataProtector _protector;
    private readonly CalendarSyncService _sync;
    private readonly IOptions<CalendarOptions> _options;
    private readonly TimeProvider _clock;

    public CalendarController(
        AppDbContext context,
        IDataProtectionProvider dataProtection,
        CalendarSyncService sync,
        IOptions<CalendarOptions> options,
        TimeProvider clock)
    {
        _context = context;
        _protector = dataProtection.CreateProtector(CalendarSyncService.ProtectorPurpose);
        _sync = sync;
        _options = options;
        _clock = clock;
    }

    private Task<bool> IsMemberAsync(int dashboardId, int userId) =>
        _context.Dashboards.AnyAsync(d => d.Id == dashboardId && d.Members.Any(m => m.UserId == userId));

    private async Task<AgendaCalendarStatus> StatusAsync(int dashboardId)
    {
        var source = await _context.CalendarSources.AsNoTracking().FirstOrDefaultAsync(s => s.DashboardId == dashboardId);
        return AgendaCalendarStatus.From(source);
    }

    [HttpGet("source")]
    public async Task<ActionResult<AgendaCalendarStatus>> GetSource([FromQuery] int dashboardId)
    {
        if (!await IsMemberAsync(dashboardId, GetUserId())) return NotFound();
        return Ok(await StatusAsync(dashboardId));
    }

    /// <summary>Valide, chiffre, enregistre en Pending, puis synchronise ce dashboard tout de suite. 400 si l'adresse est refusée, sans la répéter.</summary>
    [HttpPut("source")]
    public async Task<ActionResult<AgendaCalendarStatus>> PutSource([FromQuery] int dashboardId, [FromBody] CalendarSourceUrlDto body, CancellationToken cancellationToken)
    {
        if (!await IsMemberAsync(dashboardId, GetUserId())) return NotFound();

        var refusal = IcsUrlPolicy.Refuse(body.Url, _options.Value.AllowedHosts, out var uri);
        if (refusal != null) return BadRequest(refusal);

        var source = await _context.CalendarSources.FirstOrDefaultAsync(s => s.DashboardId == dashboardId, cancellationToken);
        if (source == null)
        {
            source = new CalendarSource { DashboardId = dashboardId, CreatedAt = _clock.GetUtcNow().UtcDateTime };
            _context.CalendarSources.Add(source);
        }
        source.EncryptedUrl = _protector.Protect(uri!.AbsoluteUri);
        source.LastSyncStatus = CalendarSyncStatus.Pending;
        source.LastError = null;
        await _context.SaveChangesAsync(cancellationToken);

        await _sync.SyncDashboardAsync(dashboardId, cancellationToken);
        _context.ChangeTracker.Clear();
        return Ok(await StatusAsync(dashboardId));
    }

    /// <summary>Supprime la source et toutes les occurrences du dashboard. 204 même sans source.</summary>
    [HttpDelete("source")]
    public async Task<ActionResult> DeleteSource([FromQuery] int dashboardId, CancellationToken cancellationToken)
    {
        if (!await IsMemberAsync(dashboardId, GetUserId())) return NotFound();

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        await _context.CalendarOccurrences.Where(o => o.DashboardId == dashboardId).ExecuteDeleteAsync(cancellationToken);
        await _context.CalendarSources.Where(s => s.DashboardId == dashboardId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Resynchronise maintenant, sous le même sémaphore que le service de fond. Cinq par minute et par utilisateur.</summary>
    [HttpPost("source/refresh")]
    [EnableRateLimiting("calendar-refresh")]
    public async Task<ActionResult<AgendaCalendarStatus>> Refresh([FromQuery] int dashboardId, CancellationToken cancellationToken)
    {
        if (!await IsMemberAsync(dashboardId, GetUserId())) return NotFound();
        if (!await _context.CalendarSources.AnyAsync(s => s.DashboardId == dashboardId, cancellationToken)) return NotFound();

        await _sync.SyncDashboardAsync(dashboardId, cancellationToken);
        return Ok(await StatusAsync(dashboardId));
    }
}
