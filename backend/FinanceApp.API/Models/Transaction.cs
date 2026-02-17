namespace FinanceApp.API.Models;

public class Transaction
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public TransactionType Type { get; set; }
    public int CategoryId { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Category Category { get; set; } = null!;
    public User User { get; set; } = null!;
}
