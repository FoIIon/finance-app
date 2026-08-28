using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class CategoryRuleDto
{
    public int Id { get; set; }
    public string Keyword { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public bool MarkAsFixed { get; set; }
    /// <summary>Les dépenses carte Trade Republic matchées comptent au Perso, pas au Commun. Voir PersoScopeRouter.</summary>
    public bool RouteToPerso { get; set; }
}

public class CreateCategoryRuleDto
{
    [Required, MaxLength(200)]
    public string Keyword { get; set; } = string.Empty;

    [Required]
    public int CategoryId { get; set; }

    public bool MarkAsFixed { get; set; }

    public bool RouteToPerso { get; set; }
}

public class UpdateCategoryRuleDto
{
    [MaxLength(200)]
    public string? Keyword { get; set; }

    public int? CategoryId { get; set; }

    public bool? MarkAsFixed { get; set; }

    public bool? RouteToPerso { get; set; }
}
