using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Calendar;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services;

/// <summary>
/// L'exécuteur du rapprochement automatique des échéances, lancé une fois par cycle de synchronisation
/// bancaire. Il prépare les candidats et applique <see cref="EcheanceMatcher.FindPayment"/> ; il ne décide rien.
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
    /// Lot du rattrapage par passe. Chaque ligne prise sort du filtre (clé ou sentinelle), le rattrapage
    /// converge : trois passes pour l'historique de prod, puis la requête est vide et immédiate.
    /// </summary>
    public const int BackfillBatchSize = 1000;

    private readonly AppDbContext _context;
    private readonly HouseholdOptions _household;
    private readonly ILogger<EcheanceReconciliationService> _logger;

    public EcheanceReconciliationService(AppDbContext context, IOptions<HouseholdOptions> household, ILogger<EcheanceReconciliationService> logger)
    {
        _context = context;
        _household = household.Value;
        _logger = logger;
    }

    /// <summary>Une passe complète : rattrapage des communications, puis rapprochement. Rend le nombre d'échéances rapprochées.</summary>
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        var (backfilled, examined) = await BackfillStructuredCommunicationsAsync(ct);
        var matched = await MatchOpenEcheancesAsync(ct);

        _logger.LogInformation(
            "Rapprochement des échéances : {Matched} rapprochée(s), {Backfilled} communication(s) structurée(s) rattrapée(s), {Examined} libellé(s) examiné(s) sans communication valide.",
            matched, backfilled, examined);
        return matched;
    }

    /// <summary>
    /// Les transactions jamais examinées (colonne null), par lots triés par Id. Chaque ligne reçoit sa clé ou la
    /// sentinelle, et ne revient plus. Seule écriture sur Transactions de tout le lot. Sauvegardée avant le
    /// rapprochement pour que les candidats relus en base portent leur clé.
    /// </summary>
    private async Task<(int Backfilled, int Examined)> BackfillStructuredCommunicationsAsync(CancellationToken ct)
    {
        var rows = await _context.Transactions
            .Where(t => t.StructuredCommunication == null)
            .OrderBy(t => t.Id)
            .Take(BackfillBatchSize)
            .ToListAsync(ct);

        var backfilled = 0;
        foreach (var t in rows)
        {
            var digits = StructuredCommunication.Extract(t.Description);
            t.StructuredCommunication = digits ?? StructuredCommunication.Examined;
            if (digits != null) backfilled++;
        }

        if (rows.Count > 0) await _context.SaveChangesAsync(ct);
        return (backfilled, rows.Count - backfilled);
    }

    private async Task<int> MatchOpenEcheancesAsync(CancellationToken ct)
    {
        // Au-delà de la fenêtre forte après la date limite, aucun virement ne peut plus prouver l'échéance :
        // elle reste manuelle et n'élargit pas la fenêtre des candidats. Le jour du ménage, pas l'UTC : le Pi
        // tourne en UTC et, entre 22 h et minuit, la borne glisserait d'un jour.
        var oldestDueStillMatchable = _household.TodayLocal(DateTime.UtcNow).AddDays(-EcheanceMatcher.StrongDaysAfter);

        var open = await _context.Echeances
            .Where(e => e.PaidAt == null && e.TransactionId == null && e.AutoMatchRefusedAt == null)
            .Where(e => e.CounterpartyIban != null || e.StructuredCommunication != null)
            .Where(e => e.DueDate >= oldestDueStillMatchable)
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
                    e.DueDate, e.Amount, e.CounterpartyIban, e.StructuredCommunication, candidates, alreadyClaimed);
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
    /// échéances du dashboard peuvent réclamer. Sans tri : le matcher trie lui-même. La date du candidat
    /// est le jour du ménage (HouseholdOptions), le matcher compare des jours locaux à des dates limites.
    /// </summary>
    private async Task<List<PaymentCandidate>> LoadCandidatesAsync(int dashboardId, IEnumerable<Echeance> echeances, CancellationToken ct)
    {
        var dues = echeances.Select(e => e.DueDate).ToList();
        // Un jour de marge de chaque côté : la fenêtre SQL compare des instants UTC, le matcher des jours
        // locaux, et un virement du premier jour de la fenêtre locale peut être daté de la veille en UTC.
        var from = dues.Min().AddDays(-EcheanceMatcher.StrongDaysBefore - 1).ToDateTime(TimeOnly.MinValue);
        var toExclusive = dues.Max().AddDays(EcheanceMatcher.StrongDaysAfter + 2).ToDateTime(TimeOnly.MinValue);

        var rows = await _context.Transactions
            .Where(t => t.Type == TransactionType.Expense && !t.IsProvisional)
            .Where(t => t.Account.DashboardAccounts.Any(da => da.DashboardId == dashboardId))
            .Where(t => t.Date >= from && t.Date < toExclusive)
            .Where(t => !_context.Echeances.Any(e => e.TransactionId == t.Id))
            .Select(t => new { t.Id, t.Amount, t.Date, t.CounterpartyIban, t.StructuredCommunication })
            .ToListAsync(ct);

        // Relue de SQLite en Kind Unspecified, la date d'une transaction est UTC : TodayLocal la ramène au
        // jour du ménage. La sentinelle vide n'est pas une clé : elle sort en null.
        return rows
            .Select(t => new PaymentCandidate(t.Id, t.Amount, _household.TodayLocal(t.Date), t.CounterpartyIban,
                t.StructuredCommunication == StructuredCommunication.Examined ? null : t.StructuredCommunication))
            .ToList();
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
