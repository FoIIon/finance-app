using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

/// <summary>
/// Le compte logique « Perso » vers lequel PersoScopeRouter envoie les transactions personnelles de
/// Sébastien. Identifié par Account.IsPersonalScope, jamais par son nom : le nom est libre dans
/// l'écran des comptes, et le résoudre par « Perso » faisait naître un second compte au premier
/// renommage (revue du 28/08/2026). Un index unique par utilisateur garantit qu'il n'y en a qu'un,
/// y compris quand une sync manuelle chevauche la boucle de six heures.
///
/// Le reporting est déjà scopé par dashboard (TransactionController.GetMonthlyReport et consorts filtrent
/// sur t.AccountId ∈ comptes du dashboard). Rattacher ce compte au dashboard Personnel suffit donc à
/// faire apparaître un vrai bilan perso et à exclure ces lignes du Commun, sans une ligne de reporting.
/// </summary>
public static class PersoAccounts
{
    /// <summary>Nom donné au compte à sa création. Purement cosmétique, modifiable ensuite.</summary>
    public const string DefaultName = "Perso";

    /// <summary>
    /// Trouve le compte logique Perso de l'utilisateur, ou le crée. Dans les deux cas, s'assure qu'il
    /// est rattaché au dashboard Personnel, le plus ancien de l'utilisateur (celui créé à l'inscription
    /// par CreateDefaultDashboardForUser, le Commun venant après). Un compte trouvé sans rattachement
    /// (créé par un seed, ou avant que l'utilisateur ait un dashboard) est réparé au passage.
    /// </summary>
    public static async Task<int> GetOrCreatePersoAccountIdAsync(AppDbContext context, int userId)
    {
        var account = await context.Accounts
            .FirstOrDefaultAsync(a => a.UserId == userId && a.IsPersonalScope);

        if (account == null)
        {
            account = new Account { Name = DefaultName, UserId = userId, IsPersonalScope = true };
            context.Accounts.Add(account);
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Un autre passage de sync a gagné la course : l'index unique a refusé notre ligne.
                // On oublie la nôtre et on reprend la sienne.
                context.Entry(account).State = EntityState.Detached;
                account = await context.Accounts
                    .FirstAsync(a => a.UserId == userId && a.IsPersonalScope);
            }
        }

        var personalDashboard = await context.Dashboards
            .FirstOrDefaultAsync(d => d.CreatorId == userId && d.IsPersonal);

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
