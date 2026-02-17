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
        var goCardless = serviceProvider.GetRequiredService<GoCardlessClient>();

        var connection = await context.BankConnections
            .Include(bc => bc.BankAccounts)
            .FirstOrDefaultAsync(bc => bc.Id == connectionId);

        if (connection == null) return;

        // Vérifier que la réquisition est toujours valide
        try
        {
            var requisition = await goCardless.GetRequisitionAsync(connection.RequisitionId);
            var status = requisition.GetProperty("status").GetString();
            if (status == "EX")
            {
                connection.Status = BankConnectionStatus.Expired;
                await context.SaveChangesAsync();
                _logger.LogWarning("Réquisition expirée pour la connexion {ConnectionId}.", connectionId);
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
                var transactionsData = await goCardless.GetTransactionsAsync(account.ExternalAccountId, dateFrom);

                if (!transactionsData.TryGetProperty("transactions", out var transactions))
                    continue;

                if (!transactions.TryGetProperty("booked", out var booked))
                    continue;

                // Log temporaire : structure JSON d'une transaction GoCardless
                var firstTx = booked.EnumerateArray().FirstOrDefault();
                if (firstTx.ValueKind != System.Text.Json.JsonValueKind.Undefined)
                    _logger.LogInformation("GoCardless transaction sample: {Json}", firstTx.GetRawText());

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

                    // Appliquer les règles de catégorisation
                    var categoryId = defaultCategoryId;
                    foreach (var rule in rules)
                    {
                        if (description.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            categoryId = rule.CategoryId;
                            break;
                        }
                    }

                    var counterparty = tx.TryGetProperty("creditorName", out var cred)
                        ? cred.GetString()
                        : tx.TryGetProperty("debtorName", out var deb)
                            ? deb.GetString()
                            : null;

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
                        CounterpartyName = counterparty
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
}
