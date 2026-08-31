using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    /// <summary>Catégorie de transfert : l'argent change de compte sans être consommé (épargne, titres).</summary>
    public bool IsTransfer { get; set; }
    /// <summary>Sortie du bilan mensuel : balayage du compte joint, virements entre comptes suivis.</summary>
    public bool ExcludeFromMonthlyReport { get; set; }
}

public class CreateCategoryDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    public string Icon { get; set; } = string.Empty;

    [Required, MaxLength(9)]
    public string Color { get; set; } = string.Empty;
}

public class UpdateCategoryDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(10)]
    public string Icon { get; set; } = string.Empty;

    [Required, MaxLength(9)]
    public string Color { get; set; } = string.Empty;
}
