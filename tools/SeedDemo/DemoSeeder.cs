using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.SeedDemo;

public record SeedSummary(int Users, int Dashboards, int Accounts, int BankAccounts, int Transactions, int RecurringTransactions);

/// <summary>
/// Remplit une base de dev avec un ménage de démonstration : deux utilisateurs, un dashboard commun,
/// trois mois de transactions plausibles pour une famille belge. Rejouable : toute donnée de démo est
/// reconnaissable (emails en @demo.invalid, ExternalId en demo-n) et purgée avant chaque passe. Le
/// générateur est à graine fixe, deux passes le même jour donnent exactement le même contenu.
/// </summary>
public static class DemoSeeder
{
    public const string EmailDomain = "@demo.invalid";
    public const string SebEmail = "seb" + EmailDomain;
    public const string AudreyEmail = "audrey" + EmailDomain;
    public const string Password = "Demo-1234!";
    public const string ExternalIdPrefix = "demo-";
    public const string CommonDashboardName = "Commun démo";
    public const int DefaultSeed = 20260904;

    // IBAN de test à clé mod 97 valide, jamais ceux d'un vrai titulaire.
    public const string SebIban = "BE68539007547034";
    public const string AudreyIban = "BE62510007547061";

    private static readonly string[] RequiredCategories =
        { "Alimentation", "Transport", "Logement", "Loisirs", "Santé", "Éducation", "Shopping", "Salaire", "Freelance", "Autres" };

