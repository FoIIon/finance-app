namespace FinanceApp.API.Models;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public int? UserId { get; set; }

    public User? User { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
