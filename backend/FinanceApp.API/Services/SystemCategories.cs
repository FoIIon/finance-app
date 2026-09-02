using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

/// <summary>
/// Les catégories que le code cherche par son nom, parce qu'il a besoin d'y ranger des transactions
/// sans que l'utilisateur ait rien à configurer. Créées à la demande : la base de dev n'a que les dix
/// catégories du seed, la prod en a vingt-six, et coder un Id en dur ferait diverger les deux.
/// </summary>
public static class SystemCategories
{
    /// <summary>Défaut quand aucune règle ne matche.</summary>
    public const string Autres = "Autres";

    /// <summary>
    /// Les deux jambes d'un mouvement entre comptes de la famille : compte joint vers compte perso,
    /// banque vers courtier. Hors bilan mensuel, parce qu'y compter les deux jambes gonfle à la fois
    /// les revenus et les dépenses du même montant (audit du 27/08/2026).
    /// </summary>
    public const string VirementInterne = "Virement interne";

    /// <summary>
    /// Achats et ventes de titres. Marquée transfert (l'argent ne quitte pas le patrimoine) mais
    /// gardée DANS le bilan mensuel : décider d'acheter 200 € de Bitcoin est une mise de côté du mois,
    /// contrairement au balayage automatique du compte joint.
    /// </summary>
    public const string Investissement = "Investissement";

    /// <summary>
    /// Trouve la catégorie par son nom, ou la crée. Les catégories système sont marquées IsDefault
    /// pour être visibles de tous les dashboards, comme les dix du seed.
    /// </summary>
    public static async Task<int> GetOrCreateIdAsync(
        AppDbContext context,
        string name,
        string icon,
        string color,
        bool isTransfer,
        bool excludeFromMonthlyReport)
    {
        var existing = await context.Categories.FirstOrDefaultAsync(c => c.Name == name && c.IsDefault);
        if (existing != null) return existing.Id;

        var created = new Category
        {
            Name = name,
            Icon = icon,
            Color = color,
            IsDefault = true,
            IsTransfer = isTransfer,
            ExcludeFromMonthlyReport = excludeFromMonthlyReport,
        };
        context.Categories.Add(created);
        await context.SaveChangesAsync();
        return created.Id;
    }

    /// <summary>« Virement interne », créée au besoin.</summary>
    public static Task<int> VirementInterneIdAsync(AppDbContext context) =>
        GetOrCreateIdAsync(context, VirementInterne, "🔁", "#9CA3AF", isTransfer: true, excludeFromMonthlyReport: true);

    /// <summary>« Investissement », créée au besoin.</summary>
    public static Task<int> InvestissementIdAsync(AppDbContext context) =>
        GetOrCreateIdAsync(context, Investissement, "📈", "#10B981", isTransfer: true, excludeFromMonthlyReport: false);

    /// <summary>« Autres », la catégorie du seed où tombe ce qu'aucune règle ne classe. Retrouvée par son nom, jamais par son Id.</summary>
    public static Task<int> AutresIdAsync(AppDbContext context) =>
        GetOrCreateIdAsync(context, Autres, "D83DDCE6", "#C9CBCF", isTransfer: false, excludeFromMonthlyReport: false);
}
