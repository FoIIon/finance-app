using System.Security.Cryptography;

namespace FinanceApp.API.Services;

/// <summary>Racine et limites du stockage des documents, résolues au démarrage dans Program.cs.</summary>
public sealed class DocumentStorageOptions
{
    /// <summary>20 Mio par fichier, faute de valeur dans Documents:MaxFileBytes.</summary>
    public const long DefaultMaxFileBytes = 20L * 1024 * 1024;
    /// <summary>2 Gio par dashboard, faute de valeur dans Documents:QuotaBytesPerDashboard.</summary>
    public const long DefaultQuotaBytesPerDashboard = 2L * 1024 * 1024 * 1024;
    /// <summary>
    /// Plafond de la requête d'envoi (fichier plus champs du formulaire), posé par [RequestSizeLimit] qui
    /// exige une constante. Une valeur configurée au-dessus de DefaultMaxFileBytes n'a donc aucun effet.
    /// </summary>
    public const long MaxRequestBytes = DefaultMaxFileBytes + 64 * 1024;

    public string Root { get; init; } = string.Empty;
    public long MaxFileBytes { get; init; } = DefaultMaxFileBytes;
    public long QuotaBytesPerDashboard { get; init; } = DefaultQuotaBytesPerDashboard;
}

public enum StageOutcome
{
    Ok,
    Empty,
    UnknownType,
    TooLarge
}

/// <summary>Un fichier reçu, posé dans .incoming, empreinte et type connus, pas encore rangé.</summary>
public sealed record StagedFile(string PartPath, string Sha256, long SizeBytes, FileKind Kind);

/// <summary>Résultat d'une réception. Hors Ok, le .part a déjà été effacé et File est null.</summary>
public sealed record StageResult(StageOutcome Outcome, StagedFile? File);

/// <summary>
/// Le disque, et rien que le disque : réception en deux temps (.incoming puis rangement une fois la
/// ligne insérée), lecture et suppression. Le nom sur disque vient de l'identifiant de la ligne et de
/// l'année, le type des octets de tête. Aucun chemin ne sort de la racine.
/// </summary>
public class DocumentStorage
{
    private const string IncomingDirName = ".incoming";
    private static readonly TimeSpan IncomingMaxAge = TimeSpan.FromHours(24);

    private readonly DocumentStorageOptions _options;

    public string Root => _options.Root;
    public string IncomingDir => Path.Combine(Root, IncomingDirName);

