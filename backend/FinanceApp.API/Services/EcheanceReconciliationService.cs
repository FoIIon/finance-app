using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Calendar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services;

/// <summary>
/// L'exécuteur du rapprochement automatique des échéances, lancé une fois par cycle de synchronisation
/// bancaire. Il prépare les candidats et applique <see cref="EcheanceMatcher.FindPayment"/> ; il ne décide rien.
///
/// Ce qu'il écrit, et rien d'autre : <c>Echeance.TransactionId</c>, <c>MatchedAt</c>, <c>UpdatedAt</c>. Sur la
/// table <c>Transactions</c> il est strictement en lecture : la communication structurée d'un libellé est
/// extraite en mémoire à chaque passe, jamais stockée. Il n'ajoute, ne supprime, ne recatégorise jamais une
/// transaction : le bilan ne le voit pas, et un test d'invariance le prouve ligne par ligne. Les journaux ne
/// portent que des comptes, jamais un IBAN, un libellé ni une communication.
/// </summary>
public class EcheanceReconciliationService
{
    private readonly AppDbContext _context;
    private readonly HouseholdOptions _household;
    private readonly TimeProvider _clock;
    private readonly ILogger<EcheanceReconciliationService> _logger;

    public EcheanceReconciliationService(AppDbContext context, IOptions<HouseholdOptions> household, TimeProvider clock, ILogger<EcheanceReconciliationService> logger)
    {
        _context = context;
        _household = household.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Une passe complète. Rend le nombre d'échéances rapprochées.</summary>
    public async Task<int> ReconcileAsync(CancellationToken ct)
    {
        var matched = await MatchOpenEcheancesAsync(ct);
        _logger.LogInformation("Rapprochement des échéances : {Matched} rapprochée(s).", matched);
        return matched;
    }

    private async Task<int> MatchOpenEcheancesAsync(CancellationToken ct)
    {
        // Au-delà de la fenêtre forte après la date limite, aucun virement ne peut plus prouver l'échéance :
        // elle reste manuelle et n'élargit pas la fenêtre des candidats. Avant la fenêtre forte, rien ne peut
        // encore la prouver : une échéance saisie pour l'an prochain attend, elle n'élargit pas la requête d'un
        // an. Le jour du ménage, pas l'UTC : le Pi tourne en UTC et, entre 22 h et minuit, la borne glisserait
        // d'un jour.
        var today = _household.TodayLocal(_clock.GetUtcNow().UtcDateTime);
        var oldestDueStillMatchable = today.AddDays(-EcheanceMatcher.StrongDaysAfter);
        var newestDueAlreadyProvable = today.AddDays(EcheanceMatcher.StrongDaysBefore);

        var open = await _context.Echeances
            .Where(e => e.PaidAt == null && e.TransactionId == null && e.AutoMatchRefusedAt == null)
            .Where(e => e.CounterpartyIban != null || e.StructuredCommunication != null)
            .Where(e => e.DueDate >= oldestDueStillMatchable && e.DueDate <= newestDueAlreadyProvable)
            .OrderBy(e => e.DueDate).ThenBy(e => e.Id)
            .ToListAsync(ct);
        if (open.Count == 0) return 0;

        var alreadyClaimed = new HashSet<int>();
        var matched = new List<Echeance>();

        foreach (var group in open.GroupBy(e => e.DashboardId).OrderBy(g => g.Key))
        {
            var candidates = await LoadCandidatesAsync(group.Key, group, today, ct);
            foreach (var e in group)
            {
                var payment = EcheanceMatcher.FindPayment(
                    e.DueDate, e.Amount, e.CounterpartyIban, e.StructuredCommunication, candidates, alreadyClaimed);
                if (payment == null) continue;

                var now = _clock.GetUtcNow().UtcDateTime;
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
    /// Les dépenses réelles des comptes logiques du dashboard, hors provisions, pas déjà la preuve d'une autre
    /// échéance, dans la fenêtre la plus large que les échéances du dashboard peuvent réclamer. Depuis que le
    /// PUT ne lie plus de transaction (v4), cette requête est le seul endroit qui définit le périmètre d'un
    /// candidat. Sans tri : le matcher trie lui-même. La date du candidat est le jour du ménage
    /// (HouseholdOptions), et sa communication structurée est extraite du libellé en mémoire, après la lecture
    /// SQL : rien n'est écrit sur la transaction, rien n'est suivi par le contexte. La fenêtre s'arrête au
    /// surlendemain d'aujourd'hui : aucune transaction non provisionnelle n'est datée dans le futur, il n'y a
    /// rien à charger au-delà.
    /// </summary>
    private async Task<List<PaymentCandidate>> LoadCandidatesAsync(int dashboardId, IEnumerable<Echeance> echeances, DateOnly today, CancellationToken ct)
    {
        var dues = echeances.Select(e => e.DueDate).ToList();
        // Un jour de marge de chaque côté : la fenêtre SQL compare des instants UTC, le matcher des jours
        // locaux, et un virement du premier jour de la fenêtre locale peut être daté de la veille en UTC.
        var from = dues.Min().AddDays(-EcheanceMatcher.StrongDaysBefore - 1).ToDateTime(TimeOnly.MinValue);
        var latestUseful = dues.Max().AddDays(EcheanceMatcher.StrongDaysAfter + 2);
        var dayAfterTomorrow = today.AddDays(2);
        var toExclusive = (latestUseful < dayAfterTomorrow ? latestUseful : dayAfterTomorrow).ToDateTime(TimeOnly.MinValue);

        var rows = await _context.Transactions
            .Where(t => t.Type == TransactionType.Expense && !t.IsProvisional)
            .Where(t => t.Account.DashboardAccounts.Any(da => da.DashboardId == dashboardId))
            .Where(t => t.Date >= from && t.Date < toExclusive)
            .Where(t => !_context.Echeances.Any(e => e.TransactionId == t.Id))
            .Select(t => new { t.Id, t.Amount, t.Date, t.CounterpartyIban, t.Description })
            .ToListAsync(ct);

        // Relue de SQLite en Kind Unspecified, la date d'une transaction est UTC : TodayLocal la ramène au
        // jour du ménage.
        return rows
            .Select(t => new PaymentCandidate(t.Id, t.Amount, _household.TodayLocal(t.Date), t.CounterpartyIban,
                StructuredCommunication.Extract(t.Description)))
            .ToList();
    }

    /// <summary>
    /// Une sauvegarde pour toute la passe, une seule tentative. Si l'écriture échoue, toute la passe est
    /// abandonnée : les échéances reprennent leurs valeurs d'origine, on journalise le nombre, on rend 0. La
    /// passe suivante relira les candidats et réessaiera. Jamais d'exception vers la synchronisation.
    /// Le journal nomme la cause : une contrainte unique sur TransactionId (une autre écriture a pris une
    /// transaction entre la lecture des candidats et ici, attendu et bénin, avertissement) ou toute autre
    /// erreur d'écriture (clé étrangère, base verrouillée, disque : inattendu, erreur). L'exception est
    /// jointe au journal dans les deux cas ; ses messages ne portent que des noms de colonnes.
    /// </summary>
    private async Task<int> SaveLinksAsync(List<Echeance> matched, CancellationToken ct)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
            return matched.Count;
        }
        catch (DbUpdateException ex)
        {
            if (IsUniqueConstraintViolation(ex))
                _logger.LogWarning(ex, "Rapprochement des échéances : contrainte unique sur TransactionId, {Count} lien(s) abandonné(s), la passe suivante réessaiera.", matched.Count);
            else
                _logger.LogError(ex, "Rapprochement des échéances : erreur d'écriture, {Count} lien(s) abandonné(s), la passe suivante réessaiera.", matched.Count);

            foreach (var e in matched)
            {
                var entry = _context.Entry(e);
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }
            return 0;
        }
    }

    /// <summary>
    /// SQLITE_CONSTRAINT (19) avec le code étendu SQLITE_CONSTRAINT_UNIQUE (2067). Le code primaire seul ne
    /// suffit pas : une clé étrangère cassée rend aussi 19, avec le code étendu 787, et ce n'est pas la même cause.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException { SqliteErrorCode: 19, SqliteExtendedErrorCode: 2067 };
}
