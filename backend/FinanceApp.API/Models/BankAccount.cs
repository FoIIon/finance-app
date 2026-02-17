namespace FinanceApp.API.Models;

public class BankAccount
{
    public int Id { get; set; }
    public int BankConnectionId { get; set; }
    public string ExternalAccountId { get; set; } = string.Empty;
    public string Iban { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public BankConnection BankConnection { get; set; } = null!;
}
