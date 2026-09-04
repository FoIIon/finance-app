namespace FinanceApp.API.Services;

/// <summary>Les seuls types de fichier acceptés dans les documents.</summary>
public enum FileKind
{
    Pdf,
    Jpeg,
    Png
}

/// <summary>
/// Reconnaît un fichier à ses octets de tête. L'extension et le Content-Type annoncés par le client ne
/// sont jamais consultés : un exécutable renommé en .pdf est refusé, un SVG ou une page HTML aussi
/// (ils s'exécuteraient dans le navigateur qui les affiche).
/// </summary>
public static class FileSignature
{
    /// <summary>Nombre d'octets suffisant pour trancher (la signature PNG en fait huit).</summary>
    public const int HeaderLength = 8;

    private static readonly byte[] Pdf = "%PDF-"u8.ToArray();
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    public static FileKind? Detect(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith(Pdf)) return FileKind.Pdf;
        if (head.StartsWith(Jpeg)) return FileKind.Jpeg;
        if (head.StartsWith(Png)) return FileKind.Png;
        return null;
    }

    public static string Extension(FileKind kind) => kind switch
    {
        FileKind.Pdf => "pdf",
        FileKind.Jpeg => "jpg",
        FileKind.Png => "png",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string ContentType(FileKind kind) => kind switch
    {
        FileKind.Pdf => "application/pdf",
        FileKind.Jpeg => "image/jpeg",
        FileKind.Png => "image/png",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
