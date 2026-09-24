using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.API.Services.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace FinanceApp.API.Controllers;

/// <summary>
/// Les documents d'un dashboard : réception, métadonnées, contenu, suppression. Le disque est confié à
/// DocumentStorage, la ligne à AppDbContext. Appartenance par membre du dashboard, en une requête,
/// 404 hors périmètre, y compris pour le contenu.
/// </summary>
[ApiController]
[Route("api/documents")]
[Authorize]
public class DocumentController : ApiControllerBase
{
    private readonly AppDbContext _context;
    private readonly DocumentStorage _storage;
    private readonly DocumentStorageOptions _options;
    private readonly DocumentDeposit _deposit;

    public DocumentController(AppDbContext context, DocumentStorage storage, DocumentStorageOptions options, DocumentDeposit deposit)
    {
        _context = context;
        _storage = storage;
        _options = options;
        _deposit = deposit;
    }

    private Task<bool> IsMemberAsync(int dashboardId, int userId) =>
        _context.Dashboards.AnyAsync(d => d.Id == dashboardId && d.Members.Any(m => m.UserId == userId));

    private Task<Document?> FindOwnedAsync(int id, int userId) =>
        _context.Documents.FirstOrDefaultAsync(d => d.Id == id && d.Dashboard.Members.Any(m => m.UserId == userId));

    private Task<bool> EcheanceInDashboardAsync(int echeanceId, int dashboardId) =>
        _context.Echeances.AnyAsync(e => e.Id == echeanceId && e.DashboardId == dashboardId);

    private static DocumentDto Map(Document d) => new()
    {
        Id = d.Id,
        DashboardId = d.DashboardId,
        EcheanceId = d.EcheanceId,
        Kind = d.Kind.ToString(),
        FiscalYear = d.FiscalYear,
        OriginalFileName = d.OriginalFileName,
        ContentType = d.ContentType,
        SizeBytes = d.SizeBytes,
        Sha256 = d.Sha256,
        UploadedByUserId = d.UploadedByUserId,
        // Relu de SQLite, le DateTime sort en Kind Unspecified, donc sans « Z » : « déposé le » glisserait d'un
        // jour entre 22 h et minuit. Même procédé qu'EcheanceController et AgendaCalendarStatus.From.
        CreatedAt = DateTime.SpecifyKind(d.CreatedAt, DateTimeKind.Utc),
        Source = d.Source.ToString(),
    };

    private static DateTime? Utc(DateTime? value) => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null;

    private static MailSourceDto MapMailSource(MailSource s) => new()
    {
        Address = s.Address,
        LastAttemptAt = Utc(s.LastAttemptAt),
        LastSyncAt = Utc(s.LastSyncAt),
        LastSyncStatus = s.LastSyncStatus.ToString(),
        LastError = s.LastError,
        LastDepositAt = Utc(s.LastDepositAt),
        DepositedCount = s.DepositedCount,
    };

