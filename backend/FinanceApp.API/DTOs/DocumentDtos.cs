using System.ComponentModel.DataAnnotations;
using FinanceApp.API.Models;

namespace FinanceApp.API.DTOs;

public class DocumentDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public int? EcheanceId { get; set; }
    /// <summary>Facture, Fiscal, Contrat ou Autre.</summary>
    public string Kind { get; set; } = string.Empty;
    public int? FiscalYear { get; set; }
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public int UploadedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Envoi multipart : le fichier et ses métadonnées. Le type du fichier se déduit de son contenu, jamais de ce formulaire.</summary>
public class UploadDocumentDto
{
    [Required]
    public IFormFile? File { get; set; }

    [Required]
    public int DashboardId { get; set; }

    public int? EcheanceId { get; set; }

    [Range(1990, 2100)]
    public int? FiscalYear { get; set; }

    [Required]
    public DocumentKind Kind { get; set; }
}

/// <summary>Remplacement des seules métadonnées modifiables. Le fichier lui-même ne change pas.</summary>
public class UpdateDocumentDto
{
    [Required]
    public DocumentKind Kind { get; set; }

    [Range(1990, 2100)]
    public int? FiscalYear { get; set; }

    public int? EcheanceId { get; set; }
}

/// <summary>Réponse 409 d'un envoi dont le contenu existe déjà dans le dashboard.</summary>
public class DuplicateDocumentDto
{
    public int ExistingDocumentId { get; set; }
    public string Message { get; set; } = string.Empty;
}
