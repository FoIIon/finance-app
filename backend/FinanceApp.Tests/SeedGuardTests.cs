using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.SeedDemo;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Les verrous de tools/SeedDemo : chacun refuse ce qu'il vise, un chemin de dev ordinaire passe.
/// Puis le seed lui-même sur une base SQLite en mémoire, le vrai schéma : deux passes consécutives
/// laissent exactement le même contenu, c'est ce qui rend l'outil rejouable sans rien accumuler.
/// </summary>
public class SeedGuardTests
{
    private const string DevMachine = "DESKTOP-DEV";

    [Fact]
    public void Refuse_SansChemin_RendLUsage()
    {
        var reason = SeedGuard.Refuse(null, "Development", DevMachine);
        Assert.NotNull(reason);
        Assert.Contains("--db", reason);
        Assert.Contains(SeedGuard.Usage, reason);
    }

    [Fact]
    public void Refuse_CheminVide_RendLUsage()
    {
        Assert.NotNull(SeedGuard.Refuse("   ", null, DevMachine));
    }

    [Theory]
    [InlineData("/home/admin/finance-app/data/finance.db")]
    [InlineData("C:\\pi\\finance-app\\data\\finance.db")]
    [InlineData("\\\\raspberrypi5\\admin\\Finance-App\\Data\\finance.db")]
    [InlineData("./mount/FINANCE-APP//DATA/x.db")]
    public void Refuse_LeDossierDeDonneesDeProd_QuelleQueSoitLaForme(string path)
    {
        var reason = SeedGuard.Refuse(path, "Development", DevMachine);
        Assert.NotNull(reason);
        Assert.Contains("finance-app/data", reason);
    }

    [Theory]
    [InlineData("/home/admin/autre.db")]
    [InlineData("/home/seb/dev/finance.db")]
    [InlineData("\\home\\admin\\finance.db")]
    public void Refuse_ToutCheminSousHome(string path)
    {
        var reason = SeedGuard.Refuse(path, null, DevMachine);
        Assert.NotNull(reason);
        Assert.Contains("/home", reason);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData(" PRODUCTION ")]
    public void Refuse_EnvironnementProduction(string environment)
    {
        var reason = SeedGuard.Refuse("C:\\dev\\finance.db", environment, DevMachine);
        Assert.NotNull(reason);
        Assert.Contains("Production", reason);
    }

    [Theory]
    [InlineData("raspberrypi5")]
    [InlineData("RaspberryPi5")]
    [InlineData("RASPBERRYPI5")]
    public void Refuse_LaMachineDeProd(string machine)
    {
        var reason = SeedGuard.Refuse("C:\\dev\\finance.db", "Development", machine);
        Assert.NotNull(reason);
        Assert.Contains(machine, reason);
    }

    [Theory]
    [InlineData("C:\\Users\\seb\\dev\\finance-app\\backend\\FinanceApp.API\\finance.db", "Development")]
    [InlineData("./finance.db", null)]
    [InlineData("/tmp/seed-test.db", "Staging")]
    [InlineData("D:\\data\\finance-app-demo.db", "Development")]
    public void Accepte_UneBaseDeDevOrdinaire(string path, string? environment)
    {
        Assert.Null(SeedGuard.Refuse(path, environment, DevMachine));
    }

    [Fact]
    public void Accepte_UnCheminQuiContientHomeSansEnCommencer()
    {
        // « home » dans un nom de dossier n'est pas /home. Le verrou vise la racine du Pi, pas le mot.
        Assert.Null(SeedGuard.Refuse("C:\\dev\\home-budget\\finance.db", "Development", DevMachine));
    }

    // ----- Le seed complet, deux fois, sur le vrai schéma --------------------------------------------

    [Fact]
    public async Task Seed_DeuxPasses_LaissentLeMemeContenu()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using (var ctx = new AppDbContext(options))
            ctx.Database.EnsureCreated();

        var today = new DateOnly(2026, 9, 4);

        SeedSummary first;
        List<(string ExternalId, decimal Amount, DateTime Date, string Description, int CategoryId, bool IsFixed, bool IsRefund, bool IsExceptional)> firstLines;
        using (var ctx = new AppDbContext(options))
        {
            first = await DemoSeeder.RunAsync(ctx, today);
            firstLines = await Snapshot(ctx);
        }

