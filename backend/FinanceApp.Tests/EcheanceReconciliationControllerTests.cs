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
/// Le contrôleur des échéances face aux clés du rapprochement : normalisation à la saisie, refus d'un
/// contrôle 97 faux sans écho de la valeur, « Finalement non » qui refuse la transaction seulement quand
/// c'est le rapprocheur qui l'avait liée, et le détail de paiement dans le DTO.
/// </summary>
public class EcheanceReconciliationControllerTests : IDisposable
{
    private const string Ecole = "BE98068243670693";
    private static readonly string Com = StructuredCommunicationTests.Sc(2026080001);

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private Household _h = null!;

    public EcheanceReconciliationControllerTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
        using var ctx = NewContext();
        _h = TestHousehold.SeedAsync(ctx, "ctl@test.local").GetAwaiter().GetResult();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() => new(_options);

    private EcheanceController Controller(AppDbContext ctx) =>
        new(ctx, Microsoft.Extensions.Options.Options.Create(new FinanceApp.API.Services.Calendar.HouseholdOptions())) { ControllerContext = TestHousehold.As(_h.UserId) };

    private static EcheanceDto Dto(ActionResult<EcheanceDto> result) =>
        (EcheanceDto)((ObjectResult)result.Result!).Value!;

    private async Task<int> TransactionAsync(AppDbContext ctx, decimal amount, DateTime date, string desc = "Ecole communale", string? counterparty = "ECOLE COMMUNALE")
    {
        var tx = new Transaction
        {
            AccountId = _h.AccountId, CategoryId = 6, Type = TransactionType.Expense, Amount = amount, Date = date,
            Description = desc, CounterpartyName = counterparty, CounterpartyIban = Ecole, IsImported = true,
        };
        ctx.Transactions.Add(tx);
        await ctx.SaveChangesAsync();
        return tx.Id;
    }

    private async Task<int> EcheanceAsync(AppDbContext ctx, int? transactionId = null, DateTime? matchedAt = null, DateTime? refusedAt = null)
    {
        var e = new Echeance
        {
            DashboardId = _h.DashboardId, Label = "Repas", DueDate = new DateOnly(2026, 8, 31), Amount = 2.60m, CounterpartyIban = Ecole,
            TransactionId = transactionId, MatchedAt = matchedAt, AutoMatchRefusedAt = refusedAt, CreatedByUserId = _h.UserId,
        };
        ctx.Echeances.Add(e);
        await ctx.SaveChangesAsync();
        return e.Id;
    }

    [Fact]
    public async Task Unpay_DUnRapprochementAutomatique_PoseAutoMatchRefusedAt()
    {
        using var ctx = NewContext();
        var txId = await TransactionAsync(ctx, 2.60m, new DateTime(2026, 8, 28));
        var id = await EcheanceAsync(ctx, transactionId: txId, matchedAt: DateTime.UtcNow);

        var dto = Dto(await Controller(ctx).Unpay(id));
        // La date limite est passée : en retard, plus payée. Le statut se dérive, il n'est pas écrit.
        Assert.Equal("EnRetard", dto.Status);
        Assert.Null(dto.TransactionId);
        Assert.Null(dto.MatchedAt);
        Assert.Null(dto.Payment);
        Assert.NotNull(dto.AutoMatchRefusedAt);
        Assert.Equal(DateTimeKind.Utc, dto.AutoMatchRefusedAt!.Value.Kind);

        using var check = NewContext();
        var e = await check.Echeances.SingleAsync(x => x.Id == id);
        Assert.NotNull(e.AutoMatchRefusedAt);
        Assert.Null(e.TransactionId);
        Assert.Null(e.MatchedAt);
    }

    [Fact]
    public async Task Unpay_DUnLienManuel_NePosePasAutoMatchRefusedAt()
    {
        using var ctx = NewContext();
        var txId = await TransactionAsync(ctx, 2.60m, new DateTime(2026, 8, 28));
        var id = await EcheanceAsync(ctx, transactionId: txId, matchedAt: null);

        Dto(await Controller(ctx).Unpay(id));

        using var check = NewContext();
        var e = await check.Echeances.SingleAsync(x => x.Id == id);
        Assert.Null(e.AutoMatchRefusedAt);
        Assert.Null(e.TransactionId);
    }

    [Fact]
    public async Task Pay_PuisUnpay_Manuels_NeTouchentPasAuRefus()
    {
        using var ctx = NewContext();
        var id = await EcheanceAsync(ctx);
        Dto(await Controller(ctx).Pay(id));
        Dto(await Controller(ctx).Unpay(id));
        using var check = NewContext();
        Assert.Null((await check.Echeances.SingleAsync(x => x.Id == id)).AutoMatchRefusedAt);
    }

