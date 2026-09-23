using FinanceApp.API.Controllers;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.API.Services.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static FinanceApp.Tests.MailIngestTestSupport;

namespace FinanceApp.Tests;

/// <summary>
/// Les deux routes de la boîte factures dans DocumentController : GET rend 204 sans source, 200 avec, 404 hors
/// périmètre ; POST refresh rend 404 sans service configuré ou pour un autre dashboard, relève sinon, et 409
/// sans attendre quand un relevé est déjà en cours. Le DTO d'un document porte sa Source.
/// </summary>
public class MailSourceEndpointTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly DocumentStorage _storage;
    private readonly DocumentStorageOptions _storageOptions;
    private readonly string _root;
    private readonly Household _a;
    private readonly Household _b;

    public MailSourceEndpointTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
        (_storage, _storageOptions, _root) = TestHousehold.TempStorage();
        using var ctx = new AppDbContext(_options);
        _a = TestHousehold.SeedAsync(ctx, "a@test.local").GetAwaiter().GetResult();
        _b = TestHousehold.SeedAsync(ctx, "b@test.local").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _connection.Dispose();
        TestHousehold.RemoveTemp(_root);
    }

    private AppDbContext NewContext() => new(_options);

    private DocumentController Documents(AppDbContext ctx, int userId) =>
        new(ctx, _storage, _storageOptions, new DocumentDeposit(ctx, _storage, _storageOptions)) { ControllerContext = TestHousehold.As(userId) };

    private MailIngestService Service(IMailReader reader, MailIngestOptions options)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton(_storage);
        services.AddSingleton(_storageOptions);
        services.AddScoped<DocumentDeposit>();
        return new MailIngestService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), reader,
            Microsoft.Extensions.Options.Options.Create(options),
            Microsoft.Extensions.Options.Options.Create(AgendaTestSupport.Household()),
            new FixedTimeProvider(AgendaTestSupport.Now), NullLogger<MailIngestService>.Instance);
    }

    [Fact]
    public async Task GetMailSource_SansSource_Rend204()
    {
        using var ctx = NewContext();
        var result = await Documents(ctx, _a.UserId).GetMailSource(_a.DashboardId);
        Assert.IsType<NoContentResult>(result.Result);
    }

    [Fact]
    public async Task GetMailSource_NonMembre_Rend404_MemeAvecUneSource()
    {
        using (var ctx = NewContext())
        {
            ctx.MailSources.Add(new MailSource { DashboardId = _b.DashboardId, Address = Mailbox, LastSyncStatus = MailSyncStatus.Ok });
            await ctx.SaveChangesAsync();
        }
        using var check = NewContext();
        Assert.IsType<NotFoundResult>((await Documents(check, _a.UserId).GetMailSource(_b.DashboardId)).Result);
    }

    [Fact]
    public async Task GetMailSource_AvecSource_Rend200_EnUtc_SansSecret()
    {
        var reader = new FakeMailReader().With(Mail("u1", attachments: new[] { Pdf("decompte.pdf", "get") }));
        await Service(reader, Options(_a.DashboardId)).RunOnceAsync(CancellationToken.None);

        using var ctx = NewContext();
        var dto = (MailSourceDto)((OkObjectResult)(await Documents(ctx, _a.UserId).GetMailSource(_a.DashboardId)).Result!).Value!;
        Assert.Equal(Mailbox, dto.Address);
        Assert.Equal("Ok", dto.LastSyncStatus);
        Assert.Null(dto.LastError);
        Assert.Equal(1, dto.DepositedCount);
        Assert.Equal(DateTimeKind.Utc, dto.LastSyncAt!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, dto.LastAttemptAt!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, dto.LastDepositAt!.Value.Kind);

        var json = System.Text.Json.JsonSerializer.Serialize(dto, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        Assert.Matches("\"lastSyncAt\":\"[^\"]+Z\"", json);
        Assert.DoesNotContain("imap.test.invalid", json);
        Assert.DoesNotContain("Décompte", json);
        Assert.DoesNotContain("decompte.pdf", json);

        // Le document rangé par mail se lit avec sa source et sans utilisateur.
        var listed = (List<DocumentDto>)((OkObjectResult)(await Documents(ctx, _a.UserId).GetAll(_a.DashboardId, null, null, null)).Result!).Value!;
        var doc = Assert.Single(listed);
        Assert.Equal("Mail", doc.Source);
        Assert.Null(doc.UploadedByUserId);
    }

    [Fact]
    public async Task UnDepotParFormulaire_PorteLaSourceUpload_EtSonUtilisateur()
    {
        using var ctx = NewContext();
        var uploaded = await Documents(ctx, _a.UserId).Upload(new UploadDocumentDto
        {
            DashboardId = _a.DashboardId, Kind = DocumentKind.Facture,
            File = TestHousehold.FormFile(TestHousehold.PdfBytes("form"), "facture.pdf"),
        }, CancellationToken.None);
        var dto = (DocumentDto)((CreatedAtActionResult)uploaded.Result!).Value!;
        Assert.Equal("Upload", dto.Source);
        Assert.Equal(_a.UserId, dto.UploadedByUserId);
    }

    [Fact]
    public async Task Refresh_NonMembre_Rend404()
    {
        var service = Service(new FakeMailReader(), Options(_b.DashboardId));
        using var ctx = NewContext();
        Assert.IsType<NotFoundResult>((await Documents(ctx, _a.UserId).RefreshMailSource(_b.DashboardId, service, CancellationToken.None)).Result);
    }

    [Fact]
    public async Task Refresh_ServiceNonConfigure_OuAutreDashboard_Rend404_SansReleve()
    {
        var reader = new FakeMailReader();
        using var ctx = NewContext();
        var ctl = Documents(ctx, _a.UserId);

        Assert.IsType<NotFoundResult>((await ctl.RefreshMailSource(_a.DashboardId, Service(reader, new MailIngestOptions()), CancellationToken.None)).Result);
        Assert.IsType<NotFoundResult>((await ctl.RefreshMailSource(_a.DashboardId, Service(reader, Options(_b.DashboardId)), CancellationToken.None)).Result);
        Assert.Equal(0, reader.Opens);
        Assert.Empty(await ctx.MailSources.ToListAsync());
    }

    [Fact]
    public async Task Refresh_Configure_Releve_EtRendLEtatMisAJour()
    {
        var reader = new FakeMailReader().With(Mail("u1", attachments: new[] { Pdf("decompte.pdf", "refresh") }));
        var service = Service(reader, Options(_a.DashboardId));
        using var ctx = NewContext();

        var result = await Documents(ctx, _a.UserId).RefreshMailSource(_a.DashboardId, service, CancellationToken.None);

        var dto = (MailSourceDto)((OkObjectResult)result.Result!).Value!;
        Assert.Equal(1, reader.Opens);
        Assert.Equal("Ok", dto.LastSyncStatus);
        Assert.Equal(1, dto.DepositedCount);
        Assert.Equal(AgendaTestSupport.Now.UtcDateTime, dto.LastSyncAt);
        Assert.Equal(1, await ctx.Documents.CountAsync(d => d.DashboardId == _a.DashboardId));
    }

    [Fact]
    public async Task Refresh_PendantUnReleve_Rend409_SansAttendre()
    {
        var gate = new TaskCompletionSource();
        var reader = new BlockingReader(gate.Task);
        var service = Service(reader, Options(_a.DashboardId));
        var running = service.RunOnceAsync(CancellationToken.None);
        await reader.Entered.Task;

        using var ctx = NewContext();
        var result = await Documents(ctx, _a.UserId).RefreshMailSource(_a.DashboardId, service, CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("Relevé déjà en cours.", conflict.Value);
        gate.SetResult();
        await running;
    }

    private sealed class BlockingReader : IMailReader
    {
        private readonly Task _release;
        public TaskCompletionSource Entered { get; } = new();
        public BlockingReader(Task release) => _release = release;
        public async Task ReadUnseenAsync(MailIngestOptions options, Func<IncomingMail, CancellationToken, Task<MailOutcome>> handle, CancellationToken ct)
        {
            Entered.TrySetResult();
            await _release;
        }
    }
}