        SeedSummary second;
        List<(string, decimal, DateTime, string, int, bool, bool, bool)> secondLines;
        using (var ctx = new AppDbContext(options))
        {
            second = await DemoSeeder.RunAsync(ctx, today);
            secondLines = await Snapshot(ctx);
        }

        Assert.Equal(2, first.Users);
        Assert.Equal(3, first.Dashboards);
        Assert.Equal(3, first.Accounts);
        Assert.Equal(2, first.BankAccounts);
        Assert.Equal(3, first.RecurringTransactions);
        Assert.InRange(first.Transactions, 120, 200);

        Assert.Equal(first, second);
        Assert.Equal(firstLines, secondLines);

        // Rien ne s'accumule hors du périmètre démo non plus : les tables globales gardent leur taille.
        using var check = new AppDbContext(options);
        Assert.Equal(2, await check.Users.CountAsync());
        Assert.Equal(3, await check.Dashboards.CountAsync());
        Assert.Equal(3, await check.Accounts.CountAsync());
        Assert.Equal(2, await check.BankConnections.CountAsync());
        Assert.Equal(2, await check.BankAccounts.CountAsync());
        Assert.Equal(first.Transactions, await check.Transactions.CountAsync());
        Assert.Equal(5, await check.DashboardAccounts.CountAsync());
        Assert.Equal(4, await check.DashboardMembers.CountAsync());
    }

    [Fact]
    public async Task Seed_LaGraineChangeLeTirage_PasLaForme()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using (var ctx = new AppDbContext(options))
            ctx.Database.EnsureCreated();

        var today = new DateOnly(2026, 9, 4);
        List<(string, decimal, DateTime, string, int, bool, bool, bool)> seed42, seed42Again, seed43;
        using (var ctx = new AppDbContext(options)) { await DemoSeeder.RunAsync(ctx, today, seed: 42); seed42 = await Snapshot(ctx); }
        using (var ctx = new AppDbContext(options)) { await DemoSeeder.RunAsync(ctx, today, seed: 42); seed42Again = await Snapshot(ctx); }
        using (var ctx = new AppDbContext(options)) { await DemoSeeder.RunAsync(ctx, today, seed: 43); seed43 = await Snapshot(ctx); }

        Assert.Equal(seed42, seed42Again);
        Assert.NotEqual(seed42, seed43);
        // Les postes fixes ne bougent pas avec la graine, seuls les montants et jours tirés au sort.
        Assert.Equal(seed42.Count(l => l.Item4 == "Salaire"), seed43.Count(l => l.Item4 == "Salaire"));
        Assert.Equal(seed42.Count(l => l.Item4 == "Loyer maison"), seed43.Count(l => l.Item4 == "Loyer maison"));
    }

    [Fact]
    public async Task Seed_ProduitLeMenageAttendu()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using (var ctx = new AppDbContext(options))
            ctx.Database.EnsureCreated();

        using var seeded = new AppDbContext(options);
        await DemoSeeder.RunAsync(seeded, new DateOnly(2026, 9, 4));

        var seb = await seeded.Users.SingleAsync(u => u.Email == DemoSeeder.SebEmail);
        var audrey = await seeded.Users.SingleAsync(u => u.Email == DemoSeeder.AudreyEmail);
        Assert.True(seb.EmailConfirmed);
        Assert.True(audrey.EmailConfirmed);
        Assert.True(BCrypt.Net.BCrypt.Verify(DemoSeeder.Password, seb.PasswordHash));

        // Un dashboard personnel et un compte principal par personne, un compte Perso pour Sébastien.
        Assert.Single(await seeded.Dashboards.Where(d => d.CreatorId == seb.Id && d.IsPersonal).ToListAsync());
        Assert.Single(await seeded.Dashboards.Where(d => d.CreatorId == audrey.Id && d.IsPersonal).ToListAsync());
        Assert.Single(await seeded.Accounts.Where(a => a.UserId == seb.Id && a.IsPrimary).ToListAsync());
        Assert.Single(await seeded.Accounts.Where(a => a.UserId == seb.Id && a.IsPersonalScope).ToListAsync());
        Assert.Single(await seeded.Accounts.Where(a => a.UserId == audrey.Id && a.IsPrimary).ToListAsync());

        var common = await seeded.Dashboards.Include(d => d.Members).Include(d => d.DashboardAccounts)
            .SingleAsync(d => d.Name == DemoSeeder.CommonDashboardName);
        Assert.False(common.IsPersonal);
        Assert.Contains(common.Members, m => m.UserId == audrey.Id);
        Assert.Equal(2, common.DashboardAccounts.Count);

        var lines = await seeded.Transactions.ToListAsync();
        Assert.All(lines, t => Assert.StartsWith(DemoSeeder.ExternalIdPrefix, t.ExternalId!));
        Assert.Equal(lines.Count, lines.Select(t => t.ExternalId).Distinct().Count());
        Assert.Equal(10, lines.Select(t => t.CategoryId).Distinct().Count());
        Assert.Equal(6, lines.Count(t => t.Description == "Salaire"));
        Assert.Equal(3, lines.Count(t => t.Description == "Loyer maison" && t.IsFixed));
        Assert.InRange(lines.Count(t => t.IsRefund), 3, 4);
        Assert.Single(lines.Where(t => t.IsExceptional));
        Assert.All(lines, t => Assert.InRange(t.Date, new DateTime(2026, 6, 4), new DateTime(2026, 9, 4)));

        var recurring = await seeded.RecurringTransactions.ToListAsync();
        Assert.Equal(3, recurring.Count);
        Assert.All(recurring, r => Assert.Equal(common.Id, r.DashboardId));
        Assert.Single(recurring.Where(r => r.ProvisionAtMonthStart));

        // Un compte bancaire par compte principal, sur une connexion que la sync de fond ignore
        // (Provider.Manual), sans secret ni identifiant GoCardless. Le Perso n'en a pas.
        var connections = await seeded.BankConnections.ToListAsync();
        Assert.Equal(2, connections.Count);
        Assert.All(connections, c => Assert.Equal(BankProvider.Manual, c.Provider));
        Assert.All(connections, c => Assert.Null(c.EncryptedSessionToken));
        Assert.All(connections, c => Assert.StartsWith("demo-", c.RequisitionId));

        var banks = await seeded.BankAccounts.ToListAsync();
        Assert.Equal(2, banks.Count);
        Assert.All(banks, b => Assert.False(b.IsManual));
        Assert.All(banks, b => Assert.True(IbanChecksumIsValid(b.Iban), b.Iban));

        var sebPrimary = await seeded.Accounts.SingleAsync(a => a.UserId == seb.Id && a.IsPrimary);
        var sebPerso = await seeded.Accounts.SingleAsync(a => a.UserId == seb.Id && a.IsPersonalScope);
        var audreyPrimary = await seeded.Accounts.SingleAsync(a => a.UserId == audrey.Id && a.IsPrimary);
        var sebBank = banks.Single(b => b.UserId == seb.Id);
        var audreyBank = banks.Single(b => b.UserId == audrey.Id);
        Assert.All(lines.Where(t => t.AccountId == sebPrimary.Id), t => Assert.Equal(sebBank.Id, t.BankAccountId));
        Assert.All(lines.Where(t => t.AccountId == audreyPrimary.Id), t => Assert.Equal(audreyBank.Id, t.BankAccountId));
        Assert.All(lines.Where(t => t.AccountId == sebPerso.Id), t => Assert.Null(t.BankAccountId));
        Assert.NotEmpty(lines.Where(t => t.AccountId == sebPerso.Id));

        // Le solde booké suit les lignes : ouverture + net, pas un chiffre posé au hasard.
        var sebNet = lines.Where(t => t.BankAccountId == sebBank.Id)
            .Sum(t => t.Type == TransactionType.Income ? t.Amount : -t.Amount);
        Assert.Equal(2350.18m + sebNet, sebBank.BookedBalance);
        Assert.Equal(sebBank.BookedBalance, sebBank.RealBalance);
    }

    private static bool IbanChecksumIsValid(string iban)
    {
        var rearranged = iban[4..] + iban[..4];
        var digits = string.Concat(rearranged.Select(c => char.IsDigit(c) ? c.ToString() : (c - 'A' + 10).ToString()));
        var remainder = 0;
        foreach (var d in digits)
            remainder = (remainder * 10 + (d - '0')) % 97;
        return remainder == 1;
    }

    private static async Task<List<(string, decimal, DateTime, string, int, bool, bool, bool)>> Snapshot(AppDbContext ctx)
        => (await ctx.Transactions.OrderBy(t => t.ExternalId).ToListAsync())
            .Select(t => (t.ExternalId!, t.Amount, t.Date, t.Description, t.CategoryId, t.IsFixed, t.IsRefund, t.IsExceptional))
            .ToList();
}
