using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class BudgetDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = string.Empty;
    public string CategoryColor { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Period { get; set; } = "Monthly";
    public DateTime CreatedAt { get; set; }
}

public class CreateBudgetDto
{
    [Required]
    public int DashboardId { get; set; }

    [Required]
    public int CategoryId { get; set; }

    [Required]
    [Range(0.01, 999999.99)]
    public decimal Amount { get; set; }

    [RegularExpression("^(Monthly|Yearly)$", ErrorMessage = "Period doit être 'Monthly' ou 'Yearly'.")]
    public string Period { get; set; } = "Monthly";
}

public class UpdateBudgetDto
{
    [Range(0.01, 999999.99)]
    public decimal? Amount { get; set; }

    [RegularExpression("^(Monthly|Yearly)$")]
    public string? Period { get; set; }
}

/// <summary>État d'avancement d'un budget pour une période donnée.</summary>
public class BudgetProgressDto
{
    public int BudgetId { get; set; }
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string CategoryIcon { get; set; } = string.Empty;
    public string CategoryColor { get; set; } = string.Empty;
    public decimal Budget { get; set; }
    public decimal Spent { get; set; }
    public decimal Remaining { get; set; }
    public decimal ProgressPct { get; set; }
    public string Status { get; set; } = "ok"; // ok | warning | exceeded
}
