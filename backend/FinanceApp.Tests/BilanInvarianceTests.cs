using System.Text.Json;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Reporting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le test qui compte : une échéance n'entre jamais dans le bilan. Un ménage avec une vingtaine de
/// transactions variées, le bilan mensuel, le résumé, le reste du mois et l'historique d'une catégorie
/// calculés par ReportingService, puis cinq échéances (payée, en retard, à venir, sans montant, liée à
/// une transaction existante) et deux documents ajoutés, et tout recalculé : identique à la décimale,
/// et le nombre de transactions n'a pas bougé.
/// </summary>
public class BilanInvarianceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public BilanInvarianceTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() => new(_options);

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

    /// <summary>Vingt-deux lignes sur août 2026 : revenus, remboursement, charges fixes, variable, exceptionnel, épargne, hors bilan, provision.</summary>
    private static async Task<(int epargneId, int horsBilanId, int loyerTxId)> SeedTransactionsAsync(AppDbContext ctx, Household h)
    {
        var epargne = new Category { Name = "Épargne", Icon = "x", Color = "#000", IsTransfer = true, UserId = h.UserId };
        var balayage = new Category { Name = "Balayage livret", Icon = "x", Color = "#000", IsTransfer = true, ExcludeFromMonthlyReport = true, UserId = h.UserId };
        ctx.Categories.AddRange(epargne, balayage);
        await ctx.SaveChangesAsync();

        Transaction T(int day, TransactionType type, decimal amount, int categoryId, string desc,
            bool fixe = false, bool refund = false, bool exceptional = false, bool provisional = false) => new()
        {
            AccountId = h.AccountId, CategoryId = categoryId, Type = type, Amount = amount, Description = desc,
            Date = new DateTime(2026, 8, day), IsFixed = fixe, IsRefund = refund, IsExceptional = exceptional, IsProvisional = provisional,
        };

        var loyer = T(2, TransactionType.Expense, 1250.00m, 3, "Prêt hypothécaire", fixe: true);
        var lines = new List<Transaction>
        {
            T(1, TransactionType.Income, 3120.45m, 8, "Salaire Seb"),
            T(1, TransactionType.Income, 2210.10m, 8, "Salaire Audrey"),
            T(3, TransactionType.Income, 486.20m, 10, "Allocations familiales"),
            loyer,
            T(4, TransactionType.Expense, 189.99m, 3, "Électricité", fixe: true),
            T(5, TransactionType.Expense, 45.00m, 3, "Internet", fixe: true),
            T(6, TransactionType.Income, 62.30m, 3, "Régularisation énergie", fixe: true),
            T(7, TransactionType.Expense, 143.67m, 1, "Colruyt"),
            T(9, TransactionType.Expense, 87.12m, 1, "Delhaize"),
            T(10, TransactionType.Expense, 27.50m, 5, "Pharmacie"),
            T(11, TransactionType.Income, 27.50m, 5, "Mutuelle", refund: true),
            T(12, TransactionType.Expense, 64.00m, 2, "Carburant"),
            T(13, TransactionType.Expense, 899.00m, 7, "Lave-linge", exceptional: true),
            T(14, TransactionType.Expense, 35.90m, 4, "Piscine enfants"),
            T(15, TransactionType.Expense, 300.00m, epargne.Id, "Ordre permanent livret"),
            T(16, TransactionType.Income, 100.00m, epargne.Id, "Retrait livret"),
            T(7, TransactionType.Expense, 1200.00m, balayage.Id, "Balayage > 3000"),
            T(17, TransactionType.Expense, 52.40m, 1, "Boucherie"),
            T(18, TransactionType.Expense, 19.99m, 6, "Fournitures école"),
            T(19, TransactionType.Expense, 12.30m, 10, "Divers"),
            T(25, TransactionType.Income, 3120.45m, 8, "Salaire attendu", provisional: true),
            T(28, TransactionType.Expense, 41.00m, 2, "Parking", exceptional: true),
        };
        ctx.Transactions.AddRange(lines);
        await ctx.SaveChangesAsync();
        Assert.Equal(22, lines.Count);
        return (epargne.Id, balayage.Id, loyer.Id);
    }

    [Fact]
    public async Task LesEcheancesEtLesDocuments_NeChangentRien_AuBilan()
    {
        Household h;
        int loyerTxId;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "bilan@test.local");
            (_, _, loyerTxId) = await SeedTransactionsAsync(ctx, h);
        }

        var before = await SnapshotAsync(h, categoryId: 3);

        // Garde-fou : le bilan de départ n'est pas vide, sinon le test ne prouverait rien.
        using (var doc = JsonDocument.Parse(before.Monthly))
        {
            Assert.Equal(8937.20m, doc.RootElement.GetProperty("Entrees").GetDecimal());
            Assert.NotEqual(0m, doc.RootElement.GetProperty("Fixe").GetDecimal());
            Assert.NotEqual(0m, doc.RootElement.GetProperty("Variable").GetDecimal());
            Assert.NotEqual(0m, doc.RootElement.GetProperty("MisesDeCote").GetDecimal());
            Assert.NotEqual(0m, doc.RootElement.GetProperty("HorsBilan").GetDecimal());
        }
        Assert.Equal(22, before.TransactionCount);

        using (var ctx = NewContext())
        {
            var now = Now;
            ctx.Echeances.AddRange(
                new Echeance { DashboardId = h.DashboardId, Label = "Précompte immobilier", DueDate = new DateOnly(2026, 8, 10), Amount = 1450.00m, PaidAt = now, CreatedByUserId = h.UserId },
                new Echeance { DashboardId = h.DashboardId, Label = "Taxe déchets", DueDate = new DateOnly(2026, 8, 5), Amount = 95.00m, CreatedByUserId = h.UserId },
                new Echeance { DashboardId = h.DashboardId, Label = "Assurance auto", DueDate = new DateOnly(2026, 9, 30), Amount = 612.33m, CreatedByUserId = h.UserId },
                new Echeance { DashboardId = h.DashboardId, Label = "Facture ostéo", DueDate = new DateOnly(2026, 8, 28), Amount = null, CreatedByUserId = h.UserId },
                new Echeance { DashboardId = h.DashboardId, Label = "Prêt août", DueDate = new DateOnly(2026, 8, 2), Amount = 1250.00m, TransactionId = loyerTxId, CreatedByUserId = h.UserId });
            await ctx.SaveChangesAsync();

            var pret = await ctx.Echeances.SingleAsync(e => e.TransactionId == loyerTxId);
            ctx.Documents.AddRange(
                new Document { DashboardId = h.DashboardId, EcheanceId = pret.Id, Kind = DocumentKind.Facture, OriginalFileName = "decompte.pdf", StoredPath = "2026/1.pdf", ContentType = "application/pdf", SizeBytes = 12345, Sha256 = new string('a', 64), UploadedByUserId = h.UserId },
                new Document { DashboardId = h.DashboardId, Kind = DocumentKind.Fiscal, FiscalYear = 2025, OriginalFileName = "aer.pdf", StoredPath = "2026/2.pdf", ContentType = "application/pdf", SizeBytes = 54321, Sha256 = new string('b', 64), UploadedByUserId = h.UserId });
            await ctx.SaveChangesAsync();

            Assert.Equal(5, await ctx.Echeances.CountAsync());
            Assert.Equal(2, await ctx.Documents.CountAsync());
        }

        var after = await SnapshotAsync(h, categoryId: 3);

        Assert.Equal(before.Monthly, after.Monthly);
        Assert.Equal(before.Summary, after.Summary);
        Assert.Equal(before.Burndown, after.Burndown);
        Assert.Equal(before.CategoryHistory, after.CategoryHistory);
        Assert.Equal(before.FlowHistory, after.FlowHistory);
        Assert.Equal(before.TransactionCount, after.TransactionCount);
    }

    [Fact]
    public async Task SupprimerLaTransaction_DetacheLEcheance_QuiRedevientAPayer()
    {
        Household h;
        int loyerTxId;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "detach@test.local");
            (_, _, loyerTxId) = await SeedTransactionsAsync(ctx, h);
            ctx.Echeances.Add(new Echeance { DashboardId = h.DashboardId, Label = "Prêt août", DueDate = new DateOnly(2026, 8, 2), Amount = 1250.00m, TransactionId = loyerTxId, CreatedByUserId = h.UserId });
            await ctx.SaveChangesAsync();
        }

        using (var ctx = NewContext())
        {
            ctx.Transactions.Remove(await ctx.Transactions.SingleAsync(t => t.Id == loyerTxId));
            await ctx.SaveChangesAsync();
        }

        using var check = NewContext();
        var e = await check.Echeances.SingleAsync();
        Assert.Null(e.TransactionId);
        Assert.Equal(21, await check.Transactions.CountAsync());
    }

    [Fact]
    public async Task UneTransaction_NeProuveQuUneEcheance()
    {
        Household h;
        int loyerTxId;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "unique@test.local");
            (_, _, loyerTxId) = await SeedTransactionsAsync(ctx, h);
            ctx.Echeances.Add(new Echeance { DashboardId = h.DashboardId, Label = "Prêt août", DueDate = new DateOnly(2026, 8, 2), TransactionId = loyerTxId, CreatedByUserId = h.UserId });
            await ctx.SaveChangesAsync();
        }

        using var dup = NewContext();
        dup.Echeances.Add(new Echeance { DashboardId = h.DashboardId, Label = "Doublon", DueDate = new DateOnly(2026, 8, 2), TransactionId = loyerTxId, CreatedByUserId = h.UserId });
        await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());

        // Plusieurs échéances sans transaction restent permises : l'index est filtré.
        using var free = NewContext();
        free.Echeances.AddRange(
            new Echeance { DashboardId = h.DashboardId, Label = "A", DueDate = new DateOnly(2026, 9, 1), CreatedByUserId = h.UserId },
            new Echeance { DashboardId = h.DashboardId, Label = "B", DueDate = new DateOnly(2026, 9, 2), CreatedByUserId = h.UserId });
        await free.SaveChangesAsync();
        Assert.Equal(3, await free.Echeances.CountAsync());
    }
}