    [Fact]
    public async Task Create_AvecCommunicationInvalide_Rend400_SansLaValeur()
    {
        using var ctx = NewContext();
        var result = await Controller(ctx).Create(new CreateEcheanceDto
        {
            DashboardId = _h.DashboardId, Label = "Taxe", DueDate = new DateOnly(2026, 10, 1), StructuredCommunication = "+++123/4567/89012+++",
        });

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        var body = Assert.IsType<string>(bad.Value);
        Assert.Contains("Communication structurée invalide", body);
        Assert.DoesNotContain("89012", body);
        Assert.Equal(0, await ctx.Echeances.CountAsync());
    }

    [Fact]
    public async Task Create_AvecIbanHorsForme_Rend400_SansLaValeur()
    {
        using var ctx = NewContext();
        var result = await Controller(ctx).Create(new CreateEcheanceDto
        {
            DashboardId = _h.DashboardId, Label = "Taxe", DueDate = new DateOnly(2026, 10, 1), CounterpartyIban = "BE98 0682",
        });

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.DoesNotContain("0682", Assert.IsType<string>(bad.Value));
    }

    [Fact]
    public async Task Create_NormaliseIbanEtCommunication()
    {
        using var ctx = NewContext();
        var created = Dto(await Controller(ctx).Create(new CreateEcheanceDto
        {
            DashboardId = _h.DashboardId, Label = "Ostéo", DueDate = new DateOnly(2026, 10, 1),
            CounterpartyIban = "be98 0682 4367 0693", StructuredCommunication = $"+++{Com[..3]}/{Com[3..7]}/{Com[7..]}+++",
        }));

        Assert.Equal(Ecole, created.CounterpartyIban);
        Assert.Equal(Com, created.StructuredCommunication);
        Assert.Null(created.MatchedAt);
        Assert.Null(created.Payment);

        // Vides : null en base, pas une chaîne vide.
        var sans = Dto(await Controller(ctx).Create(new CreateEcheanceDto
        {
            DashboardId = _h.DashboardId, Label = "Sans clés", DueDate = new DateOnly(2026, 10, 1), CounterpartyIban = "  ", StructuredCommunication = "",
        }));
        Assert.Null(sans.CounterpartyIban);
        Assert.Null(sans.StructuredCommunication);
    }

    [Fact]
    public async Task Update_AvecTransactionIdALaMain_MetMatchedAtNull_EtRemplitPayment()
    {
        using var ctx = NewContext();
        var txId = await TransactionAsync(ctx, 2.60m, new DateTime(2026, 8, 28), desc: "Repas chauds août");
        var id = await EcheanceAsync(ctx);

        var dto = Dto(await Controller(ctx).Update(id, new UpdateEcheanceDto
        {
            Label = "Repas", DueDate = new DateOnly(2026, 8, 31), Amount = 2.60m, TransactionId = txId, CounterpartyIban = Ecole,
        }));

        Assert.Equal("Payee", dto.Status);
        Assert.Equal(txId, dto.TransactionId);
        Assert.Null(dto.MatchedAt);
        Assert.NotNull(dto.Payment);
        Assert.Equal(txId, dto.Payment!.TransactionId);
        Assert.Equal(new DateOnly(2026, 8, 28), dto.Payment.Date);
        Assert.Equal(2.60m, dto.Payment.Amount);
        Assert.Equal("Repas chauds août", dto.Payment.Description);
        Assert.Equal("ECOLE COMMUNALE", dto.Payment.CounterpartyName);

        using var check = NewContext();
        var e = await check.Echeances.SingleAsync(x => x.Id == id);
        Assert.Null(e.AutoMatchRefusedAt);
    }

    [Fact]
    public async Task Update_QuiChangeLesClesDUneEcheancePayee_NeDetacheRien()
    {
        using var ctx = NewContext();
        var txId = await TransactionAsync(ctx, 2.60m, new DateTime(2026, 8, 28));
        var matchedAt = new DateTime(2026, 8, 29, 6, 0, 0, DateTimeKind.Utc);
        var id = await EcheanceAsync(ctx, transactionId: txId, matchedAt: matchedAt);

        var dto = Dto(await Controller(ctx).Update(id, new UpdateEcheanceDto
        {
            Label = "Repas", DueDate = new DateOnly(2026, 8, 31), Amount = 2.60m, TransactionId = txId,
            CounterpartyIban = "BE71 0961 2345 6769", StructuredCommunication = Com,
        }));

        Assert.Equal(txId, dto.TransactionId);
        Assert.Equal(matchedAt, dto.MatchedAt);
        Assert.Equal(DateTimeKind.Utc, dto.MatchedAt!.Value.Kind);
        Assert.Equal("BE71096123456769", dto.CounterpartyIban);
        Assert.Equal(Com, dto.StructuredCommunication);
        Assert.NotNull(dto.Payment);
    }

