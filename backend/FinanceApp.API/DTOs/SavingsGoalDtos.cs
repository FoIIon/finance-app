using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class SavingsGoalDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    /// <summary>True si CurrentAmount est dérivé des transactions de la catégorie liée (sinon valeur stockée).</summary>
    public bool IsAutoCalculated { get; set; }
    public DateTime TargetDate { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryIcon { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateSavingsGoalDto
{
    [Required]
    public int DashboardId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Icon { get; set; } = string.Empty;

    [Required, Range(0.01, 9999999.99)]
    public decimal TargetAmount { get; set; }

    [Required]
    public DateTime TargetDate { get; set; }

    public int? CategoryId { get; set; }
}

public class UpdateSavingsGoalDto
{
    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(20)]
    public string? Icon { get; set; }

    [Range(0, 9999999.99)]
    public decimal? TargetAmount { get; set; }

    [Range(0, 9999999.99)]
    public decimal? CurrentAmount { get; set; }

    public DateTime? TargetDate { get; set; }

    public int? CategoryId { get; set; }
}

/// <summary>Progression d'un objectif d'épargne avec calculs dérivés.</summary>
public class SavingsGoalProgressDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public decimal TargetAmount { get; set; }
    public decimal CurrentAmount { get; set; }
    /// <summary>True si CurrentAmount est dérivé des transactions de la catégorie liée.</summary>
    public bool IsAutoCalculated { get; set; }
    public decimal Remaining { get; set; }
    public decimal ProgressPct { get; set; }
    public DateTime TargetDate { get; set; }
    public int DaysRemaining { get; set; }
    public int MonthsRemaining { get; set; }
    /// <summary>Versement mensuel requis pour atteindre l'objectif à temps.</summary>
    public decimal RequiredMonthlySavings { get; set; }
}
