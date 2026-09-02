using System.Globalization;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services.Reporting;

/// <summary>
/// Les soldes par compte bancaire physique et le solde consolidé d'un dashboard. Sorti du
/// contrôleur le 02/09/2026, logique inchangée.
///
/// Les agrégats (bilan, courbes) filtrent par compte logique. Les soldes, eux, sont portés par le
/// compte bancaire, qui n'appartient à aucun dashboard : avant le 28/08/2026 le widget Soldes du
/// Commun comptait l'Argenta perso et divergeait du bilan d'exactement ce montant. Règle : un
/// dashboard qui ne contient que le compte logique Perso ne voit que les comptes bancaires marqués
/// perso, un dashboard sans compte Perso ne voit que les autres, un dashboard mixte voit tout.
/// </summary>
public class AccountBalanceService
{
    private readonly AppDbContext _context;

    public AccountBalanceService(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Les comptes bancaires actifs de l'utilisateur (connectés ou manuels) dans le périmètre du dashboard.</summary>
    public async Task<List<BankAccount>> BankAccountsInScopeAsync(int userId, List<int> accountIds)
    {
        var bankAccounts = await _context.BankAccounts
            .Include(ba => ba.BankConnection)
            .Where(ba => ba.IsActive && (
                (ba.BankConnection != null && ba.BankConnection.UserId == userId)
                || (ba.IsManual && ba.UserId == userId)
            ))
            .ToListAsync();

        var scopes = await _context.Accounts
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => a.IsPersonalScope)
            .Distinct()
            .ToListAsync();
        var hasPerso = scopes.Contains(true);
        var hasCommon = scopes.Contains(false);

        if (hasPerso && !hasCommon) return bankAccounts.Where(ba => ba.IsPersonal).ToList();
        if (hasCommon && !hasPerso) return bankAccounts.Where(ba => !ba.IsPersonal).ToList();
        return bankAccounts;
    }

