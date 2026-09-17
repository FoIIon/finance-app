using FinanceApp.API.Controllers;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.API.Services.Reporting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// GET /transaction paginé : sans limit le comportement d'avant (tout, pas d'en-tête), avec limit une
/// fenêtre Skip/Take, le total du périmètre dans X-Total-Count, un plafond à 500, offset négatif refusé,
/// et un ordre stable entre deux pages quand toutes les lignes portent la même date.
/// </summary>
public class TransactionPaginationControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly DocumentStorage _storage;
    private readonly string _root;
    private readonly Household _h;

    public TransactionPaginationControllerTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
        (_storage, _, _root) = TestHousehold.TempStorage();
        using var ctx = NewContext();
        _h = TestHousehold.SeedAsync(ctx, "pages@test.local").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _connection.Dispose();
        TestHousehold.RemoveTemp(_root);
    }

    private AppDbContext NewContext() => new(_options);

    private TransactionController Controller(AppDbContext ctx)
    {
        var balances = new AccountBalanceService(ctx);
        return new TransactionController(ctx, new DashboardService(ctx, _storage), new ReportingService(ctx, balances), balances)
        {
            ControllerContext = TestHousehold.As(_h.UserId),
        };
    }

    private async Task<List<int>> SeedAsync(AppDbContext ctx, int count, DateTime? sameDay = null)
    {
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var tx = new Transaction
            {
                AccountId = _h.AccountId, CategoryId = 1, Type = TransactionType.Expense, Amount = 10 + i,
                Date = sameDay ?? new DateTime(2026, 9, 1).AddDays(i), Description = $"Ligne {i}",
            };
            ctx.Transactions.Add(tx);
            await ctx.SaveChangesAsync();
            ids.Add(tx.Id);
        }
        return ids;
    }

    private static List<TransactionDto> Lignes(ActionResult<List<TransactionDto>> result) =>
        (List<TransactionDto>)((OkObjectResult)result.Result!).Value!;

    private Task<ActionResult<List<TransactionDto>>> GetAll(TransactionController c, int? limit = null, int? offset = null) =>
        c.GetAll(_h.DashboardId, null, null, null, null, null, null, null, null, null, null, null, limit, offset);

    [Fact]
    public async Task SansLimit_RendToutesLesLignes_SansEnTete()
    {
        using var ctx = NewContext();
        await SeedAsync(ctx, 5);
        var c = Controller(ctx);

        var lignes = Lignes(await GetAll(c));

        Assert.Equal(5, lignes.Count);
        Assert.False(c.Response.Headers.ContainsKey(TransactionController.EnTeteTotal));
    }

    [Fact]
    public async Task Limit2_Sur5Lignes_Rend2_EtTotal5()
    {
        using var ctx = NewContext();
        await SeedAsync(ctx, 5);
        var c = Controller(ctx);

        var lignes = Lignes(await GetAll(c, limit: 2));

        Assert.Equal(2, lignes.Count);
        Assert.Equal("5", c.Response.Headers[TransactionController.EnTeteTotal].ToString());
    }

    [Fact]
    public async Task Offset4_Limit2_Sur5Lignes_Rend1()
    {
        using var ctx = NewContext();
        var ids = await SeedAsync(ctx, 5);
        var c = Controller(ctx);

        var lignes = Lignes(await GetAll(c, limit: 2, offset: 4));

        // Tri par défaut : date décroissante. La cinquième et dernière ligne est la plus ancienne.
        Assert.Single(lignes);
        Assert.Equal(ids[0], lignes[0].Id);
        Assert.Equal("5", c.Response.Headers[TransactionController.EnTeteTotal].ToString());
    }

    [Fact]
    public async Task Limit1000_RendAuPlus500()
    {
        using var ctx = NewContext();
        await SeedAsync(ctx, TransactionController.LimiteMaxParPage + 1, sameDay: new DateTime(2026, 9, 1));
        var c = Controller(ctx);

        var lignes = Lignes(await GetAll(c, limit: 1000));

        Assert.Equal(TransactionController.LimiteMaxParPage, lignes.Count);
        Assert.Equal((TransactionController.LimiteMaxParPage + 1).ToString(), c.Response.Headers[TransactionController.EnTeteTotal].ToString());
    }

    [Fact]
    public async Task OffsetNegatif_Rend400()
    {
        using var ctx = NewContext();
        await SeedAsync(ctx, 2);

        var result = await GetAll(Controller(ctx), limit: 2, offset: -1);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task LimitZero_Rend400()
    {
        using var ctx = NewContext();
        await SeedAsync(ctx, 2);

        var result = await GetAll(Controller(ctx), limit: 0);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task OffsetSansLimit_SauteLesLignes_SansEnTete()
    {
        using var ctx = NewContext();
        await SeedAsync(ctx, 5);
        var c = Controller(ctx);

        var lignes = Lignes(await GetAll(c, offset: 3));

        Assert.Equal(2, lignes.Count);
        Assert.False(c.Response.Headers.ContainsKey(TransactionController.EnTeteTotal));
    }

    [Fact]
    public async Task CinqLignesLeMemeJour_ParPagesDe2_AucunDoublon_AucunePerte()
    {
        using var ctx = NewContext();
        var ids = await SeedAsync(ctx, 5, sameDay: new DateTime(2026, 9, 10));

        var vus = new List<int>();
        for (var offset = 0; offset < 5; offset += 2)
        {
            var page = Lignes(await GetAll(Controller(ctx), limit: 2, offset: offset));
            Assert.True(page.Count <= 2);
            vus.AddRange(page.Select(l => l.Id));
        }

        Assert.Equal(5, vus.Count);
        Assert.Equal(5, vus.Distinct().Count());
        Assert.Equal(ids.OrderByDescending(i => i), vus);
    }

    [Fact]
    public async Task SansCompteVisible_AvecLimit_RendVide_EtTotalZero()
    {
        using var ctx = NewContext();
        var c = Controller(ctx);

        // Un dashboard inexistant pour cet utilisateur : aucun compte dans le périmètre.
        var lignes = Lignes(await c.GetAll(_h.DashboardId + 1000, null, null, null, null, null, null, null, null, null, null, null, 100, 0));

        Assert.Empty(lignes);
        Assert.Equal("0", c.Response.Headers[TransactionController.EnTeteTotal].ToString());
    }
}
