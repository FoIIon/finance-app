using System.Text.Json;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Reporting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>
/// Le test qui compte pour le lot Agenda : une source de calendrier et ses occurrences n'entrent jamais
/// dans le bilan. Même patron que BilanInvarianceTests : un ménage avec des transactions variées, tout
/// ReportingService photographié, la source et les occurrences ajoutées, tout recalculé à l'identique.
/// Puis la preuve par la source que BilanClassifier, ProvisionService, ReportingService et BankSyncService
/// ignorent les types du lot.
/// </summary>
public class AgendaInvarianceTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 9, 9, 6, 0, 0, DateTimeKind.Utc);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AgendaInvarianceTests()
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

        var monthly = await reporting.MonthlyReportAsync(accounts, 2026, 9);
        var summary = await reporting.SummaryAsync(h.UserId, accounts, new DateTime(2026, 9, 1), new DateTime(2026, 9, 30, 23, 59, 59), null, true, Now);
        var burndown = await reporting.BurndownAsync(accounts, h.DashboardId, 2026, 9, Now);
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

    /// <summary>Douze lignes sur septembre 2026 : revenus, fixe, variable, remboursement, exceptionnel, épargne, hors bilan, plus une récurrente active.</summary>
    private static async Task SeedAsync(AppDbContext ctx, Household h)
    {
        var epargne = new Category { Name = "Épargne", Icon = "x", Color = "#000", IsTransfer = true, UserId = h.UserId };
        var balayage = new Category { Name = "Balayage livret", Icon = "x", Color = "#000", IsTransfer = true, ExcludeFromMonthlyReport = true, UserId = h.UserId };
        ctx.Categories.AddRange(epargne, balayage);
        await ctx.SaveChangesAsync();

        Transaction T(int day, TransactionType type, decimal amount, int categoryId, string desc, bool fixe = false, bool refund = false, bool exceptional = false) => new()
        {
            AccountId = h.AccountId, CategoryId = categoryId, Type = type, Amount = amount, Description = desc,
            Date = new DateTime(2026, 9, day), IsFixed = fixe, IsRefund = refund, IsExceptional = exceptional,
        };

        ctx.Transactions.AddRange(
            T(1, TransactionType.Income, 3120.45m, 8, "Salaire Seb"),
            T(1, TransactionType.Income, 2210.10m, 8, "Salaire Audrey"),
            T(2, TransactionType.Expense, 1250.00m, 3, "Prêt hypothécaire", fixe: true),
            T(4, TransactionType.Expense, 189.99m, 3, "Électricité", fixe: true),
            T(5, TransactionType.Expense, 143.67m, 1, "Colruyt"),
            T(6, TransactionType.Expense, 27.50m, 5, "Pharmacie"),
            T(7, TransactionType.Income, 27.50m, 5, "Mutuelle", refund: true),
            T(7, TransactionType.Expense, 1200.00m, balayage.Id, "Balayage > 3000"),
            T(8, TransactionType.Expense, 899.00m, 7, "Lave-linge", exceptional: true),
            T(8, TransactionType.Expense, 300.00m, epargne.Id, "Ordre permanent livret"),
            T(9, TransactionType.Expense, 52.40m, 1, "Boucherie"),
            T(9, TransactionType.Expense, 12.30m, 10, "Divers"));

        ctx.RecurringTransactions.Add(new RecurringTransaction
        {
            UserId = h.UserId, DashboardId = h.DashboardId, AccountId = h.AccountId, CategoryId = 3,
            Description = "Assurance habitation", Amount = 62.40m, Type = TransactionType.Expense,
            Frequency = RecurringFrequency.Monthly, DayOfMonth = 20, StartDate = new DateOnly(2025, 1, 20), IsActive = true,
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task UneSourceEtSesOccurrences_NeChangentRien_AuBilan()
    {
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "agenda-bilan@test.local");
            await SeedAsync(ctx, h);
        }

        var before = await SnapshotAsync(h, categoryId: 3);
        using (var doc = JsonDocument.Parse(before.Monthly))
        {
            Assert.Equal(5330.55m, doc.RootElement.GetProperty("Entrees").GetDecimal());
            Assert.NotEqual(0m, doc.RootElement.GetProperty("Fixe").GetDecimal());
            Assert.NotEqual(0m, doc.RootElement.GetProperty("Variable").GetDecimal());
        }
        Assert.Equal(12, before.TransactionCount);

        using (var ctx = NewContext())
        {
            ctx.CalendarSources.Add(new CalendarSource { DashboardId = h.DashboardId, EncryptedUrl = "chiffré", CalendarName = "Famille", LastSyncStatus = CalendarSyncStatus.Ok, LastSyncAt = Now });
            ctx.CalendarOccurrences.AddRange(
                new CalendarOccurrence { DashboardId = h.DashboardId, Uid = "danse@test", OccurrenceStart = new DateTime(2026, 9, 10, 14, 45, 0, DateTimeKind.Utc), LocalDate = new DateOnly(2026, 9, 10), LocalStart = new TimeOnly(16, 45), LocalEnd = new TimeOnly(17, 45), LocalEndDate = new DateOnly(2026, 9, 10), Summary = "Danse Clothilde", Recurrence = CalendarRecurrence.Weekly },
                new CalendarOccurrence { DashboardId = h.DashboardId, Uid = "danse@test", OccurrenceStart = new DateTime(2026, 9, 17, 14, 45, 0, DateTimeKind.Utc), LocalDate = new DateOnly(2026, 9, 17), LocalStart = new TimeOnly(16, 45), LocalEnd = new TimeOnly(17, 45), LocalEndDate = new DateOnly(2026, 9, 17), Summary = "Danse Clothilde", Recurrence = CalendarRecurrence.Weekly },
                new CalendarOccurrence { DashboardId = h.DashboardId, Uid = "weekend@test", OccurrenceStart = new DateTime(2026, 9, 11, 22, 0, 0, DateTimeKind.Utc), LocalDate = new DateOnly(2026, 9, 12), LocalEndDate = new DateOnly(2026, 9, 13), IsAllDay = true, Summary = "Week-end", Recurrence = CalendarRecurrence.None });
            await ctx.SaveChangesAsync();
            Assert.Equal(1, await ctx.CalendarSources.CountAsync());
            Assert.Equal(3, await ctx.CalendarOccurrences.CountAsync());
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
    public async Task SupprimerLeDashboard_EmporteSourceEtOccurrences()
    {
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "cascade@test.local");
            ctx.CalendarSources.Add(new CalendarSource { DashboardId = h.DashboardId, EncryptedUrl = "x" });
            ctx.CalendarOccurrences.Add(new CalendarOccurrence { DashboardId = h.DashboardId, Uid = "u", OccurrenceStart = Now, LocalDate = new DateOnly(2026, 9, 9), LocalEndDate = new DateOnly(2026, 9, 9), Summary = "s" });
            await ctx.SaveChangesAsync();
        }
        using (var ctx = NewContext())
        {
            ctx.Dashboards.Remove(await ctx.Dashboards.SingleAsync(d => d.Id == h.DashboardId));
            await ctx.SaveChangesAsync();
        }
        using var check = NewContext();
        Assert.Equal(0, await check.CalendarSources.CountAsync());
        Assert.Equal(0, await check.CalendarOccurrences.CountAsync());
    }

    [Fact]
    public async Task UneSeuleSourceParDashboard_EtUneOccurrenceParInstant()
    {
        Household h;
        using (var ctx = NewContext())
        {
            h = await TestHousehold.SeedAsync(ctx, "unique@test.local");
            ctx.CalendarSources.Add(new CalendarSource { DashboardId = h.DashboardId, EncryptedUrl = "x" });
            ctx.CalendarOccurrences.Add(new CalendarOccurrence { DashboardId = h.DashboardId, Uid = "u", OccurrenceStart = Now, LocalDate = new DateOnly(2026, 9, 9), LocalEndDate = new DateOnly(2026, 9, 9), Summary = "s" });
            await ctx.SaveChangesAsync();
        }
        using (var dup = NewContext())
        {
            dup.CalendarSources.Add(new CalendarSource { DashboardId = h.DashboardId, EncryptedUrl = "y" });
            await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
        }
        using (var dup = NewContext())
        {
            dup.CalendarOccurrences.Add(new CalendarOccurrence { DashboardId = h.DashboardId, Uid = "u", OccurrenceStart = Now, LocalDate = new DateOnly(2026, 9, 9), LocalEndDate = new DateOnly(2026, 9, 9), Summary = "doublon" });
            await Assert.ThrowsAsync<DbUpdateException>(() => dup.SaveChangesAsync());
        }
    }

    /// <summary>Remonte depuis le dossier de sortie des tests jusqu'à backend/FinanceApp.API.</summary>
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
    [InlineData("Services/BankSyncService.cs")]
    public void LeBilanIgnoreLesTypesDuLotAgenda_ParLaSource(string relative)
    {
        var source = File.ReadAllText(Path.Combine(ApiSourceDir(), relative));
        Assert.NotEmpty(source);
        foreach (var token in new[] { "CalendarSource", "CalendarOccurrence", "CalendarSync", "AgendaItem", "AgendaBuilder", "AgendaProjectors", "IcsOccurrence" })
            Assert.DoesNotContain(token, source);
    }

    [Fact]
    public void LeBilanIgnoreLesTypesDuLotAgenda_ParReflexion()
    {
        var bilan = typeof(BilanClassifier);
        var types = bilan.GetMethods().SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType))
            .SelectMany(t => t.IsGenericType ? t.GetGenericArguments().Append(t) : new[] { t })
            .Select(t => t.Name)
            .ToList();
        Assert.DoesNotContain(types, n => n.StartsWith("Calendar") || n.StartsWith("Agenda"));
    }
}