    public static async Task<SeedSummary> RunAsync(AppDbContext ctx, DateOnly today, int seed = DefaultSeed)
    {
        var categories = await ctx.Categories
            .Where(c => c.IsDefault && c.UserId == null)
            .ToDictionaryAsync(c => c.Name, c => c.Id);
        var missing = RequiredCategories.Where(n => !categories.ContainsKey(n)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Catégories par défaut absentes : {string.Join(", ", missing)}. La base n'est pas migrée.");

        await using var transaction = await ctx.Database.BeginTransactionAsync();

        await PurgeAsync(ctx);

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(Password);
        var createdAt = today.AddMonths(-3).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var seb = new User { Email = SebEmail, PasswordHash = passwordHash, EmailConfirmed = true, CreatedAt = createdAt };
        var audrey = new User { Email = AudreyEmail, PasswordHash = passwordHash, EmailConfirmed = true, CreatedAt = createdAt };
        ctx.Users.AddRange(seb, audrey);
        await ctx.SaveChangesAsync();

        // Même forme qu'à l'inscription : un compte principal et un dashboard Personnel par personne,
        // plus le compte Perso de Sébastien (celui que PersoAccounts crée à la demande).
        var sebPrimary = new Account { Name = "Compte principal", UserId = seb.Id, IsPrimary = true, CreatedAt = createdAt };
        var sebPerso = new Account { Name = "Perso", UserId = seb.Id, IsPersonalScope = true, CreatedAt = createdAt.AddMinutes(1) };
        var audreyPrimary = new Account { Name = "Compte principal", UserId = audrey.Id, IsPrimary = true, CreatedAt = createdAt };
        ctx.Accounts.AddRange(sebPrimary, sebPerso, audreyPrimary);
        await ctx.SaveChangesAsync();

        var sebPersonal = new Dashboard { Name = "Personnel", CreatorId = seb.Id, IsPersonal = true, CreatedAt = createdAt };
        var audreyPersonal = new Dashboard { Name = "Personnel", CreatorId = audrey.Id, IsPersonal = true, CreatedAt = createdAt };
        var common = new Dashboard { Name = CommonDashboardName, CreatorId = seb.Id, CreatedAt = createdAt.AddMinutes(2) };
        ctx.Dashboards.AddRange(sebPersonal, audreyPersonal, common);
        await ctx.SaveChangesAsync();

        ctx.DashboardMembers.AddRange(
            new DashboardMember { DashboardId = sebPersonal.Id, UserId = seb.Id, JoinedAt = createdAt },
            new DashboardMember { DashboardId = audreyPersonal.Id, UserId = audrey.Id, JoinedAt = createdAt },
            new DashboardMember { DashboardId = common.Id, UserId = seb.Id, JoinedAt = createdAt },
            new DashboardMember { DashboardId = common.Id, UserId = audrey.Id, JoinedAt = createdAt.AddMinutes(5) });

        ctx.DashboardAccounts.AddRange(
            new DashboardAccount { DashboardId = sebPersonal.Id, AccountId = sebPrimary.Id },
            new DashboardAccount { DashboardId = sebPersonal.Id, AccountId = sebPerso.Id },
            new DashboardAccount { DashboardId = audreyPersonal.Id, AccountId = audreyPrimary.Id },
            new DashboardAccount { DashboardId = common.Id, AccountId = sebPrimary.Id },
            new DashboardAccount { DashboardId = common.Id, AccountId = audreyPrimary.Id });

        // Un compte bancaire physique par compte principal, porté par une connexion Manual : la sync de
        // fond la saute sans appel externe (BankSyncService), donc aucun risque de toucher un quota
        // GoCardless. Le compte lui-même n'est pas manuel, pour que les soldes et la couverture
        // bancaire (période « Tout ») se comportent comme avec une vraie banque.
        var sebConnection = DemoConnection(seb.Id, createdAt);
        var audreyConnection = DemoConnection(audrey.Id, createdAt);
        ctx.BankConnections.AddRange(sebConnection, audreyConnection);
        await ctx.SaveChangesAsync();

        var sebBank = DemoBankAccount(sebConnection.Id, seb.Id, "demo-account-seb", SebIban, "Compte à vue démo Seb");
        var audreyBank = DemoBankAccount(audreyConnection.Id, audrey.Id, "demo-account-audrey", AudreyIban, "Compte à vue démo Audrey");
        ctx.BankAccounts.AddRange(sebBank, audreyBank);
        await ctx.SaveChangesAsync();

        var bankAccountByLogical = new Dictionary<int, int>
        {
            [sebPrimary.Id] = sebBank.Id,
            [audreyPrimary.Id] = audreyBank.Id,
        };
        var generator = new TransactionGenerator(new Random(seed), today, categories,
            sebPrimary.Id, sebPerso.Id, audreyPrimary.Id, bankAccountByLogical);
        var lines = generator.Generate();
        ctx.Transactions.AddRange(lines);

        // Solde booké = solde d'ouverture plausible + net des lignes, daté d'aujourd'hui, pour que la
        // page des comptes et la courbe rétrospective aient quelque chose à montrer.
        var asOf = today.ToDateTime(new TimeOnly(7, 0), DateTimeKind.Utc);
        SetBalances(sebBank, 2350.18m, lines.Where(t => t.BankAccountId == sebBank.Id), asOf);
        SetBalances(audreyBank, 1480.55m, lines.Where(t => t.BankAccountId == audreyBank.Id), asOf);

        var firstMonth = today.AddMonths(-3);
        var startOfWindow = new DateOnly(firstMonth.Year, firstMonth.Month, 1);
        ctx.RecurringTransactions.AddRange(
            new RecurringTransaction
            {
                UserId = seb.Id, DashboardId = common.Id, AccountId = sebPrimary.Id, CategoryId = categories["Salaire"],
                Description = "Salaire Seb", Amount = TransactionGenerator.SebSalary, Type = TransactionType.Income,
                Frequency = RecurringFrequency.Monthly, DayOfMonth = TransactionGenerator.SalaryDay,
                StartDate = startOfWindow, ProvisionAtMonthStart = true, CreatedAt = createdAt, UpdatedAt = createdAt
            },
            new RecurringTransaction
            {
                UserId = seb.Id, DashboardId = common.Id, AccountId = sebPrimary.Id, CategoryId = categories["Logement"],
                Description = "Loyer", Amount = TransactionGenerator.Rent, Type = TransactionType.Expense,
                Frequency = RecurringFrequency.Monthly, DayOfMonth = 1,
                StartDate = startOfWindow, CreatedAt = createdAt, UpdatedAt = createdAt
            },
            new RecurringTransaction
            {
                UserId = seb.Id, DashboardId = common.Id, AccountId = sebPrimary.Id, CategoryId = categories["Logement"],
                Description = "Énergie (gaz + électricité)", Amount = TransactionGenerator.Energy, Type = TransactionType.Expense,
                Frequency = RecurringFrequency.Monthly, DayOfMonth = TransactionGenerator.EnergyDay,
                StartDate = startOfWindow, CreatedAt = createdAt, UpdatedAt = createdAt
            });

        await ctx.SaveChangesAsync();
        await transaction.CommitAsync();

        return await CountAsync(ctx);
    }

    private static BankConnection DemoConnection(int userId, DateTime createdAt) => new()
    {
        UserId = userId,
        Provider = BankProvider.Manual,
        Status = BankConnectionStatus.Linked,
        InstitutionId = "DEMO",
        InstitutionName = "Banque démo",
        InstitutionLogo = string.Empty,
        RequisitionId = $"demo-requisition-{userId}",
        Reference = $"demo-reference-{userId}",
        CreatedAt = createdAt,
        LastSyncAt = createdAt,
    };

    private static BankAccount DemoBankAccount(int connectionId, int userId, string externalId, string iban, string name) => new()
    {
        BankConnectionId = connectionId,
        UserId = userId,
        ExternalAccountId = externalId,
        Iban = iban,
        OwnerName = "Titulaire démo",
        AccountName = name,
        Currency = "EUR",
        IsActive = true,
        IsManual = false,
        IsPersonal = false,
    };

    private static void SetBalances(BankAccount bank, decimal opening, IEnumerable<Transaction> lines, DateTime asOf)
    {
        var net = lines.Sum(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount);
        bank.BookedBalance = opening + net;
        bank.RealBalance = bank.BookedBalance;
        bank.BalanceUpdatedAt = asOf;
    }

    /// <summary>
    /// Efface tout ce qui appartient aux utilisateurs de démo, dans l'ordre que les clés étrangères
    /// imposent. Les autres tables rattachées à un dashboard (budgets, enveloppes, prêts, invitations…)
    /// suivent en cascade au niveau SQLite si un développeur en a créé depuis l'interface.
    /// </summary>
    public static async Task PurgeAsync(AppDbContext ctx)
    {
        var userIds = await ctx.Users.Where(u => u.Email.EndsWith(EmailDomain)).Select(u => u.Id).ToListAsync();
        var dashboardIds = await ctx.Dashboards.Where(d => userIds.Contains(d.CreatorId)).Select(d => d.Id).ToListAsync();
        var accountIds = await ctx.Accounts.Where(a => userIds.Contains(a.UserId)).Select(a => a.Id).ToListAsync();
        var connectionIds = await ctx.BankConnections.Where(bc => userIds.Contains(bc.UserId)).Select(bc => bc.Id).ToListAsync();

        await ctx.Transactions
            .Where(t => accountIds.Contains(t.AccountId) || (t.ExternalId != null && t.ExternalId.StartsWith(ExternalIdPrefix)))
            .ExecuteDeleteAsync();
        await ctx.BankAccounts
            .Where(ba => (ba.BankConnectionId != null && connectionIds.Contains(ba.BankConnectionId.Value))
                         || (ba.UserId != null && userIds.Contains(ba.UserId.Value)))
            .ExecuteDeleteAsync();
        await ctx.BankConnections.Where(bc => connectionIds.Contains(bc.Id)).ExecuteDeleteAsync();
        await ctx.DashboardAccounts
            .Where(da => dashboardIds.Contains(da.DashboardId) || accountIds.Contains(da.AccountId))
            .ExecuteDeleteAsync();
        await ctx.DashboardMembers
            .Where(dm => dashboardIds.Contains(dm.DashboardId) || userIds.Contains(dm.UserId))
            .ExecuteDeleteAsync();
        await ctx.RecurringTransactions
            .Where(r => dashboardIds.Contains(r.DashboardId) || userIds.Contains(r.UserId))
            .ExecuteDeleteAsync();
        await ctx.Accounts.Where(a => userIds.Contains(a.UserId)).ExecuteDeleteAsync();
        await ctx.Dashboards.Where(d => userIds.Contains(d.CreatorId)).ExecuteDeleteAsync();
        // Règles puis catégories custom, explicitement : CategoryRule.Category est en Restrict et SQLite
        // l'évalue pendant la cascade depuis User, une catégorie créée dans l'UI de démo avec une règle
        // dessus ferait échouer la purge.
        await ctx.CategoryRules.Where(cr => userIds.Contains(cr.UserId)).ExecuteDeleteAsync();
        await ctx.Categories.Where(c => c.UserId != null && userIds.Contains(c.UserId.Value)).ExecuteDeleteAsync();
        await ctx.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
    }

    public static async Task<SeedSummary> CountAsync(AppDbContext ctx)
    {
        var userIds = await ctx.Users.Where(u => u.Email.EndsWith(EmailDomain)).Select(u => u.Id).ToListAsync();
        return new SeedSummary(
            userIds.Count,
            await ctx.Dashboards.CountAsync(d => userIds.Contains(d.CreatorId)),
            await ctx.Accounts.CountAsync(a => userIds.Contains(a.UserId)),
            await ctx.BankAccounts.CountAsync(ba => ba.UserId != null && userIds.Contains(ba.UserId.Value)),
            await ctx.Transactions.CountAsync(t => t.ExternalId != null && t.ExternalId.StartsWith(ExternalIdPrefix)),
            await ctx.RecurringTransactions.CountAsync(r => userIds.Contains(r.UserId)));
    }
}
