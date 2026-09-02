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
    public DbSet<ProjectEnvelope> ProjectEnvelopes => Set<ProjectEnvelope>();
    public DbSet<ShoppingItem> ShoppingItems => Set<ShoppingItem>();
    public DbSet<Investment> Investments => Set<Investment>();
    public DbSet<InvestmentValuation> InvestmentValuations => Set<InvestmentValuation>();
    public DbSet<InvestmentMovement> InvestmentMovements => Set<InvestmentMovement>();
    public DbSet<PortfolioValuation> PortfolioValuations => Set<PortfolioValuation>();
    public DbSet<Loan> Loans => Set<Loan>();

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

        // Trace de la catégorie d'origine avant correction manuelle. Sans collection en face (elle
        // n'intéresse personne) et détachée à la suppression d'une catégorie : perdre l'origine est
        // sans conséquence, bloquer la suppression d'une catégorie pour ça n'aurait aucun sens.
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.CategoryBeforeManual)
            .WithMany()
            .HasForeignKey(t => t.CategoryBeforeManualId)
            .OnDelete(DeleteBehavior.SetNull);

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

        // Un seul compte logique Perso par utilisateur (PersoAccounts). L'index tranche la course
        // entre une sync manuelle et la boucle de fond, qui passaient toutes deux le check-then-insert.
        // L'index de la FK, déclaré explicitement : sans ça EF le juge couvert par l'index filtré
        // ci-dessous et le supprime, alors qu'un index filtré ne sert pas les lignes ordinaires.
        modelBuilder.Entity<Account>()
            .HasIndex(a => a.UserId)
            .HasDatabaseName("IX_Accounts_UserId");

        modelBuilder.Entity<Account>()
            .HasIndex(a => new { a.UserId, a.IsPersonalScope })
            .IsUnique()
            .HasFilter("[IsPersonalScope] = 1")
            .HasDatabaseName("IX_Accounts_UserId_PersonalScope");

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

        // Enum stocke sous son nom : les lignes existantes portaient deja ces mots en clair.
        modelBuilder.Entity<BankConnection>()
            .Property(bc => bc.Provider)
            .HasConversion<string>();

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
            .Property(r => r.Type)
            .HasConversion<string>();

        modelBuilder.Entity<RecurringTransaction>()
            .Property(r => r.Frequency)
            .HasConversion<string>();

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

        // ProjectEnvelope
        modelBuilder.Entity<ProjectEnvelope>()
            .Property(p => p.TargetBudget)
            .HasPrecision(18, 2);

        modelBuilder.Entity<ProjectEnvelope>()
            .HasOne(p => p.Dashboard)
            .WithMany()
            .HasForeignKey(p => p.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        // Transaction → ProjectEnvelope (rattachement d'une dépense, détachée si l'enveloppe est supprimée)
        modelBuilder.Entity<Transaction>()
            .HasOne(t => t.ProjectEnvelope)
            .WithMany()
            .HasForeignKey(t => t.ProjectEnvelopeId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // ShoppingItem
        modelBuilder.Entity<ShoppingItem>()
            .Property(s => s.EstimatedCost)
            .HasPrecision(18, 2);

        modelBuilder.Entity<ShoppingItem>()
            .HasOne(s => s.Dashboard)
            .WithMany()
            .HasForeignKey(s => s.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        // Investment
        // Précision explicite obligatoire : SQLite ne porte pas de précision native sur decimal,
        // et les quantités fractionnaires (parts Trade Republic) descendent à six décimales.
        modelBuilder.Entity<Investment>()
            .Property(i => i.Quantity)
            .HasPrecision(18, 6);

        modelBuilder.Entity<Investment>()
            .Property(i => i.CostBasis)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Investment>()
            .HasOne(i => i.Dashboard)
            .WithMany()
            .HasForeignKey(i => i.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Investment>()
            .HasIndex(i => i.DashboardId);

        // Loan
        modelBuilder.Entity<Loan>()
            .Property(l => l.InitialPrincipal)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Loan>()
            .Property(l => l.MonthlyPayment)
            .HasPrecision(18, 2);

        modelBuilder.Entity<Loan>()
            .Property(l => l.AnchorPrincipal)
            .HasPrecision(18, 2);

        // Le taux mensuel se déduit du taux annuel : quatre décimales de trop peu et le
        // solde reconstruit dérive de plusieurs euros sur la durée du prêt.
        modelBuilder.Entity<Loan>()
            .Property(l => l.AnnualRatePercent)
            .HasPrecision(9, 6);

        modelBuilder.Entity<Loan>()
            .HasOne(l => l.Dashboard)
            .WithMany()
            .HasForeignKey(l => l.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Loan>()
            .HasIndex(l => l.DashboardId);

        // PortfolioValuation : une valeur par dashboard et par jour. Un ré-import remplace la
        // valeur du jour, Trade Republic fait foi.
        modelBuilder.Entity<PortfolioValuation>()
            .Property(v => v.MarketValue)
            .HasPrecision(18, 2);

        modelBuilder.Entity<PortfolioValuation>()
            .Property(v => v.Invested)
            .HasPrecision(18, 2);

        modelBuilder.Entity<PortfolioValuation>()
            .HasOne(v => v.Dashboard)
            .WithMany()
            .HasForeignKey(v => v.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PortfolioValuation>()
            .HasIndex(v => new { v.DashboardId, v.AsOf })
            .IsUnique();

        // InvestmentValuation
        modelBuilder.Entity<InvestmentValuation>()
            .Property(v => v.MarketValue)
            .HasPrecision(18, 2);

        modelBuilder.Entity<InvestmentValuation>()
            .Property(v => v.UnitPrice)
            .HasPrecision(18, 6);

        modelBuilder.Entity<InvestmentValuation>()
            .HasOne(v => v.Investment)
            .WithMany(i => i.Valuations)
            .HasForeignKey(v => v.InvestmentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Une seule valorisation par ligne et par date : une correction remplace la valeur
        // du jour, elle ne crée pas un doublon qui fausserait la courbe.
        modelBuilder.Entity<InvestmentValuation>()
            .HasIndex(v => new { v.InvestmentId, v.AsOf })
            .IsUnique();

        // InvestmentMovement
        // Table créée dès maintenant pour n'avoir qu'une migration. Non alimentée au lot 1.
        modelBuilder.Entity<InvestmentMovement>()
            .Property(m => m.Quantity)
            .HasPrecision(18, 6);

        modelBuilder.Entity<InvestmentMovement>()
            .Property(m => m.UnitPrice)
            .HasPrecision(18, 6);

        modelBuilder.Entity<InvestmentMovement>()
            .Property(m => m.Amount)
            .HasPrecision(18, 2);

        modelBuilder.Entity<InvestmentMovement>()
            .HasOne(m => m.Investment)
            .WithMany(i => i.Movements)
            .HasForeignKey(m => m.InvestmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<InvestmentMovement>()
            .HasIndex(m => m.InvestmentId);

        // Déduplication des imports courtier. Filtré pour tolérer plusieurs mouvements
        // saisis à la main sans identifiant externe.
        modelBuilder.Entity<InvestmentMovement>()
            .HasIndex(m => m.ExternalId)
            .IsUnique()
            .HasFilter("[ExternalId] IS NOT NULL");

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
