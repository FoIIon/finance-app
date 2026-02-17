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
}

public class UpdateBankAccountDto
{
    [Required]
    public bool IsActive { get; set; }
}
