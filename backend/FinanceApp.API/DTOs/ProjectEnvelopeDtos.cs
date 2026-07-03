using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class ProjectEnvelopeDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public decimal? TargetBudget { get; set; }
    public string? FundingNote { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Enveloppe projet avec progression : engagé, restant, compteur de transactions.</summary>
public class ProjectEnvelopeProgressDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public decimal? TargetBudget { get; set; }
    public string? FundingNote { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Engagé = dépenses rattachées − remboursements rattachés (revenus).</summary>
    public decimal Spent { get; set; }
    /// <summary>TargetBudget − Spent. null si pas de cible.</summary>
    public decimal? Remaining { get; set; }
    public int TransactionCount { get; set; }
}

public class CreateProjectEnvelopeDto
{
    [Required]
    public int DashboardId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? Icon { get; set; }

    [Range(0.01, 9999999.99)]
    public decimal? TargetBudget { get; set; }

    [MaxLength(200)]
    public string? FundingNote { get; set; }
}

public class UpdateProjectEnvelopeDto
{
    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(20)]
    public string? Icon { get; set; }

    [Range(0, 9999999.99)]
    public decimal? TargetBudget { get; set; }

    [MaxLength(200)]
    public string? FundingNote { get; set; }

    public bool? IsArchived { get; set; }
}