    public DocumentStorage(DocumentStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Root) || !Path.IsPathRooted(options.Root))
            throw new ArgumentException("La racine des documents doit être un chemin absolu, voir ResolveRoot.", nameof(options));
        _options = options;
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(IncomingDir);
        CleanIncoming(DateTime.UtcNow);
    }

    /// <summary>
    /// Résout Documents:Root. Obligatoire, même exigence que Jwt:Key. Un chemin relatif part du
    /// ContentRootPath. Refusé sous wwwroot (UseStaticFiles servirait chaque facture à qui en devine
    /// l'URL) et sur le dossier de l'application lui-même.
    /// </summary>
    public static string ResolveRoot(string? configured, string contentRootPath, string? webRootPath, string appBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("Documents:Root non configuré. Renseigner Documents__Root (dossier hors de wwwroot).");

        var root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(contentRootPath, configured));
        var wwwroot = Path.GetFullPath(string.IsNullOrWhiteSpace(webRootPath) ? Path.Combine(contentRootPath, "wwwroot") : webRootPath);

        if (IsSameOrUnder(root, wwwroot))
            throw new InvalidOperationException($"Documents:Root ({root}) est sous wwwroot, qui est servi en clair. Choisir un dossier hors du site.");
        if (PathsEqual(root, contentRootPath) || PathsEqual(root, appBaseDirectory))
            throw new InvalidOperationException($"Documents:Root ({root}) est le dossier de l'application. Choisir un sous-dossier dédié.");

        return root;
    }

    /// <summary>
    /// Copie le flux dans .incoming/{guid}.part en calculant le SHA-256 au fil de l'eau. Le type se
    /// tranche sur les huit premiers octets, et un type inconnu ou un dépassement de taille arrête la
    /// copie : le .part est effacé, rien ne reste.
    /// </summary>
    public async Task<StageResult> StageAsync(Stream source, CancellationToken ct = default)
    {
        Directory.CreateDirectory(IncomingDir);
        var partPath = Path.Combine(IncomingDir, $"{Guid.NewGuid():N}.part");
        var head = new byte[FileSignature.HeaderLength];
        var headLength = 0;
        FileKind? kind = null;
        long total = 0;
        StageOutcome? failure = null;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            await using (var target = new FileStream(partPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while (failure is null && (read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;
                    if (total > _options.MaxFileBytes) { failure = StageOutcome.TooLarge; break; }

                    if (headLength < head.Length)
                    {
                        var take = Math.Min(head.Length - headLength, read);
                        Buffer.BlockCopy(buffer, 0, head, headLength, take);
                        headLength += take;
                        if (headLength == head.Length)
                        {
                            kind = FileSignature.Detect(head);
                            if (kind is null) { failure = StageOutcome.UnknownType; break; }
                        }
                    }

                    hash.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            if (failure is null && total == 0) failure = StageOutcome.Empty;
            // Fichier plus court que l'en-tête : on tranche sur ce qu'on a.
            if (failure is null && kind is null)
            {
                kind = FileSignature.Detect(head.AsSpan(0, headLength));
                if (kind is null) failure = StageOutcome.UnknownType;
            }

            if (failure is not null)
            {
                TryDelete(partPath);
                return new StageResult(failure.Value, null);
            }

            var sha = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return new StageResult(StageOutcome.Ok, new StagedFile(partPath, sha, total, kind!.Value));
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }
    }

    /// <summary>Range le fichier reçu sous {année}/{id}.{ext}. Rend le chemin relatif à stocker en base.</summary>
    public string Commit(StagedFile staged, int documentId, int year)
    {
        var fileName = $"{documentId}.{FileSignature.Extension(staged.Kind)}";
        var directory = Path.Combine(Root, year.ToString());
        Directory.CreateDirectory(directory);
        File.Move(staged.PartPath, Path.Combine(directory, fileName));
        return $"{year}/{fileName}";
    }

    /// <summary>Abandonne un fichier reçu (doublon, quota, insertion échouée). Déjà absent : rien à faire.</summary>
    public void Discard(StagedFile? staged)
    {
        if (staged is not null) TryDelete(staged.PartPath);
    }

    /// <summary>Le fichier d'une ligne, ou null s'il manque sur le disque. Un chemin qui sort de la racine est une erreur, pas une absence.</summary>
    public FileStream? Open(string storedPath)
    {
        var full = Resolve(storedPath);
        if (!File.Exists(full)) return null;
        return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
    }

    /// <summary>Efface le fichier d'une ligne déjà supprimée. Déjà absent : ce n'est pas une erreur.</summary>
    public void Delete(string storedPath) => TryDelete(Resolve(storedPath));

    /// <summary>Chemin absolu d'un StoredPath, vérifié sous la racine.</summary>
    public string Resolve(string storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath) || Path.IsPathRooted(storedPath)
            || storedPath.Split('/', '\\').Any(segment => segment is "" or "." or ".."))
            throw new InvalidOperationException($"Chemin de document invalide : « {storedPath} ».");

        var full = Path.GetFullPath(Path.Combine(Root, storedPath));
        if (!IsSameOrUnder(full, Root) || PathsEqual(full, Root))
            throw new InvalidOperationException($"Chemin de document hors racine : « {storedPath} ».");
        return full;
    }

    /// <summary>Efface les .part de plus de 24 h, restes d'envois interrompus.</summary>
    public int CleanIncoming(DateTime utcNow)
    {
        if (!Directory.Exists(IncomingDir)) return 0;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(IncomingDir, "*.part"))
        {
            if (utcNow - File.GetLastWriteTimeUtc(file) < IncomingMaxAge) continue;
            if (TryDelete(file)) removed++;
        }
        return removed;
    }

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Trim(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static bool PathsEqual(string a, string b) => string.Equals(Trim(a), Trim(b), PathComparison);

    /// <summary>Vrai si <paramref name="path"/> est <paramref name="ancestor"/> ou l'un de ses descendants.</summary>
    internal static bool IsSameOrUnder(string path, string ancestor)
    {
        var p = Trim(path);
        var a = Trim(ancestor);
        return string.Equals(p, a, PathComparison)
            || p.StartsWith(a + Path.DirectorySeparatorChar, PathComparison)
            || p.StartsWith(a + Path.AltDirectorySeparatorChar, PathComparison);
    }
}
