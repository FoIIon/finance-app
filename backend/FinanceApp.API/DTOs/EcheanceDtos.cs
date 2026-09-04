using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class EcheanceDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Label { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    /// <summary>Null quand le montant n'est pas encore connu, voir IsAmountKnown.</summary>
    public decimal? Amount { get; set; }
    public bool IsAmountKnown { get; set; }
    public string? Notes { get; set; }
    /// <summary>AVenir, EnRetard ou Payee. Calculé à la lecture, jamais stocké.</summary>
    public string Status { get; set; } = string.Empty;
    public DateTime? PaidAt { get; set; }
    public int? TransactionId { get; set; }
    public List<int> DocumentIds { get; set; } = new();
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateEcheanceDto
{
    [Required]
    public int DashboardId { get; set; }

    [Required, MaxLength(200)]
    public string Label { get; set; } = string.Empty;

    [Required]
    public DateOnly DueDate { get; set; }

    [Range(0, 9999999.99)]
    public decimal? Amount { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }
}

/// <summary>Remplacement complet : un champ absent revient à null (le montant redevient inconnu).</summary>
public class UpdateEcheanceDto
{
    [Required, MaxLength(200)]
    public string Label { get; set; } = string.Empty;

    [Required]
    public DateOnly DueDate { get; set; }

    [Range(0, 9999999.99)]
    public decimal? Amount { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>Transaction qui règle l'échéance, sur un compte du dashboard. Null : détachée.</summary>
    public int? TransactionId { get; set; }
}
