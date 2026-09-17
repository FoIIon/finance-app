using System.Text.Json;
using FinanceApp.API.Controllers;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using FinanceApp.API.Services.Calendar;
using FinanceApp.API.Services.Reporting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// L'exécuteur du rapprochement, sur une base SQLite en mémoire au vrai schéma. Ce qu'il doit faire (lier,
/// dater, une seule fois, jamais deux fois la même transaction) et ce qu'il ne doit jamais faire (toucher
/// une provision, un compte d'un autre dashboard, une échéance payée à la main, une ligne de Transactions,
/// ou le bilan).
/// </summary>
public class EcheanceReconciliationServiceTests : IDisposable
{
    private const string Ecole = "BE98068243670693";
    private static readonly string Com = StructuredCommunicationTests.Sc(2026080001);
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public EcheanceReconciliationServiceTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() => new(_options);

    /// <summary>Le fuseau du ménage par défaut, Europe/Brussels : les dates des candidats sont des jours locaux.</summary>
    private static IOptions<HouseholdOptions> Household => Options.Create(new HouseholdOptions());

    private static EcheanceReconciliationService Service(AppDbContext ctx, ILogger<EcheanceReconciliationService>? logger = null) =>
        new(ctx, Household, logger ?? NullLogger<EcheanceReconciliationService>.Instance);

    private static EcheanceController Controller(AppDbContext ctx, int userId) =>
        new(ctx, Microsoft.Extensions.Options.Options.Create(new FinanceApp.API.Services.Calendar.HouseholdOptions())) { ControllerContext = TestHousehold.As(userId) };

    private async Task<int> RunAsync()
    {
        using var ctx = NewContext();
        return await Service(ctx).ReconcileAsync(CancellationToken.None);
    }