    /// <summary>Nom d'affichage : la dernière composante de ce que le client a envoyé, tronquée. Jamais un chemin.</summary>
    internal static string DisplayName(string? clientFileName)
    {
        var name = clientFileName ?? string.Empty;
        var cut = name.LastIndexOfAny(new[] { '/', '\\' });
        if (cut >= 0) name = name[(cut + 1)..];
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0) name = "document";
        return name.Length <= 250 ? name : name[..250];
    }

    /// <summary>
    /// Réception d'un fichier. Ordre : appartenance, réception dans .incoming (type et empreinte au fil
    /// de l'eau), puis DocumentDeposit pour doublon, quota, ligne, rangement. Toute sortie avant le
    /// rangement efface le .part.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(DocumentStorageOptions.MaxRequestBytes)]
    public async Task<ActionResult<DocumentDto>> Upload([FromForm] UploadDocumentDto dto, CancellationToken ct)
    {
        var userId = GetUserId();
        if (!await IsMemberAsync(dto.DashboardId, userId)) return NotFound();
        // Une échéance d'un autre dashboard : 404, comme toute ligne hors périmètre.
        if (dto.EcheanceId.HasValue && !await EcheanceInDashboardAsync(dto.EcheanceId.Value, dto.DashboardId)) return NotFound();
        if (dto.Kind is null) return BadRequest("Nature du document manquante.");
        if (dto.File == null || dto.File.Length == 0) return BadRequest("Fichier manquant.");

        StageResult staged;
        await using (var source = dto.File.OpenReadStream())
            staged = await _storage.StageAsync(source, ct);

        switch (staged.Outcome)
        {
            case StageOutcome.Empty: return BadRequest("Fichier vide.");
            case StageOutcome.UnknownType: return StatusCode(StatusCodes.Status415UnsupportedMediaType, "Seuls les PDF, JPEG et PNG sont acceptés, d'après leur contenu.");
            case StageOutcome.TooLarge: return StatusCode(StatusCodes.Status413PayloadTooLarge, $"Fichier au-delà de {_options.MaxFileBytes} octets.");
        }
        var file = staged.File!;

        // Doublon, quota, ligne, rangement : DocumentDeposit, partagé avec l'ingestion par mail. Le .part est
        // effacé par lui sur toute sortie autre que Created.
        var deposited = await _deposit.DepositAsync(file,
            new DepositRequest(dto.DashboardId, dto.EcheanceId, dto.Kind.Value, dto.FiscalYear, DisplayName(dto.File.FileName), userId, DocumentSource.Upload, null), ct);
        switch (deposited.Outcome)
        {
            case DepositOutcome.Duplicate:
                return Conflict(new DuplicateDocumentDto { ExistingDocumentId = deposited.ExistingDocumentId!.Value, Message = "Ce fichier est déjà rangé dans ce dashboard." });
            case DepositOutcome.QuotaExceeded:
                return StatusCode(StatusCodes.Status507InsufficientStorage, "Quota de stockage du dashboard atteint.");
        }
        var document = deposited.Document!;
        return CreatedAtAction(nameof(GetById), new { id = document.Id }, Map(document));
    }

    [HttpGet]
    public async Task<ActionResult<List<DocumentDto>>> GetAll(
        [FromQuery] int dashboardId,
        [FromQuery] int? fiscalYear,
        [FromQuery] DocumentKind? kind,
        [FromQuery] int? echeanceId)
    {
        var userId = GetUserId();
        if (!await IsMemberAsync(dashboardId, userId)) return NotFound();

        var query = _context.Documents.Where(d => d.DashboardId == dashboardId);
        if (fiscalYear.HasValue) query = query.Where(d => d.FiscalYear == fiscalYear.Value);
        if (kind.HasValue) query = query.Where(d => d.Kind == kind.Value);
        if (echeanceId.HasValue) query = query.Where(d => d.EcheanceId == echeanceId.Value);

        var rows = await query.OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).ToListAsync();
        return Ok(rows.Select(Map).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DocumentDto>> GetById(int id)
    {
        var document = await FindOwnedAsync(id, GetUserId());
        if (document == null) return NotFound();
        return Ok(Map(document));
    }

    /// <summary>
    /// Le fichier, en ligne, sous le Content-Type déduit à la réception. Le nom d'origine part en
    /// filename* (UTF-8), il ne sert qu'à l'affichage. Ligne sans fichier sur le disque : 410.
    /// </summary>
    [HttpGet("{id}/content")]
    public async Task<IActionResult> GetContent(int id)
    {
        var document = await FindOwnedAsync(id, GetUserId());
        if (document == null) return NotFound();

        var stream = _storage.Open(document.StoredPath);
        if (stream == null) return StatusCode(StatusCodes.Status410Gone, "Le fichier n'est plus sur le disque.");

        var disposition = new ContentDispositionHeaderValue("inline");
        disposition.SetHttpFileName(document.OriginalFileName);
        Response.Headers[HeaderNames.ContentDisposition] = disposition.ToString();
        // Déjà posé par le middleware global, redit ici : ce contenu vient de l'extérieur, le navigateur ne devine rien.
        Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";

        return File(stream, document.ContentType, enableRangeProcessing: true);
    }

    /// <summary>Seules les métadonnées changent. Le fichier, son type et son empreinte sont figés à la réception.</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<DocumentDto>> Update(int id, UpdateDocumentDto dto)
    {
        var document = await FindOwnedAsync(id, GetUserId());
        if (document == null) return NotFound();
        if (dto.EcheanceId.HasValue && !await EcheanceInDashboardAsync(dto.EcheanceId.Value, document.DashboardId)) return NotFound();
        if (dto.Kind is null) return BadRequest("Nature du document manquante.");

        document.Kind = dto.Kind.Value;
        document.FiscalYear = dto.FiscalYear;
        document.EcheanceId = dto.EcheanceId;
        await _context.SaveChangesAsync();
        return Ok(Map(document));
    }

    /// <summary>La ligne d'abord, le fichier ensuite. Un fichier déjà absent n'est pas une erreur.</summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var document = await FindOwnedAsync(id, GetUserId());
        if (document == null) return NotFound();

        _context.Documents.Remove(document);
        await _context.SaveChangesAsync();
        _storage.Delete(document.StoredPath);
        return NoContent();
    }

    /// <summary>L'état de la boîte factures du dashboard. 204 tant qu'aucun relevé n'a créé la ligne : la carte ne s'affiche pas.</summary>
    [HttpGet("mail-source")]
    public async Task<ActionResult<MailSourceDto>> GetMailSource([FromQuery] int dashboardId)
    {
        if (!await IsMemberAsync(dashboardId, GetUserId())) return NotFound();
        var source = await CurrentMailSourceAsync(dashboardId, CancellationToken.None);
        if (source == null) return NoContent();
        return Ok(MapMailSource(source));
    }

    /// <summary>
    /// La ligne active de la boîte du dashboard. La clé unique est (DashboardId, Address) : si l'adresse
    /// configurée change un jour, une seconde ligne apparaît, et c'est la dernière relevée qui compte, pas la
    /// première insérée. Le service de fond relève l'adresse courante toutes les six heures.
    /// </summary>
    private Task<MailSource?> CurrentMailSourceAsync(int dashboardId, CancellationToken ct) =>
        _context.MailSources.AsNoTracking()
            .Where(s => s.DashboardId == dashboardId)
            .OrderByDescending(s => s.LastAttemptAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Relève la boîte maintenant, sous le sémaphore du service de fond. 404 si le service n'est pas configuré
    /// ou vise un autre dashboard, 409 sans attendre si un relevé est déjà en cours. Politique « mail-refresh »
    /// (cinq par minute et par utilisateur) : chaque appel ouvre une connexion chez Gmail.
    /// </summary>
    [HttpPost("mail-source/refresh")]
    [EnableRateLimiting("mail-refresh")]
    public async Task<ActionResult<MailSourceDto>> RefreshMailSource([FromQuery] int dashboardId, [FromServices] MailIngestService mailIngest, CancellationToken ct)
    {
        if (!await IsMemberAsync(dashboardId, GetUserId())) return NotFound();
        if (!mailIngest.IsConfigured || mailIngest.ConfiguredDashboardId != dashboardId) return NotFound();

        var summary = await mailIngest.TryRunOnceAsync(ct);
        if (summary == null) return Conflict("Relevé déjà en cours.");

        _context.ChangeTracker.Clear();
        var source = await CurrentMailSourceAsync(dashboardId, ct);
        if (source == null) return NoContent();
        return Ok(MapMailSource(source));
    }
}
