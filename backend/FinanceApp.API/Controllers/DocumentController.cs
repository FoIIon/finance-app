using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

    public DocumentController(AppDbContext context, DocumentStorage storage, DocumentStorageOptions options)
    {
        _context = context;
        _storage = storage;
        _options = options;
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
        CreatedAt = d.CreatedAt,
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
    /// de l'eau), doublon, quota, ligne, rangement. Toute sortie avant le rangement efface le .part.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(DocumentStorageOptions.MaxRequestBytes)]
    public async Task<ActionResult<DocumentDto>> Upload([FromForm] UploadDocumentDto dto, CancellationToken ct)
    {
        var userId = GetUserId();
        if (!await IsMemberAsync(dto.DashboardId, userId)) return NotFound();
        if (dto.EcheanceId.HasValue && !await EcheanceInDashboardAsync(dto.EcheanceId.Value, dto.DashboardId))
            return BadRequest("Échéance introuvable dans ce dashboard.");
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

        string? storedPath = null;
        try
        {
            var existingId = await _context.Documents
                .Where(d => d.DashboardId == dto.DashboardId && d.Sha256 == file.Sha256)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync(ct);
            if (existingId.HasValue)
            {
                _storage.Discard(file);
                return Conflict(new DuplicateDocumentDto { ExistingDocumentId = existingId.Value, Message = "Ce fichier est déjà rangé dans ce dashboard." });
            }

            // Projection puis somme côté client, même discipline que les décimaux.
            var used = (await _context.Documents
                .Where(d => d.DashboardId == dto.DashboardId)
                .Select(d => d.SizeBytes)
                .ToListAsync(ct)).Sum();
            if (used + file.SizeBytes > _options.QuotaBytesPerDashboard)
            {
                _storage.Discard(file);
                return StatusCode(StatusCodes.Status507InsufficientStorage, "Quota de stockage du dashboard atteint.");
            }

            var now = DateTime.UtcNow;
            var document = new Document
            {
                DashboardId = dto.DashboardId,
                EcheanceId = dto.EcheanceId,
                Kind = dto.Kind,
                FiscalYear = dto.FiscalYear,
                OriginalFileName = DisplayName(dto.File.FileName),
                ContentType = FileSignature.ContentType(file.Kind),
                SizeBytes = file.SizeBytes,
                Sha256 = file.Sha256,
                UploadedByUserId = userId,
                CreatedAt = now,
                StoredPath = string.Empty,
            };

            // La ligne d'abord (elle donne l'identifiant, donc le nom sur disque), le rangement ensuite,
            // le tout sous transaction : un déplacement raté annule la ligne, une ligne ratée garde le .part
            // qui est effacé dans le catch.
            await using var tx = await _context.Database.BeginTransactionAsync(ct);
            _context.Documents.Add(document);
            await _context.SaveChangesAsync(ct);
            storedPath = _storage.Commit(file, document.Id, now.Year);
            document.StoredPath = storedPath;
            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return CreatedAtAction(nameof(GetById), new { id = document.Id }, Map(document));
        }
        catch (DbUpdateException)
        {
            // Course entre deux envois identiques : l'index unique tranche, on répond comme au doublon.
            _storage.Discard(file);
            if (storedPath != null) _storage.Delete(storedPath);
            var winner = await _context.Documents
                .Where(d => d.DashboardId == dto.DashboardId && d.Sha256 == file.Sha256)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync(CancellationToken.None);
            if (winner.HasValue)
                return Conflict(new DuplicateDocumentDto { ExistingDocumentId = winner.Value, Message = "Ce fichier est déjà rangé dans ce dashboard." });
            throw;
        }
        catch
        {
            _storage.Discard(file);
            if (storedPath != null) _storage.Delete(storedPath);
            throw;
        }
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

        return File(stream, document.ContentType, enableRangeProcessing: true);
    }

    /// <summary>Seules les métadonnées changent. Le fichier, son type et son empreinte sont figés à la réception.</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<DocumentDto>> Update(int id, UpdateDocumentDto dto)
    {
        var document = await FindOwnedAsync(id, GetUserId());
        if (document == null) return NotFound();
        if (dto.EcheanceId.HasValue && !await EcheanceInDashboardAsync(dto.EcheanceId.Value, document.DashboardId))
            return BadRequest("Échéance introuvable dans ce dashboard.");

        document.Kind = dto.Kind;
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
}
