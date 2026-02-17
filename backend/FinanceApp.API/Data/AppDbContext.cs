using Microsoft.EntityFrameworkCore;
using FinanceApp.API.Models;

namespace FinanceApp.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<Transaction>()
            .Property(t => t.Amount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.User)
            .WithMany(u => u.Transactions)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Category)
            .WithMany(c => c.Transactions)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Category>()
            .HasOne(c => c.User)
            .WithMany(u => u.Categories)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Alimentation", Icon = "\uD83C\uDF55", Color = "#FF6384", IsDefault = true },
            new Category { Id = 2, Name = "Transport", Icon = "\uD83D\uDE97", Color = "#36A2EB", IsDefault = true },
            new Category { Id = 3, Name = "Logement", Icon = "\uD83C\uDFE0", Color = "#FFCE56", IsDefault = true },
            new Category { Id = 4, Name = "Loisirs", Icon = "\uD83C\uDFAE", Color = "#4BC0C0", IsDefault = true },
            new Category { Id = 5, Name = "Sant\u00E9", Icon = "\uD83D\uDC8A", Color = "#9966FF", IsDefault = true },
            new Category { Id = 6, Name = "\u00C9ducation", Icon = "\uD83D\uDCDA", Color = "#FF9F40", IsDefault = true },
            new Category { Id = 7, Name = "Shopping", Icon = "\uD83D\uDECD\uFE0F", Color = "#FF6384", IsDefault = true },
            new Category { Id = 8, Name = "Salaire", Icon = "\uD83D\uDCB0", Color = "#4BC0C0", IsDefault = true },
            new Category { Id = 9, Name = "Freelance", Icon = "\uD83D\uDCBB", Color = "#36A2EB", IsDefault = true },
            new Category { Id = 10, Name = "Autres", Icon = "\uD83D\uDCE6", Color = "#C9CBCF", IsDefault = true }
        );
    }
}
