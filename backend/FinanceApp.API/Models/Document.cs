namespace FinanceApp.API.Models;

/// <summary>
/// Un fichier du ménage (facture, avertissement-extrait de rôle, contrat) rangé sous la racine
/// Documents:Root, jamais sous wwwroot. Le nom sur disque vient de l'identifiant, le type des octets de
/// tête : rien de ce que le client annonce ne touche au chemin ni au Content-Type servi.
/// </summary>
public class Document
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    /// <summary>Échéance à laquelle le document se rattache. Supprimer l'échéance détache le document, ne l'efface pas.</summary>
    public int? EcheanceId { get; set; }
    public DocumentKind Kind { get; set; }
    /// <summary>Année fiscale concernée, pour les documents Fiscal surtout.</summary>
    public int? FiscalYear { get; set; }
    /// <summary>Nom d'origine, affiché seulement. Jamais utilisé pour construire un chemin.</summary>
    public string OriginalFileName { get; set; } = string.Empty;
    /// <summary>Chemin relatif à la racine, de la forme {année}/{id}.{ext}.</summary>
    public string StoredPath { get; set; } = string.Empty;
    /// <summary>Type déduit des octets de tête (FileSignature), pas du client.</summary>
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    /// <summary>SHA-256 du contenu, 64 caractères hexadécimaux minuscules. Unique par dashboard.</summary>
    public string Sha256 { get; set; } = string.Empty;
    /// <summary>Le membre qui a déposé le fichier. Null pour un dépôt par le service d'ingestion mail.</summary>
    public int? UploadedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Upload ou Mail. Toute ligne antérieure au lot mail vaut Upload.</summary>
    public DocumentSource Source { get; set; } = DocumentSource.Upload;
    /// <summary>En-tête Message-ID du mail d'origine, traçabilité seulement. Le doublon se tranche par Sha256.</summary>
    public string? MailMessageId { get; set; }

    public Dashboard Dashboard { get; set; } = null!;
    public Echeance? Echeance { get; set; }
    public User? UploadedByUser { get; set; }
}
