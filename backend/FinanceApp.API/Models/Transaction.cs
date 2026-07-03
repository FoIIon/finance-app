namespace FinanceApp.API.Models;

public class Transaction
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public int CategoryId { get; set; }
    public int AccountId { get; set; }
    public string? ExternalId { get; set; }
    public bool IsImported { get; set; }
    public string? CounterpartyName { get; set; }
    public bool IsExceptional { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? BankAccountId { get; set; }

    public Category Category { get; set; } = null!;
    public Account Account { get; set; } = null!;
    public BankAccount? BankAccount { get; set; }
}
