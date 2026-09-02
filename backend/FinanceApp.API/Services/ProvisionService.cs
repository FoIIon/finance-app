using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

/// <summary>
/// Provisions de revenus/dépenses récurrents : matérialise au 1er du mois une transaction
/// provisionnelle pour chaque récurrente marquée ProvisionAtMonthStart, puis la supprime
/// quand le versement réel est importé (réconciliation).
///
/// La réconciliation estampille le versement réel avec RecurringTransactionId : une transaction
/// réelle ne peut réconcilier qu'une seule provision (deux salaires dans la même catégorie ne se
/// volent plus la réconciliation), et l'historique estampillé sert d'estimation aux provisions suivantes.
///
/// Cas d'usage : le salaire tombe le ~28, sans provision le mois entier paraît en négatif.
/// </summary>
public class ProvisionService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ProvisionService> _logger;

    /// <summary>Un versement réel « correspond » à la provision s'il fait au moins 50% du montant provisionné
    /// (même compte, même catégorie, même type). Évite qu'un petit remboursement catégorisé pareil réconcilie le salaire.</summary>
    private const decimal MatchThreshold = 0.5m;

    /// <summary>Nombre de versements réels passés (estampillés sur la récurrente) utilisés pour estimer la provision.</summary>
    private const int AverageWindow = 3;

    /// <summary>Jours de grâce après la fin du mois : un versement qui glisse au début du mois suivant
    /// (weekend, férié) réconcilie encore la provision du mois précédent. Au-delà, la provision est
    /// expirée et supprimée — l'argent n'est jamais arrivé, le laisser en entrée fausserait le mois.</summary>
    private const int GraceDays = 5;

    public ProvisionService(AppDbContext context, ILogger<ProvisionService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>Crée les provisions manquantes du mois courant puis réconcilie celles dont le réel est arrivé.</summary>
    public async Task RunAsync()
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        await EnsureProvisionsAsync(monthStart, monthEnd);
        await ReconcileProvisionsAsync(now);
    }

    private async Task EnsureProvisionsAsync(DateTime monthStart, DateTime monthEnd)
    {
        var monthStartDateOnly = DateOnly.FromDateTime(monthStart);
        var monthEndDateOnly = DateOnly.FromDateTime(monthEnd.AddDays(-1));

        var recurrings = await _context.RecurringTransactions
            .Where(r => r.IsActive && r.ProvisionAtMonthStart
                     && r.StartDate <= monthEndDateOnly
                     && (r.EndDate == null || r.EndDate >= monthStartDateOnly))
            .ToListAsync();

        foreach (var recurring in recurrings)
        {
            if (recurring.CategoryId == null)
            {
                _logger.LogWarning("Récurrente {Id} provisionnable sans catégorie — provision impossible.", recurring.Id);
                continue;
            }

            // Déjà provisionnée ce mois-ci ?
            var alreadyProvisioned = await _context.Transactions.AnyAsync(t =>
                t.RecurringTransactionId == recurring.Id && t.IsProvisional
                && t.Date >= monthStart && t.Date < monthEnd);
            if (alreadyProvisioned) continue;

            var type = recurring.Type;

            // Compte cible : celui de la récurrente, sinon le premier compte du dashboard
            var accountId = recurring.AccountId ?? await _context.DashboardAccounts
                .Where(da => da.DashboardId == recurring.DashboardId)
                .Select(da => (int?)da.AccountId)
                .FirstOrDefaultAsync();
            if (accountId == null)
            {
                _logger.LogWarning("Récurrente {Id} : aucun compte rattaché au dashboard {DashboardId}.", recurring.Id, recurring.DashboardId);
                continue;
            }

            // Montant estimé : moyenne des derniers versements réels estampillés sur CETTE récurrente
            // (pas de la catégorie entière : deux salaires dans la même catégorie ont des montants différents),
            // fallback montant déclaré de la récurrente tant qu'aucune réconciliation n'a eu lieu.
            var lastRealAmounts = await _context.Transactions
                .Where(t => t.RecurringTransactionId == recurring.Id && !t.IsProvisional)
                .OrderByDescending(t => t.Date)
                .Take(AverageWindow)
                .Select(t => t.Amount)
                .ToListAsync();
            var amount = lastRealAmounts.Any() ? Math.Round(lastRealAmounts.Average(), 2) : recurring.Amount;

            // Le réel est-il déjà arrivé ce mois-ci ? (mois entamé après le versement — pas de provision)
            // Candidats : estampillé sur cette récurrente, ou pas encore attribué à une récurrente
            var landed = await _context.Transactions
                .Where(t => t.AccountId == accountId.Value && !t.IsProvisional
                         && t.CategoryId == recurring.CategoryId.Value && t.Type == type
                         && t.Date >= monthStart && t.Date < monthEnd
                         && t.Amount >= amount * MatchThreshold
                         && (t.RecurringTransactionId == null || t.RecurringTransactionId == recurring.Id))
                .OrderBy(t => t.RecurringTransactionId == recurring.Id ? 0 : 1)
                .FirstOrDefaultAsync();
            if (landed != null)
            {
                // Estampiller pour que l'historique de cette récurrente se construise
                landed.RecurringTransactionId ??= recurring.Id;
                continue;
            }

            _context.Transactions.Add(new Transaction
            {
                Amount = amount,
                Description = $"{recurring.Description} (prévu)",
                Date = monthStart,
                Type = type,
                CategoryId = recurring.CategoryId.Value,
                AccountId = accountId.Value,
                IsImported = false,
                IsProvisional = true,
                // Une dépense récurrente provisionnée est par définition une charge fixe
                IsFixed = type == TransactionType.Expense,
                RecurringTransactionId = recurring.Id,
            });

            _logger.LogInformation("Provision créée : {Description} {Amount}€ ({Month:yyyy-MM}).",
                recurring.Description, amount, monthStart);
        }

        await _context.SaveChangesAsync();
    }

    private async Task ReconcileProvisionsAsync(DateTime now)
    {
        // Toutes les provisions encore ouvertes (mois courant comme passés — un salaire peut tomber après un arrêt du serveur),
        // les plus anciennes d'abord : un versement glissant règle d'abord la provision du mois précédent
        var provisionals = await _context.Transactions
            .Where(t => t.IsProvisional)
            .OrderBy(t => t.Date)
            .ToListAsync();

        // Une transaction réelle ne réconcilie qu'une seule provision par passe
        var usedTransactionIds = new HashSet<int>();

        foreach (var provisional in provisionals)
        {
            var monthStart = new DateTime(provisional.Date.Year, provisional.Date.Month, 1);
            var monthEnd = monthStart.AddMonths(1);
            var matchWindowEnd = monthEnd.AddDays(GraceDays);
            var recurringId = provisional.RecurringTransactionId;

            // Candidats : réel, même compte/catégorie/type, dans le mois + grâce, montant suffisant,
            // pas déjà attribué à une autre récurrente ni consommé dans cette passe
            var candidates = await _context.Transactions
                .Where(t => t.Id != provisional.Id && !t.IsProvisional
                         && t.AccountId == provisional.AccountId
                         && t.CategoryId == provisional.CategoryId && t.Type == provisional.Type
                         && t.Date >= monthStart && t.Date < matchWindowEnd
                         && t.Amount >= provisional.Amount * MatchThreshold
                         && (t.RecurringTransactionId == null || t.RecurringTransactionId == recurringId))
                .ToListAsync();

            var match = candidates
                .Where(t => !usedTransactionIds.Contains(t.Id))
                .OrderBy(t => Math.Abs(t.Amount - provisional.Amount))
                .FirstOrDefault();

            if (match != null)
            {
                usedTransactionIds.Add(match.Id);
                match.RecurringTransactionId ??= recurringId;
                _context.Transactions.Remove(provisional);
                _logger.LogInformation("Provision réconciliée et supprimée : {Description} ({Month:yyyy-MM}), réel {Amount}€ le {Date:dd/MM}.",
                    provisional.Description, monthStart, match.Amount, match.Date);
            }
            else if (now >= matchWindowEnd)
            {
                // Fenêtre de réconciliation dépassée sans versement : la provision est expirée.
                // La garder gonflerait définitivement les entrées d'un mois passé.
                _context.Transactions.Remove(provisional);
                _logger.LogWarning("Provision expirée supprimée (aucun versement réel) : {Description} {Amount}€ ({Month:yyyy-MM}).",
                    provisional.Description, provisional.Amount, monthStart);
            }
        }

        await _context.SaveChangesAsync();
    }
}
