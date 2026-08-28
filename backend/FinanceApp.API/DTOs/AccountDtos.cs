using System.ComponentModel.DataAnnotations;

namespace FinanceApp.API.DTOs;

public class AccountDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    /// <summary>Compte logique Perso, cible du routage perso/commun. Un seul par utilisateur.</summary>
    public bool IsPersonalScope { get; set; }
}

public class CreateAccountDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;
}