    [Fact]
    public async Task Update_QuiDetacheUnRapprochementAutomatique_RefuseLaTransaction()
    {
        using var ctx = NewContext();
        var txId = await TransactionAsync(ctx, 2.60m, new DateTime(2026, 8, 28));
        var id = await EcheanceAsync(ctx, transactionId: txId, matchedAt: DateTime.UtcNow);

        var dto = Dto(await Controller(ctx).Update(id, new UpdateEcheanceDto
        {
            Label = "Repas", DueDate = new DateOnly(2026, 8, 31), Amount = 2.60m, TransactionId = null, CounterpartyIban = Ecole,
        }));

        Assert.Null(dto.TransactionId);
        Assert.Null(dto.MatchedAt);
        Assert.Null(dto.Payment);
        Assert.NotNull(dto.AutoMatchRefusedAt);
        using var check = NewContext();
        Assert.NotNull((await check.Echeances.SingleAsync(x => x.Id == id)).AutoMatchRefusedAt);
    }

    [Fact]
    public async Task Update_QuiCorrigeUneCle_LeveLeRefus_UnUpdateSansChangementDeCle_LeGarde()
    {
        using var ctx = NewContext();
        var refusedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var id = await EcheanceAsync(ctx, refusedAt: refusedAt);
        var ctl = Controller(ctx);

        // Même IBAN, libellé changé : le refus reste.
        var same = Dto(await ctl.Update(id, new UpdateEcheanceDto { Label = "Repas chauds", DueDate = new DateOnly(2026, 8, 31), Amount = 2.60m, CounterpartyIban = "BE98 0682 4367 0693" }));
        Assert.Equal(refusedAt, same.AutoMatchRefusedAt);

        // Communication ajoutée : la clé a changé, le refus tombe.
        var corrected = Dto(await ctl.Update(id, new UpdateEcheanceDto { Label = "Repas chauds", DueDate = new DateOnly(2026, 8, 31), Amount = 2.60m, CounterpartyIban = Ecole, StructuredCommunication = Com }));
        Assert.Null(corrected.AutoMatchRefusedAt);

        // Détacher un lien automatique et corriger l'IBAN dans le même geste : la correction l'emporte, on redevine.
        var txId = await TransactionAsync(ctx, 2.60m, new DateTime(2026, 8, 28));
        var linked = await EcheanceAsync(ctx, transactionId: txId, matchedAt: DateTime.UtcNow);
        var both = Dto(await ctl.Update(linked, new UpdateEcheanceDto { Label = "Repas", DueDate = new DateOnly(2026, 8, 31), Amount = 2.60m, TransactionId = null, CounterpartyIban = "BE71 0961 2345 6769" }));
        Assert.Null(both.TransactionId);
        Assert.Null(both.AutoMatchRefusedAt);
    }

    [Fact]
    public async Task GetById_RemplitPayment_GetAll_LeLaisseNull_MaisDitLeStatut()
    {
        using var ctx = NewContext();
        var txId = await TransactionAsync(ctx, 2.60m, new DateTime(2026, 8, 28));
        var id = await EcheanceAsync(ctx, transactionId: txId, matchedAt: DateTime.UtcNow);

        using var read = NewContext();
        var ctl = Controller(read);
        var one = Dto(await ctl.GetById(id));
        Assert.NotNull(one.Payment);
        Assert.Equal(txId, one.Payment!.TransactionId);
        Assert.NotNull(one.MatchedAt);
        Assert.Equal(DateTimeKind.Utc, one.MatchedAt!.Value.Kind);

        // La liste ne charge pas la transaction : Payment null, mais le statut et le lien se lisent des colonnes.
        // Un contexte neuf, comme une requête à part : sinon la transaction suivie par GetById se raccrocherait.
        using var list = NewContext();
        var all = (List<EcheanceDto>)((OkObjectResult)(await Controller(list).GetAll(_h.DashboardId, null, null, null)).Result!).Value!;
        Assert.Single(all);
        Assert.Null(all[0].Payment);
        Assert.Equal(txId, all[0].TransactionId);
        Assert.Equal("Payee", all[0].Status);
        Assert.NotNull(all[0].MatchedAt);
    }
}
