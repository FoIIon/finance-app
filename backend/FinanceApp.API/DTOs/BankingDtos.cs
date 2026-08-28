using System.ComponentModel.DataAnnotations;
using FinanceApp.API.Models;

namespace FinanceApp.API.DTOs;

public class InstitutionDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Logo { get; set; } = string.Empty;
    public List<string> Countries { get; set; } = new();
}

public class ConnectBankRequest
{
    [Required]
    public string InstitutionId { get; set; } = string.Empty;

    [Required]
    public string InstitutionName { get; set; } = string.Empty;

    public string InstitutionLogo { get; set; } = string.Empty;
}

public class ConnectBankResponse
{
    public string AuthorizationUrl { get; set; } = string.Empty;
}

public class BankConnectionDto
{
    public int Id { get; set; }
    public string InstitutionId { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string InstitutionLogo { get; set; } = string.Empty;
    public BankConnectionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public List<BankAccountDto> Accounts { get; set; } = new();
}

public class BankAccountDto
{
    public int Id { get; set; }
    public string ExternalAccountId { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    /// <summary>Compte personnel : toutes ses transactions comptent au Perso. Voir PersoScopeRouter.</summary>
    public bool IsPersonal { get; set; }
}

/// <summary>PATCH partiel : seuls les champs renseignés sont modifiés.</summary>
public class UpdateBankAccountDto
{
    public bool? IsActive { get; set; }

    public bool? IsPersonal { get; set; }
}

public class TradeRepublicLoginRequest
{
    [Required]
    [RegularExpression(@"^\+[1-9]\d{6,14}$", ErrorMessage = "Format de téléphone invalide")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(4, MinimumLength = 4)]
    [RegularExpression(@"^\d{4}$", ErrorMessage = "Le PIN doit être composé de 4 chiffres")]
    public string Pin { get; set; } = string.Empty;
}

public class TradeRepublicLoginResponse
{
    public int ConnectionId { get; set; }
}

public class TradeRepublicVerifyRequest
{
    [Required]
    public int ConnectionId { get; set; }

    // Flux v2 (2026) : l'approbation se fait dans l'app mobile, il n'y a plus de code.
    // Champ conservé pour compatibilité, ignoré par le backend.
    public string? Code { get; set; }
}

public class ManualAccountDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public decimal InitialBalance { get; set; }
    public DateTime InitialBalanceDate { get; set; }
    public int? SourceBankAccountId { get; set; }
    public string? SourceBankAccountName { get; set; }
    public int? IncrementCategoryId { get; set; }
    public string? IncrementCategoryName { get; set; }
}

public class CreateManualAccountDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(34)]
    public string? Iban { get; set; }

    [MaxLength(3)]
    public string? Currency { get; set; }

    [Required]
    public decimal InitialBalance { get; set; }

    public DateTime? InitialBalanceDate { get; set; }

    /// <summary>BankAccountId source : compte courant qui alimente ce compte (ex: Argenta courant pour Argenta épargne).</summary>
    public int? SourceBankAccountId { get; set; }

    /// <summary>Catégorie qui marque les transferts (ex: Épargne).</summary>
    public int? IncrementCategoryId { get; set; }
}

public class UpdateManualAccountDto
{
    [MaxLength(100)]
    public string? Name { get; set; }
    [MaxLength(34)]
    public string? Iban { get; set; }
    public decimal? InitialBalance { get; set; }
    public DateTime? InitialBalanceDate { get; set; }
    public int? SourceBankAccountId { get; set; }
    public int? IncrementCategoryId { get; set; }
}
