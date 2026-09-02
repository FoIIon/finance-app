using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le compte logique Perso : identifié par son flag et non par son nom, un seul par utilisateur,
/// rattaché au dashboard Personnel même quand il préexistait sans lien. Base SQLite en mémoire, le
/// vrai schéma (index unique filtré compris), pas un provider InMemory qui ignorerait les contraintes.
/// </summary>
public class PersoAccountsTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public PersoAccountsTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() => new(_options);

    private async Task<(int userId, int personalDashboardId)> SeedUserAsync()
    {
        using var ctx = NewContext();
        var user = new User { Email = "seb@test.local", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        var personal = new Dashboard { Name = "Personnel", CreatorId = user.Id, IsPersonal = true, CreatedAt = DateTime.UtcNow.AddDays(-2) };
        var common = new Dashboard { Name = "Commun", CreatorId = user.Id, CreatedAt = DateTime.UtcNow.AddDays(-1) };
        ctx.Dashboards.AddRange(personal, common);
        await ctx.SaveChangesAsync();
        return (user.Id, personal.Id);
    }

    [Fact]
    public async Task Cree_LeCompte_Et_LeRattacheAuDashboardPersonnel()
    {
        var (userId, personalDashboardId) = await SeedUserAsync();

        int id;
        using (var ctx = NewContext())
            id = await PersoAccounts.GetOrCreatePersoAccountIdAsync(ctx, userId);

        using var check = NewContext();
        var account = await check.Accounts.SingleAsync(a => a.Id == id);
        Assert.True(account.IsPersonalScope);
        Assert.Equal(PersoAccounts.DefaultName, account.Name);
        Assert.True(await check.DashboardAccounts.AnyAsync(da => da.DashboardId == personalDashboardId && da.AccountId == id));
    }

    [Fact]
    public async Task RetrouveLeCompte_ParSonFlag_MemeRenomme()
    {
        // Revue du 28/08 : résolu par le nom « Perso », un renommage dans l'écran des comptes faisait
        // créer un second compte à la sync suivante.
        var (userId, _) = await SeedUserAsync();
        int id;
        using (var ctx = NewContext())
        {
            id = await PersoAccounts.GetOrCreatePersoAccountIdAsync(ctx, userId);
            var account = await ctx.Accounts.SingleAsync(a => a.Id == id);
            account.Name = "Perso Seb";
            await ctx.SaveChangesAsync();
        }

        using (var ctx = NewContext())
            Assert.Equal(id, await PersoAccounts.GetOrCreatePersoAccountIdAsync(ctx, userId));

        using var check = NewContext();
        Assert.Equal(1, await check.Accounts.CountAsync(a => a.UserId == userId && a.IsPersonalScope));
    }

    [Fact]
    public async Task UnCompteNommePerso_SansFlag_NEstPasLeComptePerso()
    {
        var (userId, _) = await SeedUserAsync();
        int homonyme;
        using (var ctx = NewContext())
        {
            var account = new Account { Name = PersoAccounts.DefaultName, UserId = userId };
            ctx.Accounts.Add(account);
            await ctx.SaveChangesAsync();
            homonyme = account.Id;
        }

        using var ctx2 = NewContext();
        var id = await PersoAccounts.GetOrCreatePersoAccountIdAsync(ctx2, userId);
        Assert.NotEqual(homonyme, id);
    }

    [Fact]
    public async Task RepareLeLienDashboard_DUnComptePreexistant()
    {
        var (userId, personalDashboardId) = await SeedUserAsync();
        int id;
        using (var ctx = NewContext())
        {
            var account = new Account { Name = "Perso", UserId = userId, IsPersonalScope = true };
            ctx.Accounts.Add(account);
            await ctx.SaveChangesAsync();
            id = account.Id;
        }

        using (var ctx = NewContext())
            Assert.Equal(id, await PersoAccounts.GetOrCreatePersoAccountIdAsync(ctx, userId));

        using var check = NewContext();
        Assert.True(await check.DashboardAccounts.AnyAsync(da => da.DashboardId == personalDashboardId && da.AccountId == id));
    }

    [Fact]
    public async Task DeuxComptesPerso_PourUnMemeUtilisateur_SontRefusesParLaBase()
    {
        var (userId, _) = await SeedUserAsync();
        using var ctx = NewContext();
        ctx.Accounts.Add(new Account { Name = "A", UserId = userId, IsPersonalScope = true });
        await ctx.SaveChangesAsync();
        ctx.Accounts.Add(new Account { Name = "B", UserId = userId, IsPersonalScope = true });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task PlusieursComptesOrdinaires_ParUtilisateur_RestentPermis()
    {
        var (userId, _) = await SeedUserAsync();
        using var ctx = NewContext();
        ctx.Accounts.Add(new Account { Name = "Commun", UserId = userId });
        ctx.Accounts.Add(new Account { Name = "Épargne", UserId = userId });
        await ctx.SaveChangesAsync();
        Assert.Equal(2, await ctx.Accounts.CountAsync(a => a.UserId == userId));
    }

    [Fact]
    public async Task Course_LePerdantReprendLeCompteDuGagnant()
    {
        // Deux contextes passent le check « pas de compte Perso » avant que l'un ait inséré.
        var (userId, _) = await SeedUserAsync();
        int winnerId;
        using (var winner = NewContext())
        {
            winner.Accounts.Add(new Account { Name = "Perso", UserId = userId, IsPersonalScope = true });
            await winner.SaveChangesAsync();
            winnerId = (await winner.Accounts.SingleAsync(a => a.IsPersonalScope)).Id;
        }

        // Le perdant : simulé par un contexte dont l'insertion échoue sur l'index. GetOrCreate lit
        // d'abord, donc on force le scénario en insérant nous-mêmes le doublon dans son contexte.
        using var loser = NewContext();
        loser.Accounts.Add(new Account { Name = "Perso", UserId = userId, IsPersonalScope = true });
        await Assert.ThrowsAsync<DbUpdateException>(() => loser.SaveChangesAsync());
        loser.ChangeTracker.Clear();
        Assert.Equal(winnerId, await PersoAccounts.GetOrCreatePersoAccountIdAsync(loser, userId));
    }

    [Fact]
    public async Task LeDashboardPersonnel_EstReconnuParSonDrapeau_PasParSonAge()
    {
        // Avant le 02/09/2026 le code prenait « le plus ancien des dashboards du créateur ». Ici le
        // Commun est plus ancien que le Personnel : c'est quand même le Personnel qui reçoit le compte.
        int userId, personalId;
        using (var ctx = NewContext())
        {
            var user = new User { Email = "flag@test.local", PasswordHash = "x", CreatedAt = DateTime.UtcNow };
            ctx.Users.Add(user);
            await ctx.SaveChangesAsync();
            var common = new Dashboard { Name = "Commun", CreatorId = user.Id, CreatedAt = DateTime.UtcNow.AddDays(-10) };
            var personal = new Dashboard { Name = "Personnel", CreatorId = user.Id, IsPersonal = true, CreatedAt = DateTime.UtcNow };
            ctx.Dashboards.AddRange(common, personal);
            await ctx.SaveChangesAsync();
            userId = user.Id;
            personalId = personal.Id;
        }

        int id;
        using (var ctx = NewContext())
            id = await PersoAccounts.GetOrCreatePersoAccountIdAsync(ctx, userId);

        using var check = NewContext();
        var links = await check.DashboardAccounts.Where(da => da.AccountId == id).Select(da => da.DashboardId).ToListAsync();
        Assert.Equal(new[] { personalId }, links);
    }

    [Fact]
    public async Task DeuxDashboardsPersonnels_PourUnMemeCreateur_SontRefusesParLaBase()
    {
        var (userId, _) = await SeedUserAsync();
        using var ctx = NewContext();
        ctx.Dashboards.Add(new Dashboard { Name = "Doublon", CreatorId = userId, IsPersonal = true });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }
}
