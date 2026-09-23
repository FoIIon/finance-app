using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

/// <summary>
/// Ce qu'un appelant sait d'un fichier déjà reçu (StagedFile) et qu'il veut ranger. UploadedByUserId est
/// null pour un dépôt par le service d'ingestion mail, MailMessageId ne sert qu'à la traçabilité.
/// </summary>
public sealed record DepositRequest(int DashboardId, int? EcheanceId, DocumentKind Kind, int? FiscalYear, string DisplayName, int? UploadedByUserId, DocumentSource Source, string? MailMessageId);

public enum DepositOutcome
{
    Created,
    Duplicate,
    QuotaExceeded
}

/// <summary>Hors Created, Document est null. Sur Duplicate, ExistingDocumentId désigne la ligne déjà rangée.</summary>
public sealed record DepositResult(DepositOutcome Outcome, Document? Document, int? ExistingDocumentId);

/// <summary>
/// Le rangement d'un fichier déjà reçu, commun à l'envoi par formulaire (DocumentController.Upload) et à
/// tout autre dépôt : doublon par empreinte dans le dashboard, quota, ligne puis rangement sous
/// transaction, rattrapage de la course entre deux dépôts identiques. Le .part est effacé sur toute sortie
/// autre que Created. L'appartenance au dashboard et la réception restent à la charge de l'appelant.
/// </summary>
public class DocumentDeposit
{
    private readonly AppDbContext _context;
    private readonly DocumentStorage _storage;
    private readonly DocumentStorageOptions _options;

    public DocumentDeposit(AppDbContext context, DocumentStorage storage, DocumentStorageOptions options)
    {
        _context = context;
        _storage = storage;
        _options = options;
    }

    public async Task<DepositResult> DepositAsync(StagedFile file, DepositRequest request, CancellationToken ct)
    {
        string? storedPath = null;
        Document? document = null;
        try
        {
            var existingId = await _context.Documents
                .Where(d => d.DashboardId == request.DashboardId && d.Sha256 == file.Sha256)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync(ct);
            if (existingId.HasValue)
            {
                _storage.Discard(file);
                return new DepositResult(DepositOutcome.Duplicate, null, existingId.Value);
            }

            // Projection puis somme côté client, même discipline que les décimaux.
            var used = (await _context.Documents
                .Where(d => d.DashboardId == request.DashboardId)
                .Select(d => d.SizeBytes)
                .ToListAsync(ct)).Sum();
            if (used + file.SizeBytes > _options.QuotaBytesPerDashboard)
            {
                _storage.Discard(file);
                return new DepositResult(DepositOutcome.QuotaExceeded, null, null);
            }

            var now = DateTime.UtcNow;
            document = new Document
            {
                DashboardId = request.DashboardId,
                EcheanceId = request.EcheanceId,
                Kind = request.Kind,
                FiscalYear = request.FiscalYear,
                OriginalFileName = request.DisplayName,
                ContentType = FileSignature.ContentType(file.Kind),
                SizeBytes = file.SizeBytes,
                Sha256 = file.Sha256,
                UploadedByUserId = request.UploadedByUserId,
                CreatedAt = now,
                StoredPath = string.Empty,
                Source = request.Source,
                MailMessageId = request.MailMessageId,
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

            return new DepositResult(DepositOutcome.Created, document, null);
        }
        catch (DbUpdateException)
        {
            // Course entre deux dépôts identiques : l'index unique tranche, on répond comme au doublon.
            _storage.Discard(file);
            if (storedPath != null) _storage.Delete(storedPath);
            Detach(document);
            var winner = await _context.Documents
                .Where(d => d.DashboardId == request.DashboardId && d.Sha256 == file.Sha256)
                .Select(d => (int?)d.Id)
                .FirstOrDefaultAsync(CancellationToken.None);
            if (winner.HasValue)
                return new DepositResult(DepositOutcome.Duplicate, null, winner.Value);
            throw;
        }
        catch
        {
            _storage.Discard(file);
            if (storedPath != null) _storage.Delete(storedPath);
            Detach(document);
            throw;
        }
    }

    /// <summary>
    /// Une ligne dont l'insertion a échoué reste suivie en Added par le contexte : un appelant qui réutilise
    /// le même contexte pour le dépôt suivant (le service de fond) la réinsérerait. On l'oublie.
    /// </summary>
    private void Detach(Document? document)
    {
        if (document is not null) _context.Entry(document).State = EntityState.Detached;
    }
}