    /// <summary>
    /// Total des soldes du dashboard, ancré sur le solde booké (hors pending) pour les comptes GoCardless :
    /// cohérent avec le net mensuel qui n'agrège que des transactions booked. RealBalance reste utilisé
    /// partout ailleurs (indicateurs, liste de comptes).
    /// </summary>
    public async Task<decimal> TotalBookedBalanceAsync(int userId, List<int> accountIds)
    {
        if (accountIds.Count == 0) return 0m;

        var bankAccounts = await BankAccountsInScopeAsync(userId, accountIds);

        if (bankAccounts.Count == 0)
        {
            // Pas de banque connectée : solde calculé par compte logique.
            var rawTxns = await _context.Transactions
                .Where(t => accountIds.Contains(t.AccountId))
                .Select(t => new { t.Type, t.Amount })
                .ToListAsync();
            return rawTxns.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount)
                 - rawTxns.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount);
        }

        var bankAccountIds = bankAccounts.Where(ba => !ba.IsManual).Select(ba => ba.Id).ToList();
        var rawByBank = await _context.Transactions
            .Where(t => t.BankAccountId != null && bankAccountIds.Contains(t.BankAccountId.Value))
            .Select(t => new { BankAccountId = t.BankAccountId!.Value, t.Type, t.Amount })
            .ToListAsync();

        var byBank = rawByBank
            .GroupBy(t => t.BankAccountId)
            .ToDictionary(
                g => g.Key,
                g => g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount)
                   - g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount));

        var manualTransfers = await ManualTransfersAsync(bankAccounts.Where(ba => ba.IsManual), asOf: null);

        var total = 0m;
        foreach (var ba in bankAccounts)
        {
            if (ba.IsManual)
            {
                total += (ba.InitialBalance ?? 0) + manualTransfers.GetValueOrDefault(ba.Id).Sum;
            }
            else
            {
                var netFallback = byBank.GetValueOrDefault(ba.Id, 0);
                total += ba.BookedBalance ?? ba.RealBalance ?? netFallback;
            }
        }

        return total;
    }

    /// <summary>
    /// Soldes par compte bancaire physique. Préfère le solde réel banque. Si <paramref name="to"/> est
    /// antérieur à maintenant, calcule le solde rétrospectif à cette date.
    /// </summary>
    public async Task<List<AccountBalanceDto>> AccountBalancesAsync(int userId, List<int> accountIds, DateTime? to, DateTime? now = null)
    {
        if (accountIds.Count == 0) return new List<AccountBalanceDto>();

        var today = now ?? DateTime.UtcNow;
        var asOf = to;
        // Une borne au-delà de maintenant (période « Cette année » jusqu'au 31/12) se ramène à aujourd'hui.
        if (asOf.HasValue && asOf.Value > today) asOf = null;
        var historical = asOf.HasValue;

        var bankAccounts = await BankAccountsInScopeAsync(userId, accountIds);

        if (bankAccounts.Count == 0)
        {
            // Pas de banque connectée : un solde calculé par compte logique.
            var fallback = await _context.Accounts
                .Where(a => accountIds.Contains(a.Id))
                .ToListAsync();
            var rawTxns = await _context.Transactions
                .Where(t => accountIds.Contains(t.AccountId)
                         && (!historical || t.Date <= asOf!.Value))
                .Select(t => new { t.AccountId, t.Type, t.Amount, t.Date })
                .ToListAsync();
            return fallback.Select(a =>
            {
                var g = rawTxns.Where(t => t.AccountId == a.Id).ToList();
                return new AccountBalanceDto
                {
                    AccountId = a.Id,
                    AccountName = a.Name,
                    Balance = g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount)
                            - g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
                    IsRealBalance = false,
                    LastTransactionDate = g.Any() ? g.Max(t => t.Date) : (DateTime?)null,
                };
            }).ToList();
        }

        var bankAccountIds = bankAccounts.Where(ba => !ba.IsManual).Select(ba => ba.Id).ToList();
        var rawByBank = await _context.Transactions
            .Where(t => t.BankAccountId != null && bankAccountIds.Contains(t.BankAccountId.Value))
            .Select(t => new { BankAccountId = t.BankAccountId!.Value, t.Type, t.Amount, t.Date })
            .ToListAsync();

        var byBank = rawByBank
            .GroupBy(t => t.BankAccountId)
            .ToDictionary(g => g.Key, g => new
            {
                Income = g.Where(t => t.Type == TransactionType.Income && (!historical || t.Date <= asOf!.Value)).Sum(t => t.Amount),
                Expenses = g.Where(t => t.Type == TransactionType.Expense && (!historical || t.Date <= asOf!.Value)).Sum(t => t.Amount),
                IncomeAfter = historical ? g.Where(t => t.Type == TransactionType.Income && t.Date > asOf!.Value).Sum(t => t.Amount) : 0m,
                ExpensesAfter = historical ? g.Where(t => t.Type == TransactionType.Expense && t.Date > asOf!.Value).Sum(t => t.Amount) : 0m,
                LastDate = g.Where(t => !historical || t.Date <= asOf!.Value).Select(t => (DateTime?)t.Date).DefaultIfEmpty(null).Max(),
            });

        var manualTransfers = await ManualTransfersAsync(bankAccounts.Where(ba => ba.IsManual), asOf);

        return bankAccounts.Select(ba =>
        {
            decimal balance;
            DateTime? lastDate;
            bool isReal;

            if (ba.IsManual)
            {
                var (sum, last) = manualTransfers.GetValueOrDefault(ba.Id, (0, null));
                balance = (ba.InitialBalance ?? 0) + sum;
                lastDate = last;
                isReal = false;
            }
            else
            {
                var stats = byBank.GetValueOrDefault(ba.Id);
                if (historical && ba.RealBalance.HasValue && stats != null)
                {
                    // Solde rétrospectif : solde réel d'aujourd'hui − net des transactions postérieures à asOf.
                    balance = ba.RealBalance.Value - stats.IncomeAfter + stats.ExpensesAfter;
                    isReal = false;
                }
                else
                {
                    balance = ba.RealBalance ?? (stats != null ? stats.Income - stats.Expenses : 0);
                    isReal = !historical && ba.RealBalance.HasValue;
                }
                lastDate = stats?.LastDate;
            }

            return new AccountBalanceDto
            {
                AccountId = ba.Id,
                AccountName = !string.IsNullOrWhiteSpace(ba.AccountName) ? ba.AccountName : ba.Iban,
                BankInstitutionName = ba.BankConnection?.InstitutionName ?? (ba.IsManual ? "Manuel" : null),
                Balance = balance,
                IsRealBalance = isReal,
                IsManual = ba.IsManual,
                LastTransactionDate = lastDate,
                BalanceUpdatedAt = ba.BalanceUpdatedAt ?? ba.InitialBalanceDate,
            };
        })
        .OrderByDescending(b => b.Balance)
        .ToList();
    }

    /// <summary>
    /// Rafraîchit le solde réel des comptes GoCardless de l'utilisateur sans attendre la boucle de six
    /// heures. Un compte manuel se calcule, et le compte espèces Trade Republic est écrit par l'import
    /// TR, son identifiant externe n'existe pas chez GoCardless. Un échec par compte est silencieux, on
    /// garde le solde précédent.
    /// </summary>
    public async Task RefreshRealBalancesAsync(int userId, GoCardlessClient goCardless)
    {
        var bankAccounts = await _context.BankAccounts
            .Include(ba => ba.BankConnection)
            .Where(ba => ba.IsActive && !ba.IsManual
                      && ba.BankConnection != null
                      && ba.BankConnection.UserId == userId
                      && ba.BankConnection.Provider == BankProvider.GoCardless)
            .ToListAsync();

        foreach (var ba in bankAccounts)
        {
            try
            {
                var data = await goCardless.GetBalancesAsync(ba.ExternalAccountId);
                if (!data.TryGetProperty("balances", out var arr)) continue;
                foreach (var b in arr.EnumerateArray())
                {
                    if (b.TryGetProperty("balanceAmount", out var amt) && amt.TryGetProperty("amount", out var v)
                        && decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var bal))
                    {
                        ba.RealBalance = bal;
                        ba.BalanceUpdatedAt = DateTime.UtcNow;
                        break;
                    }
                }
            }
            catch { /* silencieux : on garde le solde précédent */ }
        }
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Versements nets vers chaque compte manuel : sur le compte source, une dépense dans la catégorie
    /// d'incrément alimente le manuel, un revenu dans cette catégorie en retire (miroir exact).
    /// </summary>
    private async Task<Dictionary<int, (decimal Sum, DateTime? LastDate)>> ManualTransfersAsync(IEnumerable<BankAccount> manualAccounts, DateTime? asOf)
    {
        var result = new Dictionary<int, (decimal Sum, DateTime? LastDate)>();
        foreach (var m in manualAccounts)
        {
            if (m.SourceBankAccountId == null || m.IncrementCategoryId == null) continue;
            var transfers = await _context.Transactions
                .Where(t => t.BankAccountId == m.SourceBankAccountId
                         && t.CategoryId == m.IncrementCategoryId
                         && (m.InitialBalanceDate == null || t.Date >= m.InitialBalanceDate)
                         && (asOf == null || t.Date <= asOf.Value))
                .Select(t => new { t.Type, t.Amount, t.Date })
                .ToListAsync();
            result[m.Id] = (
                transfers.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount)
                    - transfers.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                transfers.Any() ? transfers.Max(t => t.Date) : (DateTime?)null
            );
        }
        return result;
    }
}
