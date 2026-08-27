using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

/// <summary>
/// Le compte logique « Perso » vers lequel PersoScopeRouter envoie les transactions personnelles de
/// Sébastien. Résolu par son nom, comme les catégories système (SystemCategories) : coder un Id en
/// dur ferait diverger la base de dev (un seul compte au seed) et la prod.
///
/// Le reporting est déjà scopé par dashboard (TransactionController.GetMonthlyReport et consorts filtrent
/// sur t.AccountId ∈ comptes du dashboard). Rattacher ce compte au dashboard Personnel suffit donc à
/// faire apparaître un vrai bilan perso et à exclure ces lignes du Commun, sans une ligne de reporting.
/// </summary>
public static class PersoAccounts
{
    public const string Name = "Perso";

    /// <summary>
    /// Trouve le compte logique Perso de l'utilisateur, ou le crée et le rattache à son dashboard
    /// Personnel. Le dashboard Personnel est le plus ancien de l'utilisateur (celui créé par défaut à
    /// l'inscription, CreateDefaultDashboardForUser), le dashboard Commun étant créé après. S'il n'a
    /// pas de dashboard, le compte est tout de même créé, le rattachement se fera au premier dashboard.
    /// </summary>
    public static async Task<int> GetOrCreatePersoAccountIdAsync(AppDbContext context, int userId)
    {
        var existing = await context.Accounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Name == Name);
        if (existing != null) return existing.Id;

        var account = new Account { Name = Name, UserId = userId };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        // Rattacher au dashboard Personnel (le plus ancien), s'il n'y est pas déjà.
        var personalDashboard = await context.Dashboards
            .Where(d => d.CreatorId == userId)
            .OrderBy(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        if (personalDashboard != null)
        {
            var alreadyLinked = await context.DashboardAccounts
                .AnyAsync(da => da.DashboardId == personalDashboard.Id && da.AccountId == account.Id);
            if (!alreadyLinked)
            {
                context.DashboardAccounts.Add(new DashboardAccount
                {
                    DashboardId = personalDashboard.Id,
                    AccountId = account.Id,
                });
                await context.SaveChangesAsync();
            }
        }

        return account.Id;
    }
}
