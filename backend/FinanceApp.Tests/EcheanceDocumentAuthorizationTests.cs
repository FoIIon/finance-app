using FinanceApp.API.Controllers;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Deux ménages, A et B. A ne lit, ne modifie ni ne supprime rien de B : 404 partout, y compris le
/// contenu d'un fichier, et jamais 403 qui révélerait que la ligne existe. Les contrôleurs sont appelés
/// tels quels, avec la claim que lit GetUserId(), sur une base SQLite en mémoire au vrai schéma.
/// </summary>
public class EcheanceDocumentAuthorizationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly DocumentStorage _storage;
    private readonly DocumentStorageOptions _storageOptions;
    private readonly string _root;

    private Household _a = null!;
    private Household _b = null!;
    private int _echeanceB;
    private int _documentB;

    public EcheanceDocumentAuthorizationTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
        (_storage, _storageOptions, _root) = TestHousehold.TempStorage();
        SeedAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _connection.Dispose();
        TestHousehold.RemoveTemp(_root);
    }

    private AppDbContext NewContext() => new(_options);

    private EcheanceController Echeances(AppDbContext ctx, int userId) =>
        new(ctx) { ControllerContext = TestHousehold.As(userId) };

    private DocumentController Documents(AppDbContext ctx, int userId) =>
        new(ctx, _storage, _storageOptions) { ControllerContext = TestHousehold.As(userId) };

    private async Task SeedAsync()
    {
        using var ctx = NewContext();
        _a = await TestHousehold.SeedAsync(ctx, "a@test.local");
        _b = await TestHousehold.SeedAsync(ctx, "b@test.local");

        // Le ménage B a une échéance et un document, posés par le contrôleur de B lui-même.
        var created = await Echeances(ctx, _b.UserId).Create(new CreateEcheanceDto
        {
            DashboardId = _b.DashboardId, Label = "Précompte immobilier", DueDate = new DateOnly(2026, 10, 15), Amount = 1234.56m,
        });
        _echeanceB = ((EcheanceDto)((CreatedAtActionResult)created.Result!).Value!).Id;

        var uploaded = await Documents(ctx, _b.UserId).Upload(new UploadDocumentDto
        {
            DashboardId = _b.DashboardId, EcheanceId = _echeanceB, Kind = DocumentKind.Facture,
            File = TestHousehold.FormFile(TestHousehold.PdfBytes("b"), "avertissement.pdf"),
        }, CancellationToken.None);
        _documentB = ((DocumentDto)((CreatedAtActionResult)uploaded.Result!).Value!).Id;
    }

    // ----- Échéances -----

    [Fact]
    public async Task A_NeListePas_LesEcheancesDeB()
    {
        using var ctx = NewContext();
        var result = await Echeances(ctx, _a.UserId).GetAll(_b.DashboardId, null, null, null);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task A_NeLitPas_UneEcheanceDeB()
    {
        using var ctx = NewContext();
        Assert.IsType<NotFoundResult>((await Echeances(ctx, _a.UserId).GetById(_echeanceB)).Result);
    }

    [Fact]
    public async Task A_NeCreePas_DansLeDashboardDeB()
    {
        using var ctx = NewContext();
        var result = await Echeances(ctx, _a.UserId).Create(new CreateEcheanceDto
        {
            DashboardId = _b.DashboardId, Label = "Intrusion", DueDate = new DateOnly(2026, 12, 1),
        });
        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(1, await ctx.Echeances.CountAsync(e => e.DashboardId == _b.DashboardId));
    }

    [Fact]
    public async Task A_NeModifiePas_NePaiePas_NeSupprimePas_UneEcheanceDeB()
    {
        using var ctx = NewContext();
        var ctl = Echeances(ctx, _a.UserId);

        Assert.IsType<NotFoundResult>((await ctl.Update(_echeanceB, new UpdateEcheanceDto { Label = "Piraté", DueDate = new DateOnly(2027, 1, 1) })).Result);
        Assert.IsType<NotFoundResult>((await ctl.Pay(_echeanceB)).Result);
        Assert.IsType<NotFoundResult>((await ctl.Unpay(_echeanceB)).Result);
        Assert.IsType<NotFoundResult>(await ctl.Delete(_echeanceB));

        using var check = NewContext();
        var e = await check.Echeances.SingleAsync(x => x.Id == _echeanceB);
        Assert.Equal("Précompte immobilier", e.Label);
        Assert.Null(e.PaidAt);
    }

    [Fact]
    public async Task B_LitEtPaieSaPropreEcheance_LeStatutSuit()
    {
        using var ctx = NewContext();
        var ctl = Echeances(ctx, _b.UserId);

        var before = (EcheanceDto)((OkObjectResult)(await ctl.GetById(_echeanceB)).Result!).Value!;
        Assert.Equal("AVenir", before.Status);
        Assert.True(before.IsAmountKnown);
        Assert.Equal(new[] { _documentB }, before.DocumentIds);

        var paid = (EcheanceDto)((OkObjectResult)(await ctl.Pay(_echeanceB)).Result!).Value!;
        Assert.Equal("Payee", paid.Status);
        Assert.NotNull(paid.PaidAt);

        Assert.IsType<ConflictObjectResult>((await ctl.Pay(_echeanceB)).Result);

        var unpaid = (EcheanceDto)((OkObjectResult)(await ctl.Unpay(_echeanceB)).Result!).Value!;
        Assert.Equal("AVenir", unpaid.Status);
        Assert.Null(unpaid.PaidAt);
    }

    [Fact]
    public async Task LeFiltreStatut_SAppliqueApresCalcul()
    {
        using var ctx = NewContext();
        var ctl = Echeances(ctx, _b.UserId);
        await ctl.Create(new CreateEcheanceDto { DashboardId = _b.DashboardId, Label = "Vieille", DueDate = new DateOnly(2020, 1, 1) });

        var late = (List<EcheanceDto>)((OkObjectResult)(await ctl.GetAll(_b.DashboardId, null, null, EcheanceStatus.EnRetard)).Result!).Value!;
        var upcoming = (List<EcheanceDto>)((OkObjectResult)(await ctl.GetAll(_b.DashboardId, null, null, EcheanceStatus.AVenir)).Result!).Value!;

        Assert.Single(late);
        Assert.Equal("Vieille", late[0].Label);
        Assert.Single(upcoming);
        Assert.Equal(_echeanceB, upcoming[0].Id);
    }

    [Fact]
    public async Task UneTransactionDUnAutreDashboard_NeProuvePas_UneEcheance()
    {
        using var ctx = NewContext();
        var txA = new Transaction { AccountId = _a.AccountId, CategoryId = 3, Amount = 1234.56m, Date = new DateTime(2026, 10, 10), Type = TransactionType.Expense, Description = "Précompte A" };
        ctx.Transactions.Add(txA);
        await ctx.SaveChangesAsync();

        var result = await Echeances(ctx, _b.UserId).Update(_echeanceB, new UpdateEcheanceDto
        {
            Label = "Précompte immobilier", DueDate = new DateOnly(2026, 10, 15), Amount = 1234.56m, TransactionId = txA.Id,
        });
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    // ----- Documents -----

    [Fact]
    public async Task A_NeListePas_NeLitPas_LesDocumentsDeB()
    {
        using var ctx = NewContext();
        var ctl = Documents(ctx, _a.UserId);
        Assert.IsType<NotFoundResult>((await ctl.GetAll(_b.DashboardId, null, null, null)).Result);
        Assert.IsType<NotFoundResult>((await ctl.GetById(_documentB)).Result);
    }

    [Fact]
    public async Task A_NObtientPas_LeContenuDUnDocumentDeB()
    {
        using var ctx = NewContext();
        Assert.IsType<NotFoundResult>(await Documents(ctx, _a.UserId).GetContent(_documentB));
    }

    [Fact]
    public async Task A_NeModifiePas_NeSupprimePas_UnDocumentDeB()
    {
        using var ctx = NewContext();
        var ctl = Documents(ctx, _a.UserId);
        Assert.IsType<NotFoundResult>((await ctl.Update(_documentB, new UpdateDocumentDto { Kind = DocumentKind.Autre })).Result);
        Assert.IsType<NotFoundResult>(await ctl.Delete(_documentB));

        using var check = NewContext();
        var d = await check.Documents.SingleAsync(x => x.Id == _documentB);
        Assert.Equal(DocumentKind.Facture, d.Kind);
        Assert.True(File.Exists(Path.Combine(_root, d.StoredPath)));
    }

    [Fact]
    public async Task A_NEnvoiePas_DansLeDashboardDeB_EtRienNeResteSurLeDisque()
    {
        using var ctx = NewContext();
        var result = await Documents(ctx, _a.UserId).Upload(new UploadDocumentDto
        {
            DashboardId = _b.DashboardId, Kind = DocumentKind.Autre,
            File = TestHousehold.FormFile(TestHousehold.PdfBytes("intrus"), "intrus.pdf"),
        }, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(1, await ctx.Documents.CountAsync(d => d.DashboardId == _b.DashboardId));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".incoming")));
    }

    [Fact]
    public async Task A_NeRattachePas_SonDocument_AUneEcheanceDeB()
    {
        using var ctx = NewContext();
        var result = await Documents(ctx, _a.UserId).Upload(new UploadDocumentDto
        {
            DashboardId = _a.DashboardId, EcheanceId = _echeanceB, Kind = DocumentKind.Facture,
            File = TestHousehold.FormFile(TestHousehold.PdfBytes("a-vers-b"), "x.pdf"),
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(0, await ctx.Documents.CountAsync(d => d.DashboardId == _a.DashboardId));
    }

    [Fact]
    public async Task B_LitLeContenu_EnLigne_SousLeTypeDeduit()
    {
        using var ctx = NewContext();
        var ctl = Documents(ctx, _b.UserId);
        var result = await ctl.GetContent(_documentB);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        var disposition = ctl.Response.Headers.ContentDisposition.ToString();
        Assert.StartsWith("inline", disposition);
        Assert.Contains("avertissement.pdf", disposition);
        await file.FileStream.DisposeAsync();
    }

    [Fact]
    public async Task LeNomSurDisque_VientDeLId_PasDuClient()
    {
        using var ctx = NewContext();
        var result = await Documents(ctx, _a.UserId).Upload(new UploadDocumentDto
        {
            DashboardId = _a.DashboardId, Kind = DocumentKind.Autre,
            File = TestHousehold.FormFile(TestHousehold.PdfBytes("traversal"), "../../../etc/passwd.exe", "text/html"),
        }, CancellationToken.None);

        var dto = (DocumentDto)((CreatedAtActionResult)result.Result!).Value!;
        Assert.Equal("passwd.exe", dto.OriginalFileName);
        Assert.Equal("application/pdf", dto.ContentType);

        var row = await ctx.Documents.SingleAsync(d => d.Id == dto.Id);
        Assert.Equal($"{DateTime.UtcNow.Year}/{dto.Id}.pdf", row.StoredPath);
        Assert.True(File.Exists(Path.Combine(_root, row.StoredPath)));
        Assert.False(Directory.Exists(Path.Combine(_root, "etc")));
    }

    [Fact]
    public async Task UnExecutableRenomme_EstRefuse_415()
    {
        using var ctx = NewContext();
        var mz = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00 };
        var result = await Documents(ctx, _a.UserId).Upload(new UploadDocumentDto
        {
            DashboardId = _a.DashboardId, Kind = DocumentKind.Autre,
            File = TestHousehold.FormFile(mz, "facture.pdf", "application/pdf"),
        }, CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(415, status.StatusCode);
        Assert.Equal(0, await ctx.Documents.CountAsync(d => d.DashboardId == _a.DashboardId));
    }

    [Fact]
    public async Task LeMemeContenu_DansLeMemeDashboard_Rend409_AvecLExistant()
    {
        using var ctx = NewContext();
        var result = await Documents(ctx, _b.UserId).Upload(new UploadDocumentDto
        {
            DashboardId = _b.DashboardId, Kind = DocumentKind.Autre,
            File = TestHousehold.FormFile(TestHousehold.PdfBytes("b"), "copie.pdf"),
        }, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(_documentB, ((DuplicateDocumentDto)conflict.Value!).ExistingDocumentId);
        Assert.Equal(1, await ctx.Documents.CountAsync(d => d.DashboardId == _b.DashboardId));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".incoming")));
    }

    [Fact]
    public async Task LeMemeContenu_DansUnAutreDashboard_EstAccepte()
    {
        using var ctx = NewContext();
        var result = await Documents(ctx, _a.UserId).Upload(new UploadDocumentDto
        {
            DashboardId = _a.DashboardId, Kind = DocumentKind.Autre,
            File = TestHousehold.FormFile(TestHousehold.PdfBytes("b"), "copie.pdf"),
        }, CancellationToken.None);
        Assert.IsType<CreatedAtActionResult>(result.Result);
    }

    [Fact]
    public async Task QuotaAtteint_Rend507_EtNeLaisseRien()
    {
        var (storage, options, root) = TestHousehold.TempStorage(quota: 10);
        try
        {
            using var ctx = NewContext();
            var ctl = new DocumentController(ctx, storage, options) { ControllerContext = TestHousehold.As(_a.UserId) };
            var result = await ctl.Upload(new UploadDocumentDto
            {
                DashboardId = _a.DashboardId, Kind = DocumentKind.Autre,
                File = TestHousehold.FormFile(TestHousehold.PdfBytes("quota"), "q.pdf"),
            }, CancellationToken.None);

            var status = Assert.IsType<ObjectResult>(result.Result);
            Assert.Equal(507, status.StatusCode);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, ".incoming")));
        }
        finally { TestHousehold.RemoveTemp(root); }
    }

    [Fact]
    public async Task FichierDisparuDuDisque_Rend410()
    {
        using var ctx = NewContext();
        var row = await ctx.Documents.SingleAsync(d => d.Id == _documentB);
        File.Delete(Path.Combine(_root, row.StoredPath));

        var result = await Documents(ctx, _b.UserId).GetContent(_documentB);
        Assert.Equal(410, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task SupprimerLEcheance_DetacheLeDocument_SansLEffacer()
    {
        using var ctx = NewContext();
        Assert.IsType<NoContentResult>(await Echeances(ctx, _b.UserId).Delete(_echeanceB));

        using var check = NewContext();
        var d = await check.Documents.SingleAsync(x => x.Id == _documentB);
        Assert.Null(d.EcheanceId);
        Assert.True(File.Exists(Path.Combine(_root, d.StoredPath)));
    }

    [Fact]
    public async Task SupprimerLeDocument_EffaceLaLigne_PuisLeFichier()
    {
        using var ctx = NewContext();
        var stored = (await ctx.Documents.SingleAsync(d => d.Id == _documentB)).StoredPath;
        Assert.IsType<NoContentResult>(await Documents(ctx, _b.UserId).Delete(_documentB));

        Assert.False(await ctx.Documents.AnyAsync(d => d.Id == _documentB));
        Assert.False(File.Exists(Path.Combine(_root, stored)));
    }
}