    /// <summary>Garde les messages formatés : le compte de rattrapage n'est visible que par le journal.</summary>
    private sealed class CapturingLogger : ILogger<EcheanceReconciliationService>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));
    }

    /// <summary>Une dépense importée. La communication structurée, s'il y en a une, est dans le libellé, comme en prod.</summary>
    private static Transaction Depense(Household h, decimal amount, DateTime date, string desc, string? iban = Ecole, bool provisional = false, int? accountId = null) => new()
    {
        AccountId = accountId ?? h.AccountId, CategoryId = 6, Type = TransactionType.Expense, Amount = amount, Date = date,
        Description = desc, CounterpartyIban = iban, IsImported = true, IsProvisional = provisional,
    };

    /// <summary>« +++123/4567/89002+++ » depuis les douze chiffres, pour écrire un libellé de virement.</summary>
    private static string Formatee(string com) => $"+++{com[..3]}/{com[3..7]}/{com[7..]}+++";

    private static Echeance Facture(Household h, string label, decimal? amount, DateOnly due, string? iban = Ecole, string? com = null, int? transactionId = null, DateTime? paidAt = null) => new()
    {
        DashboardId = h.DashboardId, Label = label, DueDate = due, Amount = amount, CounterpartyIban = iban, StructuredCommunication = com,
        TransactionId = transactionId, PaidAt = paidAt, CreatedByUserId = h.UserId, CreatedAt = Now, UpdatedAt = Now,
    };

    [Fact]
    public async Task IbanEtMontant_TransactionImportee_RapprocheEtDate()
    {
        Household h;
        int txId;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "match@test.local");
            var tx = Depense(h, 2.60m, new DateTime(2026, 8, 28), "Ecole communale");
            ctx.Transactions.AddRange(tx, Depense(h, 1.40m, new DateTime(2026, 8, 28), "Ecole communale"));
            ctx.Echeances.Add(Facture(h, "Repas Alice août", 2.60m, new DateOnly(2026, 8, 31)));
            await ctx.SaveChangesAsync();
            txId = tx.Id;
        }

        Assert.Equal(1, await RunAsync());

        using var check = NewContext();
        var e = await check.Echeances.SingleAsync();
        Assert.Equal(txId, e.TransactionId);
        Assert.NotNull(e.MatchedAt);
        Assert.Null(e.PaidAt);
        Assert.Equal(EcheanceStatus.Payee, EcheanceStatusRules.Of(e, new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public async Task CommunicationStructuree_SansMontant_Rapproche()
    {
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "com@test.local");
            ctx.Transactions.AddRange(
                Depense(h, 61.20m, new DateTime(2026, 8, 5), $"Virement {Formatee(Com)} ostéo", iban: null),
                Depense(h, 61.20m, new DateTime(2026, 8, 5), "Autre chose", iban: null));
            ctx.Echeances.Add(Facture(h, "Facture ostéo", null, new DateOnly(2026, 8, 28), iban: null, com: Com));
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(1, await RunAsync());

        using var check = NewContext();
        var e = await check.Echeances.SingleAsync();
        var tx = await check.Transactions.SingleAsync(t => t.Description.Contains("ostéo"));
        Assert.Equal(tx.Id, e.TransactionId);
    }

    [Fact]
    public async Task DeuxiemePasse_NeChangeRien()
    {
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "idem@test.local");
            ctx.Transactions.Add(Depense(h, 2.60m, new DateTime(2026, 8, 28), "Ecole communale"));
            ctx.Echeances.Add(Facture(h, "Repas", 2.60m, new DateOnly(2026, 8, 31)));
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(1, await RunAsync());
        DateTime updatedAt, matchedAt;
        using (var ctx = NewContext())
        {
            var e = await ctx.Echeances.SingleAsync();
            (updatedAt, matchedAt) = (e.UpdatedAt, e.MatchedAt!.Value);
        }

        Assert.Equal(0, await RunAsync());

        using var check = NewContext();
        var after = await check.Echeances.SingleAsync();
        Assert.Equal(updatedAt, after.UpdatedAt);
        Assert.Equal(matchedAt, after.MatchedAt);
    }

    [Fact]
    public async Task ApresUnpay_LEcheanceNEstPlusDevinee_JusquACeQuUneCleChange()
    {
        Household h;
        int tx1;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "unpay@test.local");
            var tx = Depense(h, 2.60m, new DateTime(2026, 8, 28), "Ecole communale");
            ctx.Transactions.Add(tx);
            ctx.Echeances.Add(Facture(h, "Repas", 2.60m, new DateOnly(2026, 8, 31)));
            await ctx.SaveChangesAsync();
            tx1 = tx.Id;
        }

        Assert.Equal(1, await RunAsync());

        int echeanceId;
        using (var ctx = NewContext())
        {
            echeanceId = (await ctx.Echeances.SingleAsync()).Id;
            Assert.IsType<OkObjectResult>((await Controller(ctx, h.UserId).Unpay(echeanceId)).Result);
        }

        // La passe suivante ne remet rien, et une autre transaction conforme n'y change rien non plus :
        // le geste veut dire « arrête de deviner pour celle-ci », pas « pas celle-là ».
        Assert.Equal(0, await RunAsync());
        int tx2;
        using (var ctx = NewContext())
        {
            var tx = Depense(h, 2.60m, new DateTime(2026, 9, 2), "Ecole communale");
            ctx.Transactions.Add(tx);
            await ctx.SaveChangesAsync();
            tx2 = tx.Id;
        }
        Assert.Equal(0, await RunAsync());
        using (var ctx = NewContext())
        {
            var e = await ctx.Echeances.SingleAsync();
            Assert.Null(e.TransactionId);
            Assert.Null(e.MatchedAt);
            Assert.NotNull(e.AutoMatchRefusedAt);
        }

        // L'utilisateur corrige une clé (ajoute la communication) : le refus tombe, on redevine. Le plus
        // proche de la date limite gagne : tx2 (2 jours) avant tx1 (3 jours).
        using (var ctx = NewContext())
        {
            var result = await Controller(ctx, h.UserId).Update(echeanceId, new FinanceApp.API.DTOs.UpdateEcheanceDto
            {
                Label = "Repas", DueDate = new DateOnly(2026, 8, 31), Amount = 2.60m, CounterpartyIban = Ecole, StructuredCommunication = Com,
            });
            Assert.IsType<OkObjectResult>(result.Result);
        }
        Assert.Equal(1, await RunAsync());
        using var check = NewContext();
        var relue = await check.Echeances.SingleAsync();
        Assert.Equal(tx2, relue.TransactionId);
        Assert.NotEqual(tx1, relue.TransactionId);
        Assert.Null(relue.AutoMatchRefusedAt);
    }

    [Fact]
    public async Task UneTransactionA23h30Utc_EstUnCandidatDuLendemain_DansLeFuseauDuMenage()
    {
        // Le Pi tourne en UTC. Un virement passé le 31 août à 23 h 30 UTC est du 1er septembre à Bruxelles.
        // Date limite au 16 octobre : la fenêtre ordinaire s'ouvre le 1er septembre (45 jours avant). Sur le jour
        // UTC le virement serait la veille, hors fenêtre, et l'échéance resterait à payer.
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "fuseau@test.local");
            ctx.Transactions.Add(Depense(h, 2.60m, new DateTime(2026, 8, 31, 23, 30, 0, DateTimeKind.Utc), "Ecole communale"));
            ctx.Echeances.Add(Facture(h, "Repas", 2.60m, new DateOnly(2026, 10, 16)));
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(1, await RunAsync());

        using var check = NewContext();
        var e = await check.Echeances.SingleAsync();
        Assert.NotNull(e.TransactionId);
        Assert.NotNull(e.MatchedAt);
    }

    [Fact]
    public async Task UneEcheanceTropAncienne_NEstNiRapprochee_NiChargee()
    {
        // Plus de 180 jours après sa date limite, aucun virement ne peut plus prouver l'échéance : elle reste
        // manuelle, et surtout elle n'élargit pas la fenêtre des candidats aux années précédentes.
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "ancienne@test.local");
            ctx.Transactions.AddRange(
                Depense(h, 2.60m, new DateTime(2025, 7, 5), "Ecole communale"),
                Depense(h, 1.40m, DateTime.UtcNow.Date.AddDays(-3), "Ecole communale"));
            ctx.Echeances.AddRange(
                Facture(h, "Repas juin 2025", 2.60m, new DateOnly(2025, 6, 30)),
                Facture(h, "Repas ce mois", 1.40m, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2)));
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(1, await RunAsync());

        using var check = NewContext();
        var ancienne = await check.Echeances.SingleAsync(e => e.Label == "Repas juin 2025");
        var recente = await check.Echeances.SingleAsync(e => e.Label == "Repas ce mois");
        Assert.Null(ancienne.TransactionId);
        Assert.NotNull(recente.TransactionId);
    }

    [Fact]
    public async Task DeuxEcheancesMemeIbanMemeMontant_ChacuneRecoitLaSienne()
    {
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "deux@test.local");
            ctx.Transactions.AddRange(
                Depense(h, 2.60m, new DateTime(2026, 8, 28), "Ecole communale"),
                Depense(h, 2.60m, new DateTime(2026, 9, 29), "Ecole communale"));
            ctx.Echeances.AddRange(
                Facture(h, "Repas août", 2.60m, new DateOnly(2026, 8, 31)),
                Facture(h, "Repas septembre", 2.60m, new DateOnly(2026, 9, 30)));
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(2, await RunAsync());

        using var check = NewContext();
        var echeances = await check.Echeances.Include(e => e.Transaction).OrderBy(e => e.DueDate).ToListAsync();
        Assert.All(echeances, e => Assert.NotNull(e.TransactionId));
        Assert.Equal(new DateTime(2026, 8, 28), echeances[0].Transaction!.Date);
        Assert.Equal(new DateTime(2026, 9, 29), echeances[1].Transaction!.Date);
        Assert.NotEqual(echeances[0].TransactionId, echeances[1].TransactionId);
    }

    [Fact]
    public async Task TransactionDejaLieeAUneEcheance_NEstPasProposeeAUneAutre()
    {
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "liee@test.local");
            var tx = Depense(h, 2.60m, new DateTime(2026, 8, 28), "Ecole communale");
            ctx.Transactions.Add(tx);
            await ctx.SaveChangesAsync();
            ctx.Echeances.AddRange(
                Facture(h, "A, liée à la main", 2.60m, new DateOnly(2026, 8, 31), transactionId: tx.Id),
                Facture(h, "B", 2.60m, new DateOnly(2026, 8, 31)));
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(0, await RunAsync());

        using var check = NewContext();
        var b = await check.Echeances.SingleAsync(e => e.Label == "B");
        Assert.Null(b.TransactionId);
    }

    [Fact]
    public async Task Provision_EtCompteHorsDashboard_NeSontJamaisRapproches()
    {
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "prov@test.local");
            var autreCompte = new Account { Name = "Perso", UserId = h.UserId };
            ctx.Accounts.Add(autreCompte);
            await ctx.SaveChangesAsync();

            ctx.Transactions.AddRange(
                Depense(h, 2.60m, new DateTime(2026, 8, 28), "Ecole communale", provisional: true),
                Depense(h, 2.60m, new DateTime(2026, 8, 28), "Ecole communale", accountId: autreCompte.Id));
            ctx.Echeances.Add(Facture(h, "Repas", 2.60m, new DateOnly(2026, 8, 31)));
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(0, await RunAsync());

        using var check = NewContext();
        Assert.Null((await check.Echeances.SingleAsync()).TransactionId);
    }

    [Fact]
    public async Task EcheancePayeeALaMain_EstIgnoree()
    {
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "main@test.local");
            ctx.Transactions.Add(Depense(h, 2.60m, new DateTime(2026, 8, 28), "Ecole communale"));
            ctx.Echeances.Add(Facture(h, "Repas", 2.60m, new DateOnly(2026, 8, 31), paidAt: Now));
            await ctx.SaveChangesAsync();
        }

        Assert.Equal(0, await RunAsync());

        using var check = NewContext();
        var e = await check.Echeances.SingleAsync();
        Assert.Null(e.TransactionId);
        Assert.Equal(Now, e.PaidAt);
    }

    // ----- Lecture seule sur Transactions -----

    /// <summary>Toutes les lignes de Transactions, toutes les colonnes, telles que SQLite les rend. Deux passes identiques rendent la même chaîne.</summary>
    private async Task<string> TransactionsSnapshotAsync()
    {
        using var ctx = NewContext();
        var rows = await ctx.Transactions.AsNoTracking().OrderBy(t => t.Id).ToListAsync();
        return JsonSerializer.Serialize(rows, Json);
    }

    [Fact]
    public async Task UnePasse_NeChangeAucuneLigneDeTransactions_MemeCelleQuElleRapproche()
    {
        // Un libellé à communication valide (autrefois rattrapé en colonne), un au contrôle faux, un paiement par
        // carte, et une dépense qui va être rapprochée par IBAN : après la passe, la table est identique octet
        // pour octet. Le rapprocheur est en lecture seule sur Transactions.
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "lecture-seule@test.local");
            ctx.Transactions.AddRange(
                Depense(h, 61.20m, new DateTime(2026, 8, 5), $"Virement {Formatee(Com)} ostéo", iban: null),
                Depense(h, 12.00m, new DateTime(2026, 8, 6), "+++123/4567/89012+++ contrôle faux", iban: null),
                Depense(h, 30.00m, new DateTime(2026, 8, 7), "PAIEMENT CARTE ****1234 COLRUYT", iban: null),
                Depense(h, 2.60m, new DateTime(2026, 8, 28), "Ecole communale"));
            ctx.Echeances.AddRange(
                Facture(h, "Ostéo", null, new DateOnly(2026, 8, 28), iban: null, com: Com),
                Facture(h, "Repas", 2.60m, new DateOnly(2026, 8, 31)));
            await ctx.SaveChangesAsync();
        }

        var before = await TransactionsSnapshotAsync();
        // Le sérialiseur échappe les accents : on vérifie sur un mot ASCII que le libellé est bien dans la photo.
        Assert.Contains("Virement", before);

        Assert.Equal(2, await RunAsync());

        Assert.Equal(before, await TransactionsSnapshotAsync());
        using var check = NewContext();
        Assert.Equal(2, await check.Echeances.CountAsync(e => e.TransactionId != null && e.MatchedAt != null));
    }

    [Fact]
    public void LeRapprocheur_NEcritJamaisSurTransactions_ParLaSource()
    {
        // Aucun Add, Remove ni affectation sur une transaction dans l'exécuteur : la lecture des candidats
        // projette des colonnes, et la seule sauvegarde porte des échéances.
        var source = File.ReadAllText(Path.Combine(ApiSourceDir(), "Services/EcheanceReconciliationService.cs"));
        Assert.DoesNotContain("Transactions.Add", source);
        Assert.DoesNotContain("Transactions.Remove", source);
        Assert.DoesNotContain("Transactions.Update", source);
        Assert.DoesNotContain("ExecuteUpdate", source);
        Assert.DoesNotContain("ExecuteDelete", source);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(source, @"SaveChangesAsync\("));
    }

    // ----- Conflit sur l'index unique pendant la sauvegarde -----

    /// <summary>
    /// Entre la lecture des candidats et la sauvegarde de la passe, un autre contexte lie la transaction à une
    /// troisième échéance : c'est l'écriture concurrente que SaveLinksAsync doit absorber.
    /// </summary>
    private sealed class ConcurrentLinkInterceptor : SaveChangesInterceptor
    {
        private readonly DbContextOptions<AppDbContext> _options;
        private readonly int _echeanceId;
        private readonly int _transactionId;
        public bool Fired { get; private set; }

        public ConcurrentLinkInterceptor(DbContextOptions<AppDbContext> options, int echeanceId, int transactionId)
        {
            _options = options;
            _echeanceId = echeanceId;
            _transactionId = transactionId;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            var linksEcheances = eventData.Context!.ChangeTracker.Entries<Echeance>().Any(e => e.State == EntityState.Modified);
            if (linksEcheances && !Fired)
            {
                Fired = true;
                using var other = new AppDbContext(_options);
                var c = await other.Echeances.SingleAsync(e => e.Id == _echeanceId, ct);
                c.TransactionId = _transactionId;
                await other.SaveChangesAsync(ct);
            }
            return result;
        }
    }

    [Fact]
    public async Task ConflitSurLIndexUnique_PendantLaSauvegarde_AbandonneLaPasse_SansLever_EtLaSuivanteReessaie()
    {
        Household h;
        int t1, t2, echeanceC;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "conflit@test.local");
            var tx1 = Depense(h, 2.60m, new DateTime(2026, 8, 28), "Ecole communale");
            var tx2 = Depense(h, 2.60m, new DateTime(2026, 9, 29), "Ecole communale");
            ctx.Transactions.AddRange(tx1, tx2);
            await ctx.SaveChangesAsync();
            (t1, t2) = (tx1.Id, tx2.Id);

            var c = Facture(h, "C, sans clé, liée à la main entre-temps", 2.60m, new DateOnly(2026, 9, 30), iban: null);
            ctx.Echeances.AddRange(
                Facture(h, "A", 2.60m, new DateOnly(2026, 8, 31)),
                Facture(h, "B", 2.60m, new DateOnly(2026, 9, 30)),
                c);
            await ctx.SaveChangesAsync();
            echeanceC = c.Id;
        }

        var interceptor = new ConcurrentLinkInterceptor(_options, echeanceC, t2);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).AddInterceptors(interceptor).Options;
        var logger = new CapturingLogger();

        int matched;
        using (var ctx = new AppDbContext(options))
            matched = await Service(ctx, logger).ReconcileAsync(CancellationToken.None);

        Assert.True(interceptor.Fired);
        // Deux liens trouvés, un en conflit : toute la passe est abandonnée, le compte rendu est zéro, sans exception.
        Assert.Equal(0, matched);
        Assert.Contains(logger.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("2 lien(s) abandonné(s)"));

        using (var check = NewContext())
        {
            var a = await check.Echeances.SingleAsync(e => e.Label == "A");
            var b = await check.Echeances.SingleAsync(e => e.Label == "B");
            var c2 = await check.Echeances.SingleAsync(e => e.Id == echeanceC);
            Assert.Null(a.TransactionId);
            Assert.Null(a.MatchedAt);
            Assert.Null(b.TransactionId);
            Assert.Null(b.MatchedAt);
            Assert.Equal(t2, c2.TransactionId);
            Assert.Equal(2, await check.Transactions.CountAsync());
        }

        // La passe suivante relit les candidats : t2 est prise par C, A reçoit t1, B reste à payer.
        Assert.Equal(1, await RunAsync());
        using var after = NewContext();
        Assert.Equal(t1, (await after.Echeances.SingleAsync(e => e.Label == "A")).TransactionId);
        Assert.Null((await after.Echeances.SingleAsync(e => e.Label == "B")).TransactionId);
    }

    // ----- Invariance du bilan -----

    private sealed record Snapshot(string Monthly, string Summary, string Burndown, string CategoryHistory, string FlowHistory, int TransactionCount);

    private async Task<Snapshot> SnapshotAsync(Household h, int categoryId)
    {
        using var ctx = NewContext();
        var reporting = new ReportingService(ctx, new AccountBalanceService(ctx));
        var accounts = new List<int> { h.AccountId };

        var monthly = await reporting.MonthlyReportAsync(accounts, 2026, 8);
        var summary = await reporting.SummaryAsync(h.UserId, accounts, new DateTime(2026, 8, 1), new DateTime(2026, 8, 31, 23, 59, 59), null, true, Now);
        var burndown = await reporting.BurndownAsync(accounts, h.DashboardId, 2026, 8, Now);
        var history = await reporting.CategoryHistoryAsync(accounts, categoryId, 6, Now);
        var flow = await reporting.CategoryFlowHistoryAsync(accounts, categoryId, 6, null, true, Now);

        return new Snapshot(
            JsonSerializer.Serialize(monthly, Json),
            JsonSerializer.Serialize(summary, Json),
            JsonSerializer.Serialize(burndown, Json),
            JsonSerializer.Serialize(history, Json),
            JsonSerializer.Serialize(flow, Json),
            await ctx.Transactions.CountAsync());
    }

    [Fact]
    public async Task UnePasseQuiRapprocheEtRattrape_NeChangeRien_AuBilan()
    {
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "bilan@test.local");
            var epargne = new Category { Name = "Épargne", Icon = "x", Color = "#000", IsTransfer = true, UserId = h.UserId };
            ctx.Categories.Add(epargne);
            await ctx.SaveChangesAsync();

            Transaction T(int day, TransactionType type, decimal amount, int categoryId, string desc, string? iban = null,
                bool fixe = false, bool refund = false, bool exceptional = false, bool provisional = false) => new()
            {
                AccountId = h.AccountId, CategoryId = categoryId, Type = type, Amount = amount, Description = desc, CounterpartyIban = iban,
                Date = new DateTime(2026, 8, day), IsFixed = fixe, IsRefund = refund, IsExceptional = exceptional, IsProvisional = provisional, IsImported = true,
            };

            ctx.Transactions.AddRange(
                T(1, TransactionType.Income, 3120.45m, 8, "Salaire Seb"),
                T(1, TransactionType.Income, 2210.10m, 8, "Salaire Audrey"),
                T(2, TransactionType.Expense, 1250.00m, 3, "Prêt hypothécaire", fixe: true),
                T(4, TransactionType.Expense, 189.99m, 3, $"Électricité {Formatee(Com)}", iban: "BE68539007547034", fixe: true),
                T(6, TransactionType.Income, 62.30m, 3, "Régularisation énergie", fixe: true),
                T(7, TransactionType.Expense, 143.67m, 1, "Colruyt"),
                T(10, TransactionType.Expense, 27.50m, 5, "Pharmacie"),
                T(11, TransactionType.Income, 27.50m, 5, "Mutuelle", refund: true),
                T(13, TransactionType.Expense, 899.00m, 7, "Lave-linge", exceptional: true),
                T(15, TransactionType.Expense, 300.00m, epargne.Id, "Ordre permanent livret"),
                T(18, TransactionType.Expense, 2.60m, 6, "Ecole communale", iban: Ecole),
                T(18, TransactionType.Expense, 1.40m, 6, "Ecole communale", iban: Ecole),
                T(25, TransactionType.Income, 3120.45m, 8, "Salaire attendu", provisional: true));
            await ctx.SaveChangesAsync();

            ctx.Echeances.AddRange(
                Facture(h, "Repas Alice", 2.60m, new DateOnly(2026, 8, 31)),
                Facture(h, "Repas Hugo", 1.40m, new DateOnly(2026, 8, 31)),
                Facture(h, "Électricité", null, new DateOnly(2026, 8, 10), iban: null, com: Com),
                Facture(h, "Assurance auto, rien en face", 612.33m, new DateOnly(2026, 9, 30), iban: "BE71096123456769"));
            await ctx.SaveChangesAsync();
        }

        var before = await SnapshotAsync(h, categoryId: 3);
        using (var doc = JsonDocument.Parse(before.Monthly))
        {
            Assert.NotEqual(0m, doc.RootElement.GetProperty("Entrees").GetDecimal());
            Assert.NotEqual(0m, doc.RootElement.GetProperty("Fixe").GetDecimal());
            Assert.NotEqual(0m, doc.RootElement.GetProperty("Variable").GetDecimal());
        }
        Assert.Equal(13, before.TransactionCount);

        var transactionsBefore = await TransactionsSnapshotAsync();

        // La passe rapproche trois échéances : deux par IBAN et montant, une par la communication lue dans le libellé.
        Assert.Equal(3, await RunAsync());

        using (var ctx = NewContext())
        {
            Assert.Equal(3, await ctx.Echeances.CountAsync(e => e.TransactionId != null && e.MatchedAt != null));
            var electricite = await ctx.Echeances.Include(e => e.Transaction).SingleAsync(e => e.Label == "Électricité");
            Assert.Equal(189.99m, electricite.Transaction!.Amount);
        }

        Assert.Equal(transactionsBefore, await TransactionsSnapshotAsync());
        var after = await SnapshotAsync(h, categoryId: 3);
        Assert.Equal(before.Monthly, after.Monthly);
        Assert.Equal(before.Summary, after.Summary);
        Assert.Equal(before.Burndown, after.Burndown);
        Assert.Equal(before.CategoryHistory, after.CategoryHistory);
        Assert.Equal(before.FlowHistory, after.FlowHistory);
        Assert.Equal(before.TransactionCount, after.TransactionCount);
    }

    // ----- Garde-fous de source -----

    private static string ApiSourceDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "FinanceApp.API")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "FinanceApp.API");
    }

    [Theory]
    [InlineData("Services/Reporting/BilanClassifier.cs")]
    [InlineData("Services/ProvisionService.cs")]
    [InlineData("Services/Reporting/ReportingService.cs")]
    public void LeBilanIgnoreLeRapprochement_ParLaSource(string relative)
    {
        var source = File.ReadAllText(Path.Combine(ApiSourceDir(), relative));
        Assert.NotEmpty(source);
        foreach (var token in new[] { "Echeance", "EcheanceMatcher", "EcheanceReconciliation", "StructuredCommunication", "PaymentCandidate" })
            Assert.DoesNotContain(token, source);
    }

    [Fact]
    public void LesJournauxDuRapprochement_NePortentNiIbanNiLibelleNiCommunication()
    {
        var source = File.ReadAllText(Path.Combine(ApiSourceDir(), "Services/EcheanceReconciliationService.cs"));
        var lines = source.Split('\n');
        var calls = lines.Select((l, i) => (l, i)).Where(x => x.l.Contains("_logger.Log")).Select(x => x.i).ToList();
        Assert.NotEmpty(calls);
        foreach (var i in calls)
        {
            // L'appel et ses trois lignes suivantes : le message et ses arguments.
            var call = string.Join("\n", lines.Skip(i).Take(4));
            foreach (var forbidden in new[] { "Iban", "Description", "Label", "{Communication", "CounterpartyName", ".StructuredCommunication" })
                Assert.DoesNotContain(forbidden, call);
        }
    }
}
