namespace FinanceApp.API.Models;

public class CategoryRule
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Keyword { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    /// <summary>Les transactions matchées sont marquées charge fixe (Transaction.IsFixed).</summary>
    public bool MarkAsFixed { get; set; }

    public User User { get; set; } = null!;
    public Category Category { get; set; } = null!;
}
