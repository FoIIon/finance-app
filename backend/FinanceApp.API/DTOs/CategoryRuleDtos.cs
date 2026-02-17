using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class CategoryRuleDto
{
    public int Id { get; set; }
    public string Keyword { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
}

public class CreateCategoryRuleDto
{
    [Required, MaxLength(200)]
    public string Keyword { get; set; } = string.Empty;

    [Required]
    public int CategoryId { get; set; }
}

public class UpdateCategoryRuleDto
{
    [MaxLength(200)]
    public string? Keyword { get; set; }

    public int? CategoryId { get; set; }
}
