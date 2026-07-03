using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class ShoppingItemDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal? EstimatedCost { get; set; }
    public bool IsDone { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateShoppingItemDto
{
    [Required]
    public int DashboardId { get; set; }

    [Required, MaxLength(200)]
    public string Label { get; set; } = string.Empty;

    [Range(0.01, 9999999.99)]
    public decimal? EstimatedCost { get; set; }
}

public class UpdateShoppingItemDto
{
    [MaxLength(200)]
    public string? Label { get; set; }

    [Range(0, 9999999.99)]
    public decimal? EstimatedCost { get; set; }

    public bool? IsDone { get; set; }
}
