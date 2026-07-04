using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class RecurringTransactionDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public int? AccountId { get; set; }
    public string? AccountName { get; set; }
    public int? CategoryId { get; set; }
    public string? CategoryName { get; set; }
    public string? CategoryIcon { get; set; }
    public string? CategoryColor { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Frequency { get; set; } = string.Empty;
    public int? DayOfMonth { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool IsActive { get; set; }
    public bool ProvisionAtMonthStart { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateRecurringTransactionDto
{
    [Required]
    public int DashboardId { get; set; }

    public int? AccountId { get; set; }
    public int? CategoryId { get; set; }

    [Required, MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [Range(0.01, 999999999.99)]
    public decimal Amount { get; set; }

    [Required]
    [RegularExpression("^(Income|Expense)$", ErrorMessage = "Type doit être 'Income' ou 'Expense'.")]
    public string Type { get; set; } = string.Empty;

    [Required]
    [RegularExpression("^(Weekly|Monthly|Yearly)$", ErrorMessage = "Frequency doit être 'Weekly', 'Monthly' ou 'Yearly'.")]
    public string Frequency { get; set; } = string.Empty;

    [Range(1, 31)]
    public int? DayOfMonth { get; set; }

    [Required]
    public DateOnly StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public bool ProvisionAtMonthStart { get; set; }
}

public class UpdateRecurringTransactionDto
{
    public int? AccountId { get; set; }
    public int? CategoryId { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [Range(0.01, 999999999.99)]
    public decimal? Amount { get; set; }

    [RegularExpression("^(Income|Expense)$", ErrorMessage = "Type doit être 'Income' ou 'Expense'.")]
    public string? Type { get; set; }

    [RegularExpression("^(Weekly|Monthly|Yearly)$", ErrorMessage = "Frequency doit être 'Weekly', 'Monthly' ou 'Yearly'.")]
    public string? Frequency { get; set; }

    [Range(1, 31)]
    public int? DayOfMonth { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool? IsActive { get; set; }
    public bool? ProvisionAtMonthStart { get; set; }
}
