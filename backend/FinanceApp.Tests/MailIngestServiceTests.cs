using System.Net.Sockets;
using System.Text.Json;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.API.Services.Mail;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static FinanceApp.Tests.MailIngestTestSupport;

namespace FinanceApp.Tests;

/// <summary>
/// Le service de fond sur une base SQLite en mémoire au vrai schéma, un lecteur simulé et une racine de
/// documents temporaire. Un mail accepté range son PDF en Facture sans utilisateur, le même mail relu ne
/// double rien, un mail rejeté ou sans PDF est ignoré, une boîte injoignable se classe sans toucher à
/// LastSyncAt, un message en échec ne bloque pas les suivants, et rien de persisté ne porte un objet, un
/// nom de fichier, une adresse d'expéditeur ni un Message-ID.
/// </summary>
public class MailIngestServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;
    private readonly DocumentStorage _storage;
    private readonly DocumentStorageOptions _storageOptions;
    private readonly string _root;
    private readonly Household _h;

    public MailIngestServiceTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
        (_storage, _storageOptions, _root) = TestHousehold.TempStorage();
        using var ctx = new AppDbContext(_options);
        _h = TestHousehold.SeedAsync(ctx, "mail@test.local").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _connection.Dispose();
        TestHousehold.RemoveTemp(_root);
    }

    private AppDbContext NewContext() => new(_options);

    private (MailIngestService Service, IMailReader Reader, FixedTimeProvider Clock) Build(IMailReader? reader = null, MailIngestOptions? options = null)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton(_storage);
        services.AddSingleton(_storageOptions);
        services.AddScoped<DocumentDeposit>();
        var provider = services.BuildServiceProvider();

        reader ??= new FakeMailReader();
        var clock = new FixedTimeProvider(AgendaTestSupport.Now);
        var service = new MailIngestService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            reader,
            Microsoft.Extensions.Options.Options.Create(options ?? Options(_h.DashboardId)),
            Microsoft.Extensions.Options.Options.Create(AgendaTestSupport.Household()),
            clock,
            NullLogger<MailIngestService>.Instance);
        return (service, reader, clock);
    }

    private async Task<MailSource> SourceAsync()
    {
        using var ctx = NewContext();
        return await ctx.MailSources.AsNoTracking().SingleAsync(s => s.DashboardId == _h.DashboardId);
    }

    private async Task<List<Document>> DocumentsAsync()
    {
        using var ctx = NewContext();
        return await ctx.Documents.AsNoTracking().Where(d => d.DashboardId == _h.DashboardId).OrderBy(d => d.Id).ToListAsync();
    }

    private static void AssertNoMailContent(MailSource source)
    {
        var json = JsonSerializer.Serialize(new { source.LastError, source.LastSyncStatus, source.Address });
        Assert.DoesNotContain("Décompte", json);
        Assert.DoesNotContain("decompte", json);
        Assert.DoesNotContain("proecoles", json);
        Assert.DoesNotContain(".pdf", json);
        Assert.DoesNotContain("mail.test.invalid", json);
    }

    [Fact]
    public async Task MailAccepteAvecPdf_RangeUneFacture_SansUtilisateur_EtPoseLaSource()
    {
        var reader = new FakeMailReader().With(Mail("u1", attachments: new[] { Pdf("decompte-septembre.pdf", "sept") }));
        var (service, _, clock) = Build(reader);

        var summary = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(new MailRunSummary(Seen: 1, Processed: 1, Ignored: 0, Failed: 0, Created: 1, Duplicates: 0, SkippedAttachments: 0), summary);
        Assert.Equal(MailOutcome.Processed, reader.Outcomes["u1"]);

        var docs = await DocumentsAsync();
        var doc = Assert.Single(docs);
        Assert.Equal(DocumentKind.Facture, doc.Kind);
        Assert.Equal(DocumentSource.Mail, doc.Source);
        Assert.Null(doc.UploadedByUserId);
        Assert.Null(doc.EcheanceId);
        Assert.Equal(2026, doc.FiscalYear);
        Assert.Equal("decompte-septembre.pdf", doc.OriginalFileName);
        Assert.Equal("application/pdf", doc.ContentType);
        Assert.Equal("<u1@mail.test.invalid>", doc.MailMessageId);
        Assert.True(File.Exists(Path.Combine(_root, doc.StoredPath)));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".incoming")));

        var source = await SourceAsync();
        Assert.Equal(Mailbox, source.Address);
        Assert.Equal(MailSyncStatus.Ok, source.LastSyncStatus);
        Assert.Null(source.LastError);
        Assert.Equal(clock.Now.UtcDateTime, source.LastAttemptAt);
        Assert.Equal(clock.Now.UtcDateTime, source.LastSyncAt);
        Assert.Equal(clock.Now.UtcDateTime, source.LastDepositAt);
        Assert.Equal(1, source.DepositedCount);
        AssertNoMailContent(source);
    }

    [Fact]
    public async Task LeMemeMailRelu_NeDoublePas_EtResteTraite()
    {
        var reader = new FakeMailReader().With(Mail("u1", attachments: new[] { Pdf("decompte.pdf", "meme") }));
        var (service, _, clock) = Build(reader);

        await service.RunOnceAsync(CancellationToken.None);
        clock.Now = AgendaTestSupport.Now.AddHours(6);
        var second = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, second.Seen);
        Assert.Equal(1, second.Processed);
        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Duplicates);
        Assert.Equal(MailOutcome.Processed, reader.Outcomes["u1"]);
        Assert.Single(await DocumentsAsync());

        var source = await SourceAsync();
        Assert.Equal(1, source.DepositedCount);
        Assert.Equal(AgendaTestSupport.Now.UtcDateTime, source.LastDepositAt);
        Assert.Equal(AgendaTestSupport.Now.AddHours(6).UtcDateTime, source.LastSyncAt);
        Assert.Equal(MailSyncStatus.Ok, source.LastSyncStatus);
    }

    [Fact]
    public async Task MailRejete_EstIgnore_SansLigne()
    {
        var reader = new FakeMailReader().With(
            Mail("poux", subject: "Information poux", attachments: new[] { Pdf("poux.pdf", "poux") }),
            Mail("tiers", from: "tiers@ailleurs.invalid", attachments: new[] { Pdf("facture.pdf", "tiers") }),
            Mail("direct", forwardedFor: null, attachments: new[] { Pdf("facture.pdf", "direct") }));
        var (service, _, _) = Build(reader);

        var summary = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(3, summary.Seen);
        Assert.Equal(3, summary.Ignored);
        Assert.Equal(0, summary.Processed);
        Assert.Equal(0, summary.Created);
        Assert.All(reader.Outcomes.Values, o => Assert.Equal(MailOutcome.Ignored, o));
        Assert.Empty(await DocumentsAsync());
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".incoming")));
        var source = await SourceAsync();
        Assert.Equal(MailSyncStatus.Ok, source.LastSyncStatus);
        Assert.Equal(0, source.DepositedCount);
        Assert.Null(source.LastDepositAt);
    }

    [Fact]
    public async Task MailAccepteAvecPngSeul_EstIgnore_SansLigne_PieceComptee()
    {
        var reader = new FakeMailReader().With(Mail("png", attachments: new[] { Png("logo.png") }));
        var (service, _, _) = Build(reader);

        var summary = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, summary.Ignored);
        Assert.Equal(1, summary.SkippedAttachments);
        Assert.Equal(0, summary.Created);
        Assert.Equal(MailOutcome.Ignored, reader.Outcomes["png"]);
        Assert.Empty(await DocumentsAsync());
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".incoming")));
    }

    [Fact]
    public async Task PdfEtPng_DansLeMemeMail_SeulLePdfEstRange()
    {
        var reader = new FakeMailReader().With(Mail("mix", attachments: new[] { Png("logo.png"), Pdf("decompte.pdf", "mix"), new IncomingAttachment("vide.pdf", () => new MemoryStream()) }));
        var (service, _, _) = Build(reader);

        var summary = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(1, summary.Created);
        Assert.Equal(2, summary.SkippedAttachments);
        Assert.Equal(MailOutcome.Processed, reader.Outcomes["mix"]);
        Assert.Single(await DocumentsAsync());
    }

    [Fact]
    public async Task BoiteInjoignable_AuthError_LaisseLastSyncAtNul_PoseLastAttemptAt()
    {
        var reader = new FakeMailReader { OpenFailure = new MailKit.Security.AuthenticationException("Invalid credentials (Failure)") };
        var (service, _, clock) = Build(reader);

        var summary = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(MailRunSummary.Empty, summary);
        var source = await SourceAsync();
        Assert.Equal(MailSyncStatus.AuthError, source.LastSyncStatus);
        Assert.Null(source.LastSyncAt);
        Assert.Equal(clock.Now.UtcDateTime, source.LastAttemptAt);
        Assert.NotNull(source.LastError);
        Assert.DoesNotContain("Invalid credentials", source.LastError);
        Assert.True(source.LastError!.Length <= MailIngestService.LastErrorMaxLength);
        AssertNoMailContent(source);
    }

    [Fact]
    public async Task BoiteInjoignable_Reseau_ConnectionError_PuisRetourOk_GardeLeDernierSucces()
    {
        var reader = new FakeMailReader().With(Mail("u1", attachments: new[] { Pdf("decompte.pdf", "ok") }));
        var (service, _, clock) = Build(reader);
        await service.RunOnceAsync(CancellationToken.None);
        var succes = clock.Now.UtcDateTime;

        clock.Now = AgendaTestSupport.Now.AddHours(6);
        reader.OpenFailure = new SocketException((int)SocketError.HostNotFound);
        await service.RunOnceAsync(CancellationToken.None);

        var source = await SourceAsync();
        Assert.Equal(MailSyncStatus.ConnectionError, source.LastSyncStatus);
        Assert.Equal(succes, source.LastSyncAt);
        Assert.Equal(clock.Now.UtcDateTime, source.LastAttemptAt);
        Assert.Contains("SocketException", source.LastError);

        reader.OpenFailure = new InvalidOperationException("autre chose");
        await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal(MailSyncStatus.Error, (await SourceAsync()).LastSyncStatus);
    }

    [Fact]
    public async Task UnMailDontOpenLeve_TombeEnFailed_LeSuivantPasse_StatutOkAvecLastError()
    {
        var reader = new FakeMailReader().With(
            Mail("casse", attachments: new[] { new IncomingAttachment("casse.pdf", () => throw new IOException("flux coupé")) }),
            Mail("sain", attachments: new[] { Pdf("decompte.pdf", "sain") }));
        var (service, _, _) = Build(reader);

        var summary = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(2, summary.Seen);
        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Processed);
        Assert.Equal(1, summary.Created);
        Assert.Equal(MailOutcome.Failed, reader.Outcomes["casse"]);
        Assert.Equal(MailOutcome.Processed, reader.Outcomes["sain"]);
        Assert.Single(await DocumentsAsync());
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, ".incoming")));

        var source = await SourceAsync();
        Assert.Equal(MailSyncStatus.Ok, source.LastSyncStatus);
        Assert.NotNull(source.LastSyncAt);
        Assert.Equal("Un message n'a pas pu être traité (IOException).", source.LastError);
        Assert.Equal(1, source.DepositedCount);
        AssertNoMailContent(source);
    }

    [Fact]
    public async Task QuotaAtteint_FaitTomberLeMailEnFailed_PasDeLigne()
    {
        var (storage, options, root) = TestHousehold.TempStorage(quota: 10);
        try
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
            services.AddSingleton(storage);
            services.AddSingleton(options);
            services.AddScoped<DocumentDeposit>();
            var reader = new FakeMailReader().With(Mail("gros", attachments: new[] { Pdf("decompte.pdf", "gros") }));
            var service = new MailIngestService(
                services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), reader,
                Microsoft.Extensions.Options.Options.Create(Options(_h.DashboardId)),
                Microsoft.Extensions.Options.Options.Create(AgendaTestSupport.Household()),
                new FixedTimeProvider(AgendaTestSupport.Now), NullLogger<MailIngestService>.Instance);

            var summary = await service.RunOnceAsync(CancellationToken.None);

            Assert.Equal(1, summary.Failed);
            Assert.Equal(MailOutcome.Failed, reader.Outcomes["gros"]);
            Assert.Empty(await DocumentsAsync());
            Assert.Empty(Directory.GetFiles(Path.Combine(root, ".incoming")));
            Assert.Contains("InvalidOperationException", (await SourceAsync()).LastError);
        }
        finally { TestHousehold.RemoveTemp(root); }
    }

    [Fact]
    public async Task NonConfigure_RendUnResumeVide_EtNEcritRien()
    {
        var reader = new FakeMailReader().With(Mail("u1", attachments: new[] { Pdf("decompte.pdf", "x") }));
        var (service, _, _) = Build(reader, new MailIngestOptions());

        var summary = await service.RunOnceAsync(CancellationToken.None);

        Assert.Equal(MailRunSummary.Empty, summary);
        Assert.Equal(0, reader.Opens);
        Assert.False(service.IsConfigured);
        Assert.Null(service.ConfiguredDashboardId);
        using var ctx = NewContext();
        Assert.Empty(await ctx.MailSources.ToListAsync());
        Assert.Empty(await ctx.Documents.ToListAsync());
    }

    [Fact]
    public async Task TryRunOnce_RendNull_QuandUnReleveEstEnCours()
    {
        var gate = new TaskCompletionSource();
        var reader = new BlockingReader(gate.Task);
        var (service, _, _) = Build(reader);

        var first = service.RunOnceAsync(CancellationToken.None);
        await reader.Entered.Task;
        Assert.Null(await service.TryRunOnceAsync(CancellationToken.None));

        gate.SetResult();
        await first;
        Assert.NotNull(await service.TryRunOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Annulation_RemonteAuLieuDEtreAvalee()
    {
        var reader = new FakeMailReader().With(Mail("u1", attachments: new[] { Pdf("decompte.pdf", "x") }));
        var (service, _, _) = Build(reader);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunOnceAsync(cts.Token));
    }

    [Fact]
    public void Classify_AuthErreurReseauReste()
    {
        Assert.Equal(MailSyncStatus.AuthError, MailIngestService.Classify(new MailKit.Security.AuthenticationException()).Status);
        Assert.Equal(MailSyncStatus.ConnectionError, MailIngestService.Classify(new SocketException()).Status);
        Assert.Equal(MailSyncStatus.ConnectionError, MailIngestService.Classify(new TimeoutException()).Status);
        Assert.Equal(MailSyncStatus.ConnectionError, MailIngestService.Classify(new IOException()).Status);
        Assert.Equal(MailSyncStatus.ConnectionError, MailIngestService.Classify(new MailKit.Net.Imap.ImapProtocolException()).Status);
        Assert.Equal(MailSyncStatus.Error, MailIngestService.Classify(new InvalidOperationException("x")).Status);
        Assert.DoesNotContain("x", MailIngestService.Classify(new InvalidOperationException("x")).Error.Replace("Exception", ""));
    }

    /// <summary>Un lecteur qui reste dans la boîte tant qu'on ne le libère pas, pour tester le sémaphore.</summary>
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
