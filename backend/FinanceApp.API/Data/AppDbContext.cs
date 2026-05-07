using Microsoft.EntityFrameworkCore;
using FinanceApp.API.Models;

namespace FinanceApp.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<BankConnection> BankConnections => Set<BankConnection>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<CategoryRule> CategoryRules => Set<CategoryRule>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<DashboardMember> DashboardMembers => Set<DashboardMember>();
    public DbSet<DashboardAccount> DashboardAccounts => Set<DashboardAccount>();
    public DbSet<DashboardInvitation> DashboardInvitations => Set<DashboardInvitation>();
    public DbSet<RecurringTransaction> RecurringTransactions => Set<RecurringTransaction>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<SavingsGoal> SavingsGoals => Set<SavingsGoal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // User
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.EmailConfirmationToken)
            .IsUnique()
            .HasFilter("[EmailConfirmationToken] IS NOT NULL");

        // Transaction
        modelBuilder.Entity<Transaction>()
            .Property(t => t.Amount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Account)
            .WithMany(a => a.Transactions)
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.Category)
            .WithMany(c => c.Transactions)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Transaction>()
            .HasIndex(t => t.ExternalId)
            .IsUnique()
            .HasFilter("[ExternalId] IS NOT NULL");

        // Category
        modelBuilder.Entity<Category>()
            .HasOne(c => c.User)
            .WithMany(u => u.Categories)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Account
        modelBuilder.Entity<Account>()
            .HasOne(a => a.User)
            .WithMany(u => u.Accounts)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Dashboard
        modelBuilder.Entity<Dashboard>()
            .HasOne(d => d.Creator)
            .WithMany()
            .HasForeignKey(d => d.CreatorId)
            .OnDelete(DeleteBehavior.Restrict);

        // DashboardMember — clé composite
        modelBuilder.Entity<DashboardMember>()
            .HasKey(dm => new { dm.DashboardId, dm.UserId });

        modelBuilder.Entity<DashboardMember>()
            .HasOne(dm => dm.Dashboard)
            .WithMany(d => d.Members)
            .HasForeignKey(dm => dm.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DashboardMember>()
            .HasOne(dm => dm.User)
            .WithMany(u => u.DashboardMemberships)
            .HasForeignKey(dm => dm.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // DashboardAccount — clé composite
        modelBuilder.Entity<DashboardAccount>()
            .HasKey(da => new { da.DashboardId, da.AccountId });

        modelBuilder.Entity<DashboardAccount>()
            .HasOne(da => da.Dashboard)
            .WithMany(d => d.DashboardAccounts)
            .HasForeignKey(da => da.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DashboardAccount>()
            .HasOne(da => da.Account)
            .WithMany(a => a.DashboardAccounts)
            .HasForeignKey(da => da.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // DashboardInvitation
        modelBuilder.Entity<DashboardInvitation>()
            .HasIndex(di => di.Token)
            .IsUnique();

        modelBuilder.Entity<DashboardInvitation>()
            .HasOne(di => di.Dashboard)
            .WithMany()
            .HasForeignKey(di => di.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DashboardInvitation>()
            .HasOne(di => di.InvitedByUser)
            .WithMany()
            .HasForeignKey(di => di.InvitedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // BankConnection
        modelBuilder.Entity<BankConnection>()
            .HasOne(bc => bc.User)
            .WithMany()
            .HasForeignKey(bc => bc.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BankAccount>()
            .HasOne(ba => ba.BankConnection)
            .WithMany(bc => bc.BankAccounts)
            .HasForeignKey(ba => ba.BankConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.BankAccount)
            .WithMany()
            .HasForeignKey(t => t.BankAccountId)
            .OnDelete(DeleteBehavior.ClientSetNull)
            .IsRequired(false);

        // CategoryRule
        modelBuilder.Entity<CategoryRule>()
            .HasOne(cr => cr.User)
            .WithMany()
            .HasForeignKey(cr => cr.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CategoryRule>()
            .HasOne(cr => cr.Category)
            .WithMany()
            .HasForeignKey(cr => cr.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // RecurringTransaction
        modelBuilder.Entity<RecurringTransaction>()
            .Property(r => r.Amount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<RecurringTransaction>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RecurringTransaction>()
            .HasOne(r => r.Dashboard)
            .WithMany()
            .HasForeignKey(r => r.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RecurringTransaction>()
            .HasOne(r => r.Account)
            .WithMany()
            .HasForeignKey(r => r.AccountId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<RecurringTransaction>()
            .HasOne(r => r.Category)
            .WithMany()
            .HasForeignKey(r => r.CategoryId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<RecurringTransaction>()
            .HasIndex(r => r.DashboardId);

        // BankAccount
        modelBuilder.Entity<BankAccount>()
            .Property(ba => ba.RealBalance)
            .HasPrecision(18, 2);

        modelBuilder.Entity<BankAccount>()
            .Property(ba => ba.InitialBalance)
            .HasPrecision(18, 2);

        modelBuilder.Entity<BankAccount>()
            .HasOne(ba => ba.User)
            .WithMany()
            .HasForeignKey(ba => ba.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<BankAccount>()
            .HasOne(ba => ba.SourceBankAccount)
            .WithMany()
            .HasForeignKey(ba => ba.SourceBankAccountId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<BankAccount>()
            .HasOne(ba => ba.IncrementCategory)
            .WithMany()
            .HasForeignKey(ba => ba.IncrementCategoryId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // Budget
        modelBuilder.Entity<Budget>()
            .Property(b => b.Amount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Budget>()
            .HasOne(b => b.Dashboard)
            .WithMany()
            .HasForeignKey(b => b.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Budget>()
            .HasOne(b => b.Category)
            .WithMany()
            .HasForeignKey(b => b.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Budget>()
            .HasIndex(b => new { b.DashboardId, b.CategoryId, b.Period })
            .IsUnique();

        // SavingsGoal
        modelBuilder.Entity<SavingsGoal>()
            .Property(s => s.TargetAmount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<SavingsGoal>()
            .Property(s => s.CurrentAmount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<SavingsGoal>()
            .HasOne(s => s.Dashboard)
            .WithMany()
            .HasForeignKey(s => s.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SavingsGoal>()
            .HasOne(s => s.Category)
            .WithMany()
            .HasForeignKey(s => s.CategoryId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

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
