using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

/// <summary>
/// Provisions de revenus/dépenses récurrents : matérialise au 1er du mois une transaction
/// provisionnelle pour chaque récurrente marquée ProvisionAtMonthStart, puis la supprime
/// quand le versement réel est importé (réconciliation).
///
/// Cas d'usage : le salaire tombe le ~28, sans provision le mois entier paraît en négatif.
/// </summary>
public class ProvisionService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ProvisionService> _logger;

    /// <summary>Un versement réel « correspond » à la provision s'il fait au moins 50% du montant provisionné
    /// (même catégorie, même type, même mois). Évite qu'un petit remboursement catégorisé pareil réconcilie le salaire.</summary>
    private const decimal MatchThreshold = 0.5m;

    /// <summary>Nombre de versements réels passés utilisés pour estimer le montant de la provision.</summary>
    private const int AverageWindow = 3;

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
        await ReconcileProvisionsAsync();
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

            var type = recurring.Type == "Income" ? TransactionType.Income : TransactionType.Expense;

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

            // Montant estimé : moyenne des derniers versements réels de la même catégorie, fallback montant de la récurrente
            var lastRealAmounts = await _context.Transactions
                .Where(t => t.AccountId == accountId.Value && !t.IsProvisional
                         && t.CategoryId == recurring.CategoryId.Value && t.Type == type)
                .OrderByDescending(t => t.Date)
                .Take(AverageWindow)
                .Select(t => t.Amount)
                .ToListAsync();
            var amount = lastRealAmounts.Any() ? Math.Round(lastRealAmounts.Average(), 2) : recurring.Amount;

            // Le réel est-il déjà arrivé ce mois-ci ? (mois entamé après le versement — pas de provision)
            var realAlreadyLanded = await _context.Transactions.AnyAsync(t =>
                t.AccountId == accountId.Value && !t.IsProvisional
                && t.CategoryId == recurring.CategoryId.Value && t.Type == type
                && t.Date >= monthStart && t.Date < monthEnd
                && t.Amount >= amount * MatchThreshold);
            if (realAlreadyLanded) continue;

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
                RecurringTransactionId = recurring.Id,
            });

            _logger.LogInformation("Provision créée : {Description} {Amount}€ ({Month:yyyy-MM}).",
                recurring.Description, amount, monthStart);
        }

        await _context.SaveChangesAsync();
    }

    private async Task ReconcileProvisionsAsync()
    {
        // Toutes les provisions encore ouvertes (mois courant comme passés — un salaire peut tomber après un arrêt du serveur)
        var provisionals = await _context.Transactions
            .Where(t => t.IsProvisional)
            .ToListAsync();

        foreach (var provisional in provisionals)
        {
            var monthStart = new DateTime(provisional.Date.Year, provisional.Date.Month, 1);
            var monthEnd = monthStart.AddMonths(1);

            var realMatch = await _context.Transactions.AnyAsync(t =>
                t.Id != provisional.Id && !t.IsProvisional
                && t.AccountId == provisional.AccountId
                && t.CategoryId == provisional.CategoryId && t.Type == provisional.Type
                && t.Date >= monthStart && t.Date < monthEnd
                && t.Amount >= provisional.Amount * MatchThreshold);

            if (realMatch)
            {
                _context.Transactions.Remove(provisional);
                _logger.LogInformation("Provision réconciliée et supprimée : {Description} ({Month:yyyy-MM}).",
                    provisional.Description, monthStart);
            }
        }

        await _context.SaveChangesAsync();
    }
}
