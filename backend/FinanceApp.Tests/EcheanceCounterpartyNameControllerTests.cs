using FinanceApp.API.Controllers;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le nom du bénéficiaire sur l'échéance (fiche « prête à payer », 23/09/2026) : trimé à la création, relu
/// tel quel, posé puis effacé par « Modifier », refusé au-delà de 70 caractères (limite EPC) sans écho de
/// la valeur, et null quand rien n'est saisi. Il ne touche à aucune clé du rapprochement.
/// </summary>
public class EcheanceCounterpartyNameControllerTests : IDisposable
{
    private const string Ecole = "BE98068243670693";

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private Household _h = null!;

    public EcheanceCounterpartyNameControllerTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
        using var ctx = NewContext();
        _h = TestHousehold.SeedAsync(ctx, "nom@test.local").GetAwaiter().GetResult();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() => new(_options);

    private EcheanceController Controller(AppDbContext ctx) =>
        new(ctx, Microsoft.Extensions.Options.Options.Create(AgendaTestSupport.Household())) { ControllerContext = TestHousehold.As(_h.UserId) };

    private static EcheanceDto Dto(ActionResult<EcheanceDto> result) =>
        (EcheanceDto)((ObjectResult)result.Result!).Value!;

    private CreateEcheanceDto Create(string? name) => new()
    {
        DashboardId = _h.DashboardId, Label = "Repas", DueDate = new DateOnly(2026, 10, 1), Amount = 2.60m, CounterpartyIban = Ecole, CounterpartyName = name,
    };

    private static UpdateEcheanceDto Update(string? name) => new()
    {
        Label = "Repas", DueDate = new DateOnly(2026, 10, 1), Amount = 2.60m, CounterpartyIban = Ecole, CounterpartyName = name,
    };

    [Fact]
    public async Task Create_TrimeLeNom_EtLeRelitALIdentique()
    {
        using var ctx = NewContext();
        var created = Dto(await Controller(ctx).Create(Create("  Institut Test  ")));
        Assert.Equal("Institut Test", created.CounterpartyName);

        using var check = NewContext();
        Assert.Equal("Institut Test", (await check.Echeances.SingleAsync(x => x.Id == created.Id)).CounterpartyName);
        Assert.Equal("Institut Test", Dto(await Controller(check).GetById(created.Id)).CounterpartyName);
    }

    [Fact]
    public async Task Update_PoseLeNom_PuisLEfface()
    {
        using var ctx = NewContext();
        var id = Dto(await Controller(ctx).Create(Create(null))).Id;

        var posed = Dto(await Controller(ctx).Update(id, Update("Institut Test")));
        Assert.Equal("Institut Test", posed.CounterpartyName);
        // Un nom qui change ne touche pas aux clés du rapprochement.
        Assert.Equal(Ecole, posed.CounterpartyIban);

        var erased = Dto(await Controller(ctx).Update(id, Update("   ")));
        Assert.Null(erased.CounterpartyName);

        using var check = NewContext();
        Assert.Null((await check.Echeances.SingleAsync(x => x.Id == id)).CounterpartyName);
    }

    [Fact]
    public async Task Create_Avec71Caracteres_Rend400_SansLaValeur()
    {
        using var ctx = NewContext();
        var name = new string('x', 71);
        var result = await Controller(ctx).Create(Create(name));

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var body = Assert.IsType<string>(bad.Value);
        Assert.Contains("Nom du bénéficiaire invalide", body);
        Assert.DoesNotContain(name, body);
        Assert.Equal(0, await ctx.Echeances.CountAsync());

        // 70, la limite EPC, passe.
        Assert.Equal(new string('x', 70), Dto(await Controller(ctx).Create(Create(new string('x', 70)))).CounterpartyName);
    }

    [Fact]
    public async Task Update_AvecCaractereDeControle_Rend400()
    {
        using var ctx = NewContext();
        var id = Dto(await Controller(ctx).Create(Create("Institut Test"))).Id;

        var result = await Controller(ctx).Update(id, Update("Institut\nTest"));
        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Contains("Nom du bénéficiaire invalide", Assert.IsType<string>(bad.Value));

        using var check = NewContext();
        Assert.Equal("Institut Test", (await check.Echeances.SingleAsync(x => x.Id == id)).CounterpartyName);
    }

    [Fact]
    public async Task SansNom_LeDtoRendNull()
    {
        using var ctx = NewContext();
        var created = Dto(await Controller(ctx).Create(Create(null)));
        Assert.Null(created.CounterpartyName);

        using var list = NewContext();
        var all = (List<EcheanceDto>)((OkObjectResult)(await Controller(list).GetAll(_h.DashboardId, null, null, null)).Result!).Value!;
        Assert.Single(all);
        Assert.Null(all[0].CounterpartyName);
    }
}
