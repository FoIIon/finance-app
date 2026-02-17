namespace FinanceApp.API.Models;

public class BankConnection
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string InstitutionId { get; set; } = string.Empty;
    public string InstitutionName { get; set; } = string.Empty;
    public string InstitutionLogo { get; set; } = string.Empty;
    public string RequisitionId { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public BankConnectionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastSyncAt { get; set; }

    public User User { get; set; } = null!;
    public ICollection<BankAccount> BankAccounts { get; set; } = new List<BankAccount>();
}
