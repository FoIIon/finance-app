using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

/// <summary>
/// L'exécuteur du rapprochement automatique des échéances, lancé après chaque synchronisation bancaire.
/// Il prépare les candidats et applique <see cref="EcheanceMatcher.FindPayment"/> ; il ne décide rien.
///
/// Ce qu'il écrit, et rien d'autre : <c>Echeance.TransactionId</c>, <c>MatchedAt</c>, <c>UpdatedAt</c>,
/// et <c>Transaction.StructuredCommunication</c> en rattrapage des lignes importées avant que la colonne
/// n'existe. Il n'ajoute, ne supprime, ne recatégorise jamais une transaction : le bilan ne le voit pas,
/// et un test d'invariance le prouve. Les journaux ne portent que des comptes, jamais un IBAN, un libellé
/// ni une communication.
/// </summary>
public class EcheanceReconciliationService
{
    /// <summary>
    /// Borne du rattrapage par passe. Les libellés qui portent un séparateur sans communication valide
    /// (contrôle 97 faux, groupe tronqué) repassent dans le filtre à chaque passe : borner le lot garde
    /// la passe courte, le journal en donne le compte pour qu'on sache s'il en reste.
    /// </summary>
    public const int BackfillBatchSize = 500;

    private readonly AppDbContext _context;
    private readonly ILogger<EcheanceReconciliationService> _logger;

    public EcheanceReconciliationService(AppDbContext context, ILogger<EcheanceReconciliationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>Une passe complète : rattrapage des communications, puis rapprochement. Rend le nombre d'échéances rapprochées.</summary>
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        var (backfilled, unresolved) = await BackfillStructuredCommunicationsAsync(ct);
        var matched = await MatchOpenEcheancesAsync(ct);

        _logger.LogInformation(
            "Rapprochement des échéances : {Matched} rapprochée(s), {Backfilled} communication(s) structurée(s) rattrapée(s), {Unresolved} libellé(s) à séparateur sans communication valide.",
            matched, backfilled, unresolved);
        return matched;
    }

    /// <summary>
    /// Les transactions historiques dont le libellé porte un séparateur et dont la colonne est encore vide.
    /// Filtre en base, on ne charge pas tout l'historique. Seule écriture sur Transactions de tout le lot.
    /// Sauvegardée avant le rapprochement pour que les candidats relus en base portent leur clé.
    /// </summary>
    private async Task<(int Backfilled, int Unresolved)> BackfillStructuredCommunicationsAsync(CancellationToken ct)
    {
        var rows = await _context.Transactions
            .Where(t => t.StructuredCommunication == null
                     && (t.Description.Contains("+++") || t.Description.Contains("***")))
            .OrderBy(t => t.Id)
            .Take(BackfillBatchSize)
            .ToListAsync(ct);

        var backfilled = 0;
        foreach (var t in rows)
        {
            var digits = StructuredCommunication.Extract(t.Description);
            if (digits == null) continue;
            t.StructuredCommunication = digits;
            backfilled++;
        }

        if (backfilled > 0) await _context.SaveChangesAsync(ct);
        return (backfilled, rows.Count - backfilled);
    }

    private async Task<int> MatchOpenEcheancesAsync(CancellationToken ct)
    {
        var open = await _context.Echeances
            .Where(e => e.PaidAt == null && e.TransactionId == null)
            .Where(e => e.CounterpartyIban != null || e.StructuredCommunication != null)
            .OrderBy(e => e.DueDate).ThenBy(e => e.Id)
            .ToListAsync(ct);
        if (open.Count == 0) return 0;

        var alreadyClaimed = new HashSet<int>();
        var matched = new List<Echeance>();

        foreach (var group in open.GroupBy(e => e.DashboardId).OrderBy(g => g.Key))
        {
            var candidates = await LoadCandidatesAsync(group.Key, group, ct);
            foreach (var e in group)
            {
                var payment = EcheanceMatcher.FindPayment(
                    e.DueDate, e.Amount, e.CounterpartyIban, e.StructuredCommunication,
                    e.RejectedTransactionId, candidates, alreadyClaimed);
                if (payment == null) continue;

                var now = DateTime.UtcNow;
                e.TransactionId = payment.Id;
                e.MatchedAt = now;
                e.UpdatedAt = now;
                alreadyClaimed.Add(payment.Id);
                matched.Add(e);
            }
        }

        if (matched.Count == 0) return 0;
        return await SaveLinksAsync(matched, ct);
    }

    /// <summary>
    /// Les dépenses réelles des comptes logiques du dashboard (même périmètre qu'EcheanceController.Update),
    /// hors provisions, pas déjà la preuve d'une autre échéance, dans la fenêtre la plus large que les
    /// échéances du dashboard peuvent réclamer.
    /// </summary>
    private async Task<List<PaymentCandidate>> LoadCandidatesAsync(int dashboardId, IEnumerable<Echeance> echeances, CancellationToken ct)
    {
        var dues = echeances.Select(e => e.DueDate).ToList();
        var from = dues.Min().AddDays(-EcheanceMatcher.StrongDaysBefore).ToDateTime(TimeOnly.MinValue);
        var toExclusive = dues.Max().AddDays(EcheanceMatcher.StrongDaysAfter + 1).ToDateTime(TimeOnly.MinValue);

        return await _context.Transactions
            .Where(t => t.Type == TransactionType.Expense && !t.IsProvisional)
            .Where(t => t.Account.DashboardAccounts.Any(da => da.DashboardId == dashboardId))
            .Where(t => t.Date >= from && t.Date < toExclusive)
            .Where(t => !_context.Echeances.Any(e => e.TransactionId == t.Id))
            .OrderBy(t => t.Id)
            .Select(t => new PaymentCandidate(t.Id, t.Amount, t.Date, t.CounterpartyIban, t.StructuredCommunication))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Une sauvegarde pour toute la passe. Si l'index unique sur TransactionId tranche (une autre écriture a
    /// pris la transaction entre-temps), on retire le lien en conflit et on sauvegarde le reste, sans jamais
    /// planter la synchronisation.
    /// </summary>
    private async Task<int> SaveLinksAsync(List<Echeance> matched, CancellationToken ct)
    {
        for (var attempt = 0; attempt <= matched.Count; attempt++)
        {
            try
            {
                await _context.SaveChangesAsync(ct);
                return matched.Count;
            }
            catch (DbUpdateException ex)
            {
                var conflicting = ex.Entries.Select(en => en.Entity).OfType<Echeance>().Where(matched.Contains).ToList();
                // Impossible d'isoler la ligne fautive : on renonce à toute la passe, la suivante réessaiera.
                if (conflicting.Count == 0) conflicting = matched.ToList();

                _logger.LogWarning("Rapprochement des échéances : {Count} lien(s) en conflit sur l'index unique, retiré(s) de la passe.", conflicting.Count);
                foreach (var e in conflicting)
                {
                    var entry = _context.Entry(e);
                    entry.CurrentValues.SetValues(entry.OriginalValues);
                    entry.State = EntityState.Unchanged;
                    matched.Remove(e);
                }
                if (matched.Count == 0) return 0;
            }
        }
        return 0;
    }
}
