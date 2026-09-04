using System.Security.Claims;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.Tests;

/// <summary>
/// Un ménage minimal pour les tests des échéances et des documents : un utilisateur, son dashboard,
/// un compte logique lié. Base SQLite en mémoire sur le vrai schéma (FK, index filtrés, cascades).
/// </summary>
internal sealed record Household(int UserId, int DashboardId, int AccountId);

internal static class TestHousehold
{
    public static (SqliteConnection Connection, DbContextOptions<AppDbContext> Options) OpenInMemory()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        using var ctx = new AppDbContext(options);
        ctx.Database.EnsureCreated();
        return (connection, options);
    }

    public static async Task<Household> SeedAsync(AppDbContext ctx, string email)
    {
        var user = new User { Email = email, PasswordHash = "x" };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();

        var account = new Account { Name = "Commun", UserId = user.Id, IsPrimary = true };
        var dashboard = new Dashboard { Name = "Personnel", CreatorId = user.Id, IsPersonal = true };
        ctx.Accounts.Add(account);
        ctx.Dashboards.Add(dashboard);
        await ctx.SaveChangesAsync();

        ctx.DashboardMembers.Add(new DashboardMember { DashboardId = dashboard.Id, UserId = user.Id });
        ctx.DashboardAccounts.Add(new DashboardAccount { DashboardId = dashboard.Id, AccountId = account.Id });
        await ctx.SaveChangesAsync();

        return new Household(user.Id, dashboard.Id, account.Id);
    }

    /// <summary>Le contexte d'un appel authentifié : la claim que lit ApiControllerBase.GetUserId().</summary>
    public static ControllerContext As(int userId)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "Test");
        return new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
    }

    /// <summary>Un stockage sur un dossier temporaire propre à ce test.</summary>
    public static (DocumentStorage Storage, DocumentStorageOptions Options, string Root) TempStorage(long? maxFileBytes = null, long? quota = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "finance-tests", Guid.NewGuid().ToString("N"), "documents");
        var options = new DocumentStorageOptions
        {
            Root = root,
            MaxFileBytes = maxFileBytes ?? DocumentStorageOptions.DefaultMaxFileBytes,
            QuotaBytesPerDashboard = quota ?? DocumentStorageOptions.DefaultQuotaBytesPerDashboard,
        };
        return (new DocumentStorage(options), options, root);
    }

    public static void RemoveTemp(string root)
    {
        try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Un PDF minimal mais valide à l'œil de FileSignature, dont le contenu varie avec la graine.</summary>
    public static byte[] PdfBytes(string seed = "a") =>
        "%PDF-1.4\n%"u8.ToArray().Concat(System.Text.Encoding.UTF8.GetBytes($"seed:{seed}\n%%EOF\n")).ToArray();

    public static IFormFile FormFile(byte[] content, string fileName, string contentType = "application/octet-stream")
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName) { Headers = new HeaderDictionary(), ContentType = contentType };
    }
}
