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
    }

    /// <summary>
    /// Synchronisation manuelle depuis le controller — laisse remonter les exceptions
    /// </summary>
    public async Task SyncConnectionAsync(int connectionId)
    {
        using var scope = _scopeFactory.CreateScope();
        await SyncConnectionInternalAsync(connectionId, scope.ServiceProvider, rethrow: true);
    }

    private async Task SyncConnectionInternalAsync(int connectionId, IServiceProvider serviceProvider, bool rethrow = false)
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
            await SyncGoCardlessAsync(connection, context, serviceProvider, rethrow);
        }
    }

    private async Task SyncGoCardlessAsync(BankConnection connection, AppDbContext context, IServiceProvider serviceProvider, bool rethrow)
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
        var rules = await context.CategoryRules
            .Where(cr => cr.UserId == connection.UserId)
            .ToListAsync();

        // Catégorie par défaut : "Autres"
        var defaultCategory = await context.Categories
            .FirstOrDefaultAsync(c => c.Name == "Autres" && c.IsDefault);
        var defaultCategoryId = defaultCategory?.Id ?? 10;

        // Compte par défaut de l'utilisateur pour les transactions importées
        var defaultAccount = await context.Accounts
            .Where(a => a.UserId == connection.UserId)
            .OrderBy(a => a.CreatedAt)
            .FirstOrDefaultAsync();

        if (defaultAccount == null)
        {
            _logger.LogWarning("Aucun compte trouvé pour l'utilisateur {UserId}.", connection.UserId);
            return;
        }

        // Remonter à 90 jours s'il reste des transactions sans counterparty
        var hasMissingCounterparty = await context.Transactions
            .AnyAsync(t => t.AccountId == defaultAccount.Id && t.IsImported && t.CounterpartyName == null);
        var dateFrom = hasMissingCounterparty
            ? DateTime.UtcNow.AddDays(-90)
            : connection.LastSyncAt ?? DateTime.UtcNow.AddDays(-90);

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
                        // Préférer interimAvailable (= solde dispo), sinon expected, sinon premier
                        var balanceTypes = new[] { "interimAvailable", "expected", "interimBooked", "closingBooked" };
                        JsonElement bal = default;
                        bool found = false;
                        foreach (var prefType in balanceTypes)
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
                            if (decimal.TryParse(amountStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var bookedBalance))
                            {
                                account.RealBalance = bookedBalance;
                                account.BalanceUpdatedAt = DateTime.UtcNow;
                            }
                        }
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

                    // Appliquer les règles : description en priorité, puis counterparty
                    var categoryId = defaultCategoryId;
                    foreach (var rule in rules)
                    {
                        if (description.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase) ||
                            (counterparty != null && counterparty.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase)))
                        {
                            categoryId = rule.CategoryId;
                            break;
                        }
                    }

                    var transaction = new Transaction
                    {
                        Amount = Math.Abs(parsedAmount),
                        Description = description,
                        Date = bookingDate,
                        Type = parsedAmount >= 0 ? TransactionType.Income : TransactionType.Expense,
                        CategoryId = categoryId,
                        AccountId = defaultAccount.Id,
                        ExternalId = externalId,
                        IsImported = true,
                        CounterpartyName = counterparty,
                        BankAccountId = account.Id
                    };

                    context.Transactions.Add(transaction);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erreur lors de la synchronisation du compte {AccountId}.", account.ExternalAccountId);
                if (rethrow) throw;
            }
        }

        connection.LastSyncAt = DateTime.UtcNow;
        await context.SaveChangesAsync();
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
            if (string.IsNullOrEmpty(connection.EncryptedSessionToken))
                throw new InvalidOperationException("Session Trade Republic expirée — veuillez relancer la connexion.");

            var sessionToken = trClient.DecryptToken(connection.EncryptedSessionToken);

            // Récupérer les card transactions via HTTP REST (TR a abandonné le WebSocket pour les données)
            // La déduplication par ExternalId évite les doublons — pas besoin de filtrer par date
            var cardTransactions = await trClient.GetCardTransactionsHttpAsync(sessionToken);

            // Charger les règles de catégorisation
            var rules = await context.CategoryRules
                .Where(cr => cr.UserId == connection.UserId)
                .ToListAsync();

            var defaultCategory = await context.Categories
                .FirstOrDefaultAsync(c => c.Name == "Autres" && c.IsDefault);
            var defaultCategoryId = defaultCategory?.Id ?? 10;

            var defaultAccount = await context.Accounts
                .Where(a => a.UserId == connection.UserId)
                .OrderBy(a => a.CreatedAt)
                .FirstOrDefaultAsync();

            if (defaultAccount == null)
            {
                _logger.LogWarning("Aucun compte trouvé pour l'utilisateur {UserId}.", connection.UserId);
                return;
            }

            foreach (var tx in cardTransactions)
            {
                var externalId = $"tr-{tx.Id}";

                var existing = await context.Transactions.FirstOrDefaultAsync(t => t.ExternalId == externalId);
                if (existing != null) continue;

                var categoryId = defaultCategoryId;
                foreach (var rule in rules)
                {
                    if (tx.Title.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase))
                    {
                        categoryId = rule.CategoryId;
                        break;
                    }
                }

                var transaction = new Transaction
                {
                    Amount = Math.Abs(tx.Amount),
                    Description = tx.Title,
                    Date = tx.Date,
                    Type = tx.Amount >= 0 ? TransactionType.Income : TransactionType.Expense,
                    CategoryId = categoryId,
                    AccountId = defaultAccount.Id,
                    ExternalId = externalId,
                    IsImported = true,
                    CounterpartyName = tx.Title,
                    BankAccountId = connection.BankAccounts.FirstOrDefault(ba => ba.IsActive)?.Id
                };

                context.Transactions.Add(transaction);
            }

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
