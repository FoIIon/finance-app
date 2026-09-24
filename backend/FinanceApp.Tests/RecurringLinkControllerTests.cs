using FinanceApp.API.Controllers;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Reporting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le geste manuel de la routine, sur le contrôleur tel quel et une base en mémoire. Deux ménages : A ne voit
/// rien de B (404, jamais 403 ni 200). Les deux conflits rendent 409. Le lien fait passer l'occurrence en paid
/// au GET api/agenda suivant, le délier la ramène en planned. Seule la colonne RecurringTransactionId bouge.
/// </summary>
public class RecurringLinkControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly FixedTimeProvider _clock = new(AgendaTestSupport.Now);
    private Household _a = null!;
    private Household _b = null!;

    public RecurringLinkControllerTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
        using var ctx = NewContext();
        _a = TestHousehold.SeedAsync(ctx, "a@test.local").GetAwaiter().GetResult();
        _b = TestHousehold.SeedAsync(ctx, "b@test.local").GetAwaiter().GetResult();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() => new(_options);

    private AgendaController Agenda(AppDbContext ctx, int userId)
    {
        var household = Options.Create(AgendaTestSupport.Household());
        return new AgendaController(ctx, household, _clock, new RecurringLinkService(ctx, household)) { ControllerContext = TestHousehold.As(userId) };
    }

    private static T Ok<T>(ActionResult<T> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<T>(ok.Value);
    }

    private static async Task<int> RecurringAsync(AppDbContext ctx, Household h, string description = "ENGIE — gaz/électricité", decimal amount = 400m, int day = 24, TransactionType type = TransactionType.Expense)
    {
        var r = new RecurringTransaction
        {
            UserId = h.UserId, DashboardId = h.DashboardId, Description = description, Amount = amount, Type = type,
            Frequency = RecurringFrequency.Monthly, DayOfMonth = day, StartDate = new DateOnly(2025, 1, day), IsActive = true,
        };
        ctx.RecurringTransactions.Add(r);
        await ctx.SaveChangesAsync();
        return r.Id;
    }

    private static async Task<int> TransactionAsync(AppDbContext ctx, Household h, decimal amount, DateTime date, string description = "Paiement Bancontact",
        TransactionType type = TransactionType.Expense, int? recurringId = null, bool provisional = false)
    {
        var tx = new Transaction
        {
            AccountId = h.AccountId, CategoryId = 6, Type = type, Amount = amount, Date = date, Description = description,
            IsImported = true, RecurringTransactionId = recurringId, IsProvisional = provisional,
        };
        ctx.Transactions.Add(tx);
        await ctx.SaveChangesAsync();
        return tx.Id;
    }

    private static DateTime Utc(int day, int month = 9) => new(2026, month, day, 10, 0, 0, DateTimeKind.Utc);

    private static LinkRecurringDto Body(Household h, int transactionId) => new() { DashboardId = h.DashboardId, TransactionId = transactionId };

    private async Task<AgendaItem> OccurrenceAsync(AppDbContext ctx, Household h, int recurringId, DateOnly date)
    {
        var agenda = Ok(await Agenda(ctx, h.UserId).Get(h.DashboardId, "month", date, CancellationToken.None));
        var id = $"recurring:{recurringId}:{date:yyyy-MM-dd}";
        return Assert.Single(agenda.Days.SelectMany(d => d.Items), i => i.Id == id);
    }

    [Fact]
    public async Task HorsPerimetre_LesTroisRoutesRendent404_JamaisRien()
    {
        using var ctx = NewContext();
        var recurringA = await RecurringAsync(ctx, _a);
        var txA = await TransactionAsync(ctx, _a, 12m, Utc(16));
        var recurringB = await RecurringAsync(ctx, _b);
        var txB = await TransactionAsync(ctx, _b, 400m, Utc(16));

        // B sur le dashboard de A : non membre.
        var b = Agenda(ctx, _b.UserId);
        Assert.IsType<NotFoundResult>((await b.Candidates(recurringA, _a.DashboardId, "2026-09", CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await b.Link(recurringA, Body(_a, txA), CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(await b.Unlink(recurringA, _a.DashboardId, txA, CancellationToken.None));

        // A membre de son dashboard, mais la récurrente est à B.
        var a = Agenda(ctx, _a.UserId);
        Assert.IsType<NotFoundResult>((await a.Candidates(recurringB, _a.DashboardId, "2026-09", CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await a.Link(recurringB, Body(_a, txA), CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(await a.Unlink(recurringB, _a.DashboardId, txA, CancellationToken.None));

        // La récurrente est à A, la transaction sur un compte de B.
        Assert.IsType<NotFoundResult>((await a.Link(recurringA, Body(_a, txB), CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>(await a.Unlink(recurringA, _a.DashboardId, txB, CancellationToken.None));

        using var check = NewContext();
        Assert.Null((await check.Transactions.SingleAsync(t => t.Id == txA)).RecurringTransactionId);
        Assert.Null((await check.Transactions.SingleAsync(t => t.Id == txB)).RecurringTransactionId);
    }

    [Fact]
    public async Task Candidates_MoisMalForme_400()
    {
        using var ctx = NewContext();
        var recurring = await RecurringAsync(ctx, _a);
        Assert.IsType<BadRequestObjectResult>((await Agenda(ctx, _a.UserId).Candidates(recurring, _a.DashboardId, "septembre", CancellationToken.None)).Result);
        Assert.IsType<BadRequestObjectResult>((await Agenda(ctx, _a.UserId).Candidates(recurring, _a.DashboardId, "2026-13", CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Candidates_DuMois_MemeSens_NonProvisionnelles_NiPreuveDEcheance_NiLieesAilleurs_DateDecroissante()
    {
        using var ctx = NewContext();
        var recurring = await RecurringAsync(ctx, _a);
        var autre = await RecurringAsync(ctx, _a, "Netflix", 10.66m, 2);
        var le3 = await TransactionAsync(ctx, _a, 33m, Utc(3), "Colruyt");
        var le16 = await TransactionAsync(ctx, _a, 400m, Utc(16), "ENGIE ELECTRABEL");
        var lieIci = await TransactionAsync(ctx, _a, 5m, Utc(20), "Lien manuel", recurringId: recurring);
        await TransactionAsync(ctx, _a, 10.66m, Utc(2), "Netflix", recurringId: autre);
        await TransactionAsync(ctx, _a, 400m, Utc(10), "Provision", provisional: true);
        await TransactionAsync(ctx, _a, 400m, Utc(11), "Remboursement", type: TransactionType.Income);
        await TransactionAsync(ctx, _a, 400m, Utc(31, 8), "Août");
        await TransactionAsync(ctx, _a, 400m, Utc(1, 10), "Octobre");
        var preuve = await TransactionAsync(ctx, _a, 2.60m, Utc(5), "Repas école");
        ctx.Echeances.Add(new Echeance { DashboardId = _a.DashboardId, Label = "Repas", DueDate = new DateOnly(2026, 9, 5), Amount = 2.60m, TransactionId = preuve, CreatedByUserId = _a.UserId });
        await ctx.SaveChangesAsync();

        var list = Ok(await Agenda(ctx, _a.UserId).Candidates(recurring, _a.DashboardId, "2026-09", CancellationToken.None));

        Assert.Equal(new[] { lieIci, le16, le3 }, list.Select(c => c.Id).ToArray());
        Assert.Equal(new[] { true, false, false }, list.Select(c => c.LinkedToThisRecurring).ToArray());
        var engie = list.Single(c => c.Id == le16);
        Assert.Equal(new DateOnly(2026, 9, 16), engie.Date);
        Assert.Equal(400m, engie.Amount);
        Assert.Equal("ENGIE ELECTRABEL", engie.Description);
    }

    [Fact]
    public async Task Candidates_VingtAuPlus_MaisCelleQuiRegleLOccurrenceEstToujoursLa()
    {
        using var ctx = NewContext();
        var recurring = await RecurringAsync(ctx, _a);
        // Le règlement le 2, puis vingt-cinq dépenses plus récentes qui l'auraient poussé hors des vingt.
        var regle = await TransactionAsync(ctx, _a, 400m, Utc(2), "ENGIE ELECTRABEL");
        for (var i = 0; i < 25; i++) await TransactionAsync(ctx, _a, 7m + i, Utc(3 + i % 27), $"Dépense {i}");

        var list = Ok(await Agenda(ctx, _a.UserId).Candidates(recurring, _a.DashboardId, "2026-09", CancellationToken.None));

        Assert.Equal(21, list.Count);
        Assert.Contains(list, c => c.Id == regle);
        Assert.True(list.Zip(list.Skip(1)).All(p => p.First.Date >= p.Second.Date));
    }

    [Fact]
    public async Task Link_TransactionDejaLieeAUneAutreRecurrente_409_RienNeBouge()
    {
        using var ctx = NewContext();
        var engie = await RecurringAsync(ctx, _a);
        var netflix = await RecurringAsync(ctx, _a, "Netflix", 10.66m, 2);
        var tx = await TransactionAsync(ctx, _a, 10.66m, Utc(2), "Netflix", recurringId: netflix);

        var result = await Agenda(ctx, _a.UserId).Link(engie, Body(_a, tx), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(result.Result);

        using var check = NewContext();
        Assert.Equal(netflix, (await check.Transactions.SingleAsync(t => t.Id == tx)).RecurringTransactionId);
    }

    [Fact]
    public async Task Link_TransactionPreuveDUneEcheance_409_RienNeBouge()
    {
        using var ctx = NewContext();
        var engie = await RecurringAsync(ctx, _a);
        var tx = await TransactionAsync(ctx, _a, 2.60m, Utc(5), "Repas école");
        ctx.Echeances.Add(new Echeance { DashboardId = _a.DashboardId, Label = "Repas", DueDate = new DateOnly(2026, 9, 5), Amount = 2.60m, TransactionId = tx, CreatedByUserId = _a.UserId });
        await ctx.SaveChangesAsync();

        var result = await Agenda(ctx, _a.UserId).Link(engie, Body(_a, tx), CancellationToken.None);
        Assert.IsType<ConflictObjectResult>(result.Result);

        using var check = NewContext();
        Assert.Null((await check.Transactions.SingleAsync(t => t.Id == tx)).RecurringTransactionId);
    }

    [Fact]
    public async Task Link_TransactionProvisionnelle_OuDeLAutreSens_409()
    {
        using var ctx = NewContext();
        var engie = await RecurringAsync(ctx, _a);
        var provision = await TransactionAsync(ctx, _a, 400m, Utc(1), "Provision", provisional: true);
        var recette = await TransactionAsync(ctx, _a, 400m, Utc(16), "Remboursement", type: TransactionType.Income);

        var ctl = Agenda(ctx, _a.UserId);
        Assert.IsType<ConflictObjectResult>((await ctl.Link(engie, Body(_a, provision), CancellationToken.None)).Result);
        Assert.IsType<ConflictObjectResult>((await ctl.Link(engie, Body(_a, recette), CancellationToken.None)).Result);

        using var check = NewContext();
        Assert.Null((await check.Transactions.SingleAsync(t => t.Id == recette)).RecurringTransactionId);
    }

    [Fact]
    public async Task Link_PuisAgenda_LOccurrenceEstPaid_Unlink_ElleRedevientPlanned()
    {
        using var ctx = NewContext();
        var engie = await RecurringAsync(ctx, _a);
        // 357,06 sans le mot « engie » : ni le montant ni le libellé ne le trouvent, seul le geste le peut.
        var tx = await TransactionAsync(ctx, _a, 357.06m, Utc(16), "Paiement Bancontact 1234");
        var le24 = new DateOnly(2026, 9, 24);

        var avant = await OccurrenceAsync(ctx, _a, engie, le24);
        Assert.Equal("planned", avant.Status);
        Assert.Null(avant.TransactionId);

        var linked = Ok(await Agenda(ctx, _a.UserId).Link(engie, Body(_a, tx), CancellationToken.None));
        Assert.Equal($"recurring:{engie}:2026-09-24", linked.Id);
        Assert.Equal("paid", linked.Status);
        Assert.Equal(tx, linked.TransactionId);
        Assert.Equal(357.06m, linked.Amount);
        Assert.Equal(new DateOnly(2026, 9, 16), linked.OriginalDate);
        Assert.Equal(le24, linked.Date);

        using (var read = NewContext())
        {
            var apres = await OccurrenceAsync(read, _a, engie, le24);
            Assert.Equal("paid", apres.Status);
            Assert.Equal(tx, apres.TransactionId);
            Assert.Equal(357.06m, apres.Amount);
            Assert.Equal(new DateOnly(2026, 9, 16), apres.OriginalDate);
            Assert.Equal(engie, (await read.Transactions.SingleAsync(t => t.Id == tx)).RecurringTransactionId);
            // Rien d'autre n'a bougé sur la ligne.
            var row = await read.Transactions.SingleAsync(t => t.Id == tx);
            Assert.Equal(357.06m, row.Amount);
            Assert.False(row.IsProvisional);
            Assert.Equal(6, row.CategoryId);
        }

        // Lier une seconde fois la même : idempotent, 200.
        Ok(await Agenda(ctx, _a.UserId).Link(engie, Body(_a, tx), CancellationToken.None));

        // Délier avec la mauvaise récurrente : 404, le lien reste.
        var netflix = await RecurringAsync(ctx, _a, "Netflix", 10.66m, 2);
        Assert.IsType<NotFoundResult>(await Agenda(ctx, _a.UserId).Unlink(netflix, _a.DashboardId, tx, CancellationToken.None));

        Assert.IsType<NoContentResult>(await Agenda(ctx, _a.UserId).Unlink(engie, _a.DashboardId, tx, CancellationToken.None));
        using (var read = NewContext())
        {
            var revenu = await OccurrenceAsync(read, _a, engie, le24);
            Assert.Equal("planned", revenu.Status);
            Assert.Null(revenu.TransactionId);
            Assert.Equal(400m, revenu.Amount);
            Assert.Null(revenu.OriginalDate);
            Assert.Null((await read.Transactions.SingleAsync(t => t.Id == tx)).RecurringTransactionId);
        }

        // Délier une transaction qui n'est plus liée : 404.
        Assert.IsType<NotFoundResult>(await Agenda(ctx, _a.UserId).Unlink(engie, _a.DashboardId, tx, CancellationToken.None));
    }

    [Fact]
    public async Task Unlink_UneProvision_404_LeProvisionnementGardeSonLien()
    {
        using var ctx = NewContext();
        var salaire = await RecurringAsync(ctx, _a, "Salaire", 3428m, 28, TransactionType.Income);
        var provision = await TransactionAsync(ctx, _a, 3428m, Utc(1), "Salaire (provision)", type: TransactionType.Income, recurringId: salaire, provisional: true);

        Assert.IsType<NotFoundResult>(await Agenda(ctx, _a.UserId).Unlink(salaire, _a.DashboardId, provision, CancellationToken.None));

        using var check = NewContext();
        Assert.Equal(salaire, (await check.Transactions.SingleAsync(t => t.Id == provision)).RecurringTransactionId);
    }

    [Fact]
    public async Task Agenda_UneTransactionDejaPreuveDUneEcheance_NeRegleJamaisLaRoutine()
    {
        using var ctx = NewContext();
        var engie = await RecurringAsync(ctx, _a);
        var tx = await TransactionAsync(ctx, _a, 400m, Utc(16), "ENGIE ELECTRABEL");
        ctx.Echeances.Add(new Echeance { DashboardId = _a.DashboardId, Label = "Engie régularisation", DueDate = new DateOnly(2026, 9, 16), Amount = 400m, TransactionId = tx, CreatedByUserId = _a.UserId });
        await ctx.SaveChangesAsync();

        var occurrence = await OccurrenceAsync(ctx, _a, engie, new DateOnly(2026, 9, 24));
        Assert.Equal("planned", occurrence.Status);
    }
}
