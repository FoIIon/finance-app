using System.Text.Json;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

public class BankSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BankSyncService> _logger;

    public BankSyncService(IServiceScopeFactory scopeFactory, ILogger<BankSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncAllConnectionsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la synchronisation automatique des comptes bancaires.");
            }

            await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
        }
    }

    private async Task SyncAllConnectionsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var provisions = scope.ServiceProvider.GetRequiredService<ProvisionService>();

        // Provisions AVANT la sync : elles doivent exister même si la banque est injoignable
        try
        {
            await provisions.RunAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la création des provisions.");
        }

        var connectionIds = await context.BankConnections
            .Where(bc => bc.Status == BankConnectionStatus.Linked)
            .Select(bc => bc.Id)
            .ToListAsync();

        foreach (var connectionId in connectionIds)
        {
            try
            {
                await SyncConnectionInternalAsync(connectionId, scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la synchronisation de la connexion {ConnectionId}.", connectionId);
            }
        }

        // Réconciliation APRÈS la sync : le versement réel vient peut-être d'être importé
        try
        {
            await provisions.RunAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la réconciliation des provisions.");
        }
    }

    /// <summary>
    /// Synchronisation manuelle depuis le controller — laisse remonter les exceptions.
    /// <paramref name="daysBack"/> force la profondeur de la fenêtre demandée à la banque, pour un
    /// rattrapage ponctuel d'un champ ajouté après coup (l'IBAN du bénéficiaire, le 31/08/2026). Hors
    /// rattrapage, la fenêtre reste calculée par la sync : le quota GoCardless est de quatre appels par
    /// jour et par compte, chaque appel gaspillé manque à la sync suivante.
    /// </summary>
    public async Task SyncConnectionAsync(int connectionId, int? daysBack = null)
    {
        using var scope = _scopeFactory.CreateScope();
        await SyncConnectionInternalAsync(connectionId, scope.ServiceProvider, rethrow: true, daysBack: daysBack);

        // Une sync manuelle peut avoir importé le versement réel — réconcilier dans la foulée
        try
        {
            await scope.ServiceProvider.GetRequiredService<ProvisionService>().RunAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la réconciliation des provisions après sync manuelle.");
        }
    }

    private async Task SyncConnectionInternalAsync(int connectionId, IServiceProvider serviceProvider, bool rethrow = false, int? daysBack = null)
    {
        var context = serviceProvider.GetRequiredService<AppDbContext>();

        var connection = await context.BankConnections
            .Include(bc => bc.BankAccounts)
            .FirstOrDefaultAsync(bc => bc.Id == connectionId);

        if (connection == null) return;

        if (connection.Provider == "Manual")
        {
            // Comptes manuels : pas de sync externe, le solde est calculé dynamiquement
            return;
        }
        else if (connection.Provider == "TradeRepublic")
        {
            await SyncTradeRepublicAsync(connection, context, serviceProvider, rethrow);
        }
        else
        {
            await SyncGoCardlessAsync(connection, context, serviceProvider, rethrow, daysBack);
        }
    }

    private async Task SyncGoCardlessAsync(BankConnection connection, AppDbContext context, IServiceProvider serviceProvider, bool rethrow, int? daysBack = null)
    {
        var goCardless = serviceProvider.GetRequiredService<GoCardlessClient>();

        // Vérifier que la réquisition est toujours valide
        try
        {
            var requisition = await goCardless.GetRequisitionAsync(connection.RequisitionId);
            var status = requisition.GetProperty("status").GetString();
            if (status == "EX")
            {
                connection.Status = BankConnectionStatus.Expired;
                await context.SaveChangesAsync();
                _logger.LogWarning("Réquisition expirée pour la connexion {ConnectionId}.", connection.Id);
                return;
            }
        }
        catch (HttpRequestException)
        {
            connection.Status = BankConnectionStatus.Error;
            await context.SaveChangesAsync();
            return;
        }

        // Charger les règles de catégorisation de l'utilisateur
        // Du mot-clé le plus long au plus court : c'est la règle la plus spécifique qui doit gagner.
        // Sans cet ordre, le premier créé l'emportait, et « Vacance » raflait « Legumes vacances »
        // au nez d'une règle alimentaire pourtant plus précise (règle de nature du 27/08/2026).
        var rules = await context.CategoryRules
            .Where(cr => cr.UserId == connection.UserId)
            .OrderByDescending(cr => cr.Keyword.Length)
            .ThenBy(cr => cr.Id)
            .ToListAsync();

        // Catégorie par défaut : "Autres"
        var defaultCategory = await context.Categories
            .FirstOrDefaultAsync(c => c.Name == "Autres" && c.IsDefault);
        var defaultCategoryId = defaultCategory?.Id ?? 10;

        // Compte par défaut de l'utilisateur pour les transactions importées
        var defaultAccount = await context.Accounts
            .Where(a => a.UserId == connection.UserId && !a.IsPersonalScope)
            .OrderBy(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        if (defaultAccount == null)
        {
            _logger.LogWarning("Aucun compte trouvé pour l'utilisateur {UserId}.", connection.UserId);
            return;
        }

        // Routage perso/commun : une transaction d'un compte bancaire perso part sur le compte logique
        // Perso au lieu du commun (PersoScopeRouter). Ce compte n'est résolu, et créé au besoin, qu'à la
        // première ligne perso : un utilisateur sans compte ni règle perso n'en hérite pas d'un vide.
        int? persoAccountId = null;
        async Task<int> PersoAccountIdAsync() =>
            persoAccountId ??= await PersoAccounts.GetOrCreatePersoAccountIdAsync(context, connection.UserId);

        // Remonter à 90 jours s'il reste des transactions sans counterparty, sur tous les comptes
        // logiques de l'utilisateur : une ligne routée Perso doit déclencher la relecture comme les autres.
        var hasMissingCounterparty = await context.Transactions
            .AnyAsync(t => t.Account.UserId == connection.UserId && t.IsImported && t.CounterpartyName == null);
        // Rattrapage automatique de l'IBAN du bénéficiaire (ajouté le 31/08/2026) : tant qu'aucune ligne
        // n'en porte, la fenêtre s'ouvre à 90 jours pour relire tout ce que le consentement autorise, et
        // se refermera d'elle-même dès le premier IBAN servi. Le service synchronise au démarrage, donc
        // le rattrapage part tout seul au déploiement, sans appel authentifié à déclencher à la main.
        var hasNoIbanYet = !await context.Transactions
            .AnyAsync(t => t.Account.UserId == connection.UserId && t.CounterpartyIban != null);
        // Fenêtre glissante de 14 jours minimum : GoCardless filtre par bookingDate, une transaction
        // bookée tardivement avec une date antérieure à LastSyncAt tomberait hors fenêtre pour toujours.
        // L'index unique sur ExternalId absorbe les doublons re-fetchés.
        var slidingWindowStart = DateTime.UtcNow.AddDays(-14);
        var lastSync = connection.LastSyncAt ?? DateTime.UtcNow.AddDays(-90);
        var dateFrom = daysBack.HasValue
            ? DateTime.UtcNow.AddDays(-Math.Clamp(daysBack.Value, 1, 730))
            : hasMissingCounterparty || hasNoIbanYet
                ? DateTime.UtcNow.AddDays(-90)
                : (lastSync < slidingWindowStart ? lastSync : slidingWindowStart);

        var anySyncFailed = false;
        foreach (var account in connection.BankAccounts.Where(a => a.IsActive))
        {
            try
            {
                // Récupérer le solde réel (balances GoCardless)
                try
                {
                    var balancesData = await goCardless.GetBalancesAsync(account.ExternalAccountId);
                    if (balancesData.TryGetProperty("balances", out var balancesArray))
                    {
                        // Préférer interimAvailable (= solde dispo, peut inclure du pending), sinon expected, sinon premier
                        var realBalance = ExtractPreferredBalance(balancesArray, new[] { "interimAvailable", "expected", "interimBooked", "closingBooked" });
                        if (realBalance.HasValue)
                        {
                            account.RealBalance = realBalance.Value;
                            account.BalanceUpdatedAt = DateTime.UtcNow;
                        }

                        // Solde booké uniquement (exclut le pending) — ancrage stable pour les courbes rétrospectives
                        account.BookedBalance = ExtractPreferredBalance(balancesArray, new[] { "interimBooked", "closingBooked", "expected" });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Erreur récupération solde réel pour {AccountId}.", account.ExternalAccountId);
                }

                var transactionsData = await goCardless.GetTransactionsAsync(account.ExternalAccountId, dateFrom);

                if (!transactionsData.TryGetProperty("transactions", out var transactions))
                    continue;

                if (!transactions.TryGetProperty("booked", out var booked))
                    continue;

                foreach (var tx in booked.EnumerateArray())
                {
                    var externalId = tx.TryGetProperty("transactionId", out var txId)
                        ? txId.GetString()
                        : tx.TryGetProperty("internalTransactionId", out var intId)
                            ? intId.GetString()
                            : null;

                    if (string.IsNullOrEmpty(externalId)) continue;

                    // Vérifier si la transaction existe déjà
                    var existing = await context.Transactions.FirstOrDefaultAsync(t => t.ExternalId == externalId);
                    if (existing != null)
                    {
                        // Mettre à jour le counterparty si manquant
                        if (existing.CounterpartyName == null)
                        {
                            var cp = tx.TryGetProperty("creditorName", out var c)
                                ? c.GetString()
                                : tx.TryGetProperty("debtorName", out var d)
                                    ? d.GetString()
                                    : null;
                            if (cp != null)
                                existing.CounterpartyName = cp;
                        }

                        // Idem pour l'IBAN du bénéficiaire, ajouté le 31/08/2026 : les lignes importées
                        // avant ne l'ont pas, et la banque le ressert tant que la transaction tient dans
                        // la fenêtre demandée. Rien à récupérer sur une carte, elle n'en a jamais eu.
                        if (existing.CounterpartyIban == null)
                        {
                            var backfilledIban = GoCardlessTransactionFields.CounterpartyIban(tx);
                            if (backfilledIban != null)
                                existing.CounterpartyIban = backfilledIban;
                        }
                        continue;
                    }

                    // Extraire les données de la transaction
                    var amount = tx.GetProperty("transactionAmount").GetProperty("amount").GetString();
                    var parsedAmount = decimal.Parse(amount!, System.Globalization.CultureInfo.InvariantCulture);

                    var description = tx.TryGetProperty("remittanceInformationUnstructured", out var desc)
                        ? desc.GetString() ?? ""
                        : tx.TryGetProperty("remittanceInformationUnstructuredArray", out var descArr)
                            ? string.Join(" ", descArr.EnumerateArray().Select(d => d.GetString()))
                            : "";

                    var bookingDate = tx.TryGetProperty("bookingDate", out var date)
                        ? DateTime.Parse(date.GetString()!)
                        : DateTime.UtcNow;

                    var counterparty = tx.TryGetProperty("creditorName", out var cred)
                        ? cred.GetString()
                        : tx.TryGetProperty("debtorName", out var deb)
                            ? deb.GetString()
                            : null;

                    var counterpartyIban = GoCardlessTransactionFields.CounterpartyIban(tx);

                    // Appliquer les règles : la première qui matche (mot-clé le plus long d'abord) fixe la
                    // catégorie, le flag fixe et, pour une ligne TR, le périmètre perso/commun.
                    var matchedRule = CategoryRuleMatcher.FirstMatch(rules, description, counterparty, counterpartyIban);
                    var categoryId = matchedRule?.CategoryId ?? defaultCategoryId;
                    var isFixed = matchedRule?.MarkAsFixed ?? false;

                    var type = parsedAmount >= 0 ? TransactionType.Income : TransactionType.Expense;
                    var scope = PersoScopeRouter.Decide(account.IsPersonal, externalId, type, matchedRule);

                    var transaction = new Transaction
                    {
                        Amount = Math.Abs(parsedAmount),
                        Description = description,
                        Date = bookingDate,
                        Type = type,
                        CategoryId = categoryId,
                        AccountId = scope == TransactionScope.Perso ? await PersoAccountIdAsync() : defaultAccount.Id,
                        ExternalId = externalId,
                        IsImported = true,
                        CounterpartyName = counterparty,
                        CounterpartyIban = counterpartyIban,
                        IsFixed = isFixed,
                        BankAccountId = account.Id
                    };

                    context.Transactions.Add(transaction);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la synchronisation du compte {AccountId}.", account.ExternalAccountId);
                anySyncFailed = true;
                if (rethrow) throw;
            }
        }

        // N'avancer LastSyncAt que si tous les comptes ont synchronisé : une fenêtre manquée
        // au-delà des 14 jours glissants ne serait jamais re-fetchée sinon.
        if (!anySyncFailed)
            connection.LastSyncAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Extrait le solde GoCardless correspondant au premier balanceType trouvé dans l'ordre de préférence donné
    /// (fallback sur le premier solde disponible si aucun des types préférés n'est présent).
    /// </summary>
    private static decimal? ExtractPreferredBalance(JsonElement balancesArray, string[] preferenceOrder)
    {
        JsonElement bal = default;
        bool found = false;
        foreach (var prefType in preferenceOrder)
        {
            foreach (var b in balancesArray.EnumerateArray())
            {
                if (b.TryGetProperty("balanceType", out var t) && t.GetString() == prefType)
                {
                    bal = b;
                    found = true;
                    break;
                }
            }
            if (found) break;
        }
        if (!found)
        {
            foreach (var b in balancesArray.EnumerateArray()) { bal = b; found = true; break; }
        }
        if (found && bal.TryGetProperty("balanceAmount", out var amountObj)
            && amountObj.TryGetProperty("amount", out var amountVal))
        {
            var amountStr = amountVal.GetString();
            if (decimal.TryParse(amountStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }
        return null;
    }

    private async Task SyncTradeRepublicAsync(BankConnection connection, AppDbContext context, IServiceProvider serviceProvider, bool rethrow)
    {
        if (string.IsNullOrEmpty(connection.EncryptedRefreshToken))
        {
            _logger.LogWarning("Pas de refresh token pour la connexion Trade Republic {ConnectionId}.", connection.Id);
            return;
        }

        using var trClient = serviceProvider.GetRequiredService<TradeRepublicClient>();

        try
        {
            // Renouvellement systématique : entre deux synchronisations espacées de six
            // heures, le jeton stocké est toujours périmé.
            var sessionToken = await TradeRepublicSession.RefreshAndStoreAsync(connection, trClient, context, _logger);

            // Le REST /api/v2/timeline/transactions ne rend que sa première page et ignore tout
            // paramètre de pagination (constaté le 28/08/2026), soit une quinzaine de jours. C'est ce
            // qui faisait manquer l'abonnement Shadow payé à la carte TR et les achats de titres
            // mensuels : ils étaient hors fenêtre, pas mal classés. La pagination WebSocket
            // (timelineTransactions + curseur « after ») remonte, elle, jusqu'au premier mouvement du
            // compte.
            //
            // Elle coûte cher (jusqu'à 400 souscriptions), donc on ne la paie qu'une fois : tant que la
            // base n'a aucune ligne TR de plus de 45 jours, on descend en profondeur, ensuite la
            // première page suffit à rattraper le courant. Même idiome que le rattrapage des IBAN.
            var trHistoriqueDejaLa = await context.Transactions
                .AnyAsync(t => t.ExternalId != null
                            && t.ExternalId.StartsWith("tr-")
                            && t.Date < DateTime.UtcNow.AddDays(-45));

            List<TrCardTransaction> cardTransactions;
            if (trHistoriqueDejaLa)
            {
                // La déduplication par ExternalId évite les doublons — pas besoin de filtrer par date
                cardTransactions = await trClient.GetCardTransactionsHttpAsync(sessionToken);
            }
            else
            {
                var refreshToken = trClient.DecryptToken(connection.EncryptedRefreshToken!);
                var deviceToken = string.IsNullOrEmpty(connection.EncryptedDeviceToken)
                    ? "" : trClient.DecryptToken(connection.EncryptedDeviceToken);
                var timeline = await trClient.GetTimelineAllAsync(sessionToken, refreshToken, deviceToken);
                cardTransactions = timeline
                    .Select(i => new TrCardTransaction
                    {
                        Id = i.Id,
                        Amount = i.Amount,
                        Title = i.Title,
                        Date = i.Date,
                        EventType = i.EventType,
                    })
                    .ToList();
                _logger.LogInformation(
                    "TR : premier passage profond, {lignes} lignes de timeline, plus ancienne {date:yyyy-MM-dd}.",
                    cardTransactions.Count,
                    cardTransactions.Count > 0 ? cardTransactions.Min(t => t.Date) : DateTime.MinValue);
            }
            if (cardTransactions.Count == 0)
            {
                connection.LastSyncAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
                return;
            }

            // Charger les règles de catégorisation
            // Même ordre que côté GoCardless : le mot-clé le plus long gagne.
            var rules = await context.CategoryRules
                .Where(cr => cr.UserId == connection.UserId)
                .OrderByDescending(cr => cr.Keyword.Length)
                .ThenBy(cr => cr.Id)
                .ToListAsync();

            var defaultCategory = await context.Categories
                .FirstOrDefaultAsync(c => c.Name == SystemCategories.Autres && c.IsDefault);
            var defaultCategoryId = defaultCategory?.Id ?? 10;

            // Les deux catégories où ranger ce qui n'est pas de la consommation du ménage.
            var virementInterneId = await SystemCategories.VirementInterneIdAsync(context);
            var investissementId = await SystemCategories.InvestissementIdAsync(context);

            var defaultAccount = await context.Accounts
                .Where(a => a.UserId == connection.UserId)
                .OrderBy(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            if (defaultAccount == null)
            {
                _logger.LogWarning("Aucun compte trouvé pour l'utilisateur {UserId}.", connection.UserId);
                return;
            }

            // Routage perso/commun. La carte TR sert au commun (remboursé) comme au perso (abos de
            // Sébastien) : une dépense TR est commune par défaut, seules celles qui matchent une règle
            // perso partent côté Perso. Voir PersoScopeRouter. Le compte logique Perso n'est résolu,
            // et créé au besoin, qu'à la première ligne perso.
            int? persoAccountId = null;
            async Task<int> PersoAccountIdAsync() =>
                persoAccountId ??= await PersoAccounts.GetOrCreatePersoAccountIdAsync(context, connection.UserId);
            // Le compte espèces TR est exclu : il ne porte qu'un solde. L'y accrocher ferait basculer
            // toute la timeline côté Perso, puisqu'il est marqué perso, alors qu'une dépense carte TR
            // est commune par défaut (TradeRepublicCashAccount.IsCashAccount).
            var trBankAccount = connection.BankAccounts
                .FirstOrDefault(ba => ba.IsActive && !TradeRepublicCashAccount.IsCashAccount(ba));
            var trBankAccountIsPersonal = trBankAccount?.IsPersonal ?? false;

            // Noms des titulaires de tous les comptes suivis : c'est ce qui permet de reconnaître
            // qu'un libellé TR désigne un mouvement interne et non un commerçant.
            var ownerNames = (await context.BankAccounts
                    .Where(ba => ba.UserId == connection.UserId
                              || (ba.BankConnection != null && ba.BankConnection.UserId == connection.UserId))
                    .Select(ba => new { ba.OwnerName, ba.AccountName })
                    .ToListAsync())
                .SelectMany(ba => new[] { ba.OwnerName, ba.AccountName })
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            // Noms d'instruments du portefeuille, pour reconnaître un achat de titres au libellé
            // quand TR ne fournit pas d'eventType.
            var dashboardIds = await context.DashboardMembers
                .Where(m => m.UserId == connection.UserId)
                .Select(m => m.DashboardId)
                .ToListAsync();
            var instrumentNames = TradeRepublicTimelineClassifier.UnambiguousInstrumentNames(
                await context.Investments
                    .Where(i => !i.IsArchived && dashboardIds.Contains(i.DashboardId))
                    .ToListAsync());

            // Candidats au rapprochement : les transactions rattachées à un vrai compte bancaire sur
            // la fenêtre couverte par la timeline TR, élargie de la tolérance de date. Tous les comptes
            // logiques de l'utilisateur : une alimentation de la carte depuis l'Argenta perso a sa
            // jambe bancaire côté Perso, elle doit être rapprochée comme les autres.
            var windowStart = cardTransactions.Min(t => t.Date).Date.AddDays(-InternalTransferReconciler.MaxDayGap);
            var windowEnd = cardTransactions.Max(t => t.Date).Date.AddDays(InternalTransferReconciler.MaxDayGap + 1);
            var bankLegEntities = await context.Transactions
                .Where(t => t.Account.UserId == connection.UserId
                         && t.BankAccountId != null
                         && t.Date >= windowStart && t.Date < windowEnd)
                .ToListAsync();
            var bankLegs = bankLegEntities
                .Select(t => new TransferLeg(t.Id, t.Date, t.Amount, t.Type == TransactionType.Expense, t.CounterpartyName))
                .ToList();
            var claimedLegs = new HashSet<int>();

            var unknownEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var imported = 0;
            var reconciled = 0;

            foreach (var tx in cardTransactions)
            {
                var kind = TradeRepublicTimelineClassifier.Classify(tx.Title, tx.EventType, ownerNames, instrumentNames);

                // Aucun euro n'a bougé : un paiement refusé n'a pas à figurer au budget.
                if (kind == TrLineKind.Ignore) continue;

                // Journaliser les eventType non cartographiés. C'est la seule façon de resserrer la
                // table de classement sur ce que TR envoie vraiment, au lieu de suppositions.
                if (!string.IsNullOrWhiteSpace(tx.EventType)
                    && !TradeRepublicTimelineClassifier.IsKnownEventType(tx.EventType))
                    unknownEventTypes.Add(tx.EventType!);

                var externalId = $"{PersoScopeRouter.TradeRepublicExternalIdPrefix}{tx.Id}";

                // Ligne déjà connue : ni ré-import, ni nouveau rapprochement. Le rapprochement n'a
                // lieu qu'une fois, au premier import du mouvement, ce qui le rend idempotent et
                // protège les dépenses dont TR ne renvoie plus le paiement carte correspondant.
                // Cas vécu : le débit de 60,62 € du 12/08/2026 au Leclerc drive, dont l'alimentation
                // TR est bien en base mais dont le paiement carte est sorti de la fenêtre TR. Le
                // neutraliser effacerait la seule trace de cette dépense.
                var existing = await context.Transactions.FirstOrDefaultAsync(t => t.ExternalId == externalId);
                if (existing != null) continue;

                // Un mouvement interne a une jambe bancaire déjà importée par GoCardless. La neutraliser,
                // sinon le paiement carte qu'elle finance est compté deux fois : une fois au débit du
                // compte joint, une fois chez le commerçant.
                if (kind == TrLineKind.InternalTransfer)
                {
                    var mirror = InternalTransferReconciler.FindMirror(
                        Math.Abs(tx.Amount), tx.Date, tx.Amount >= 0m, bankLegs, ownerNames, claimedLegs);

                    if (mirror != null)
                    {
                        claimedLegs.Add(mirror.Id);
                        var legEntity = bankLegEntities.First(t => t.Id == mirror.Id);
                        if (legEntity.CategoryId != virementInterneId)
                        {
                            _logger.LogInformation(
                                "Virement interne rapproche : transaction bancaire {LegId} du {LegDate:yyyy-MM-dd} "
                                + "({Amount} EUR, {Leg}) passe de la categorie {OldCategory} a Virement interne, "
                                + "jambe du mouvement TR {TrTitle}.",
                                legEntity.Id, legEntity.Date, legEntity.Amount, legEntity.Description,
                                legEntity.CategoryId, tx.Title);
                            legEntity.CategoryId = virementInterneId;
                            legEntity.IsFixed = false;
                            reconciled++;
                        }
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Mouvement interne TR sans jambe bancaire rapprochee : {Amount} EUR le {Date:yyyy-MM-dd} ({Title}).",
                            Math.Abs(tx.Amount), tx.Date, tx.Title);
                    }
                }

                int categoryId;
                var isFixed = false;
                CategoryRule? matchedRule = null;
                switch (kind)
                {
                    case TrLineKind.InternalTransfer:
                        categoryId = virementInterneId;
                        break;
                    case TrLineKind.Investment:
                        categoryId = investissementId;
                        break;
                    default:
                        matchedRule = CategoryRuleMatcher.FirstMatch(rules, tx.Title, tx.Title);
                        categoryId = matchedRule?.CategoryId ?? defaultCategoryId;
                        isFixed = matchedRule?.MarkAsFixed ?? false;
                        break;
                }

                var type = tx.Amount >= 0 ? TransactionType.Income : TransactionType.Expense;
                var scope = PersoScopeRouter.Decide(
                    trBankAccountIsPersonal, externalId, type, matchedRule, isInvestment: kind == TrLineKind.Investment);

                var transaction = new Transaction
                {
                    Amount = Math.Abs(tx.Amount),
                    Description = tx.Title,
                    Date = tx.Date,
                    Type = type,
                    CategoryId = categoryId,
                    AccountId = scope == TransactionScope.Perso ? await PersoAccountIdAsync() : defaultAccount.Id,
                    ExternalId = externalId,
                    IsImported = true,
                    CounterpartyName = tx.Title,
                    IsFixed = isFixed,
                    BankAccountId = trBankAccount?.Id
                };

                context.Transactions.Add(transaction);
                imported++;
            }

            if (unknownEventTypes.Count > 0)
                _logger.LogInformation(
                    "Trade Republic : eventType non cartographies, traites comme flux ordinaire - {EventTypes}.",
                    string.Join(", ", unknownEventTypes.OrderBy(e => e, StringComparer.Ordinal)));

            if (imported > 0 || reconciled > 0)
                _logger.LogInformation(
                    "Trade Republic : {Imported} ligne(s) importee(s), {Reconciled} jambe(s) bancaire(s) neutralisee(s).",
                    imported, reconciled);

            connection.LastSyncAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la synchronisation Trade Republic pour la connexion {ConnectionId}.", connection.Id);
            connection.Status = BankConnectionStatus.Error;
            await context.SaveChangesAsync();
            if (rethrow) throw;
        }
    }
}
