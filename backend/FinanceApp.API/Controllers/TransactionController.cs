using System.Security.Claims;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TransactionController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IDashboardService _dashboardService;

    public TransactionController(AppDbContext context, IDashboardService dashboardService)
    {
        _context = context;
        _dashboardService = dashboardService;
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId))
            throw new InvalidOperationException("Claim NameIdentifier absent ou invalide.");
        return userId;
    }

    // Récupère les IDs de comptes visibles : soit via dashboardId, soit le dashboard personnel
    private async Task<List<int>> GetAccountIds(int? dashboardId)
    {
        var userId = GetUserId();

        if (dashboardId.HasValue)
            return await _dashboardService.GetDashboardAccountIds(dashboardId.Value, userId);

        // Fallback : dashboard personnel (premier dashboard créé par le user)
        var personalDashboard = await _context.Dashboards
            .Where(d => d.CreatorId == userId)
            .OrderBy(d => d.CreatedAt)
            .FirstOrDefaultAsync();

        if (personalDashboard == null)
            return new List<int>();

        return await _dashboardService.GetDashboardAccountIds(personalDashboard.Id, userId);
    }

    private static TransactionDto MapToDto(Transaction t)
    {
        return new TransactionDto
        {
            Id = t.Id,
            Amount = t.Amount,
            Description = t.Description,
            Date = t.Date,
            Type = t.Type,
            CategoryId = t.CategoryId,
            CategoryName = t.Category.Name,
            CategoryIcon = t.Category.Icon,
            CategoryColor = t.Category.Color,
            AccountId = t.AccountId,
            AccountName = t.Account.Name,
            ExternalId = t.ExternalId,
            IsImported = t.IsImported,
            CounterpartyName = t.CounterpartyName,
            IsExceptional = t.IsExceptional,
            BankAccountName = t.BankAccount?.AccountName,
            BankInstitutionName = t.BankAccount?.BankConnection?.InstitutionName
        };
    }

    [HttpGet]
    public async Task<ActionResult<List<TransactionDto>>> GetAll(
        [FromQuery] int? dashboardId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? categoryId,
        [FromQuery] TransactionType? type,
        [FromQuery] int? accountId,
        [FromQuery] int? bankAccountId,
        [FromQuery] bool? isExceptional,
        [FromQuery] string? search,
        [FromQuery] string? sortBy,
        [FromQuery] bool? sortDesc)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<TransactionDto>());

        var query = _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Where(t => accountIds.Contains(t.AccountId));

        if (from.HasValue) query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue) query = query.Where(t => t.Date <= to.Value);
        if (categoryId.HasValue) query = query.Where(t => t.CategoryId == categoryId.Value);
        if (type.HasValue) query = query.Where(t => t.Type == type.Value);
        if (accountId.HasValue) query = query.Where(t => t.AccountId == accountId.Value);
        if (bankAccountId.HasValue) query = query.Where(t => t.BankAccountId == bankAccountId.Value);
        if (isExceptional.HasValue) query = query.Where(t => t.IsExceptional == isExceptional.Value);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(t => t.Description.Contains(search) || t.Category.Name.Contains(search) || t.Account.Name.Contains(search));

        var descending = sortDesc ?? true;
        query = sortBy?.ToLower() switch
        {
            "description" => descending ? query.OrderByDescending(t => t.Description) : query.OrderBy(t => t.Description),
            "account" => descending ? query.OrderByDescending(t => t.Account.Name) : query.OrderBy(t => t.Account.Name),
            "category" => descending ? query.OrderByDescending(t => t.Category.Name) : query.OrderBy(t => t.Category.Name),
            "amount" => descending ? query.OrderByDescending(t => t.Amount) : query.OrderBy(t => t.Amount),
            _ => descending ? query.OrderByDescending(t => t.Date) : query.OrderBy(t => t.Date),
        };

        var transactions = await query
            .Select(t => MapToDto(t))
            .ToListAsync();

        return Ok(transactions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TransactionDto>> GetById(int id)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        return Ok(MapToDto(transaction));
    }

    [HttpPost]
    public async Task<ActionResult<TransactionDto>> Create(CreateTransactionDto dto)
    {
        var userId = GetUserId();

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == userId);
        if (account == null) return BadRequest("Compte invalide.");

        var category = await _context.Categories.FirstOrDefaultAsync(
            c => c.Id == dto.CategoryId && (c.IsDefault || c.UserId == userId));
        if (category == null) return BadRequest("Catégorie invalide.");

        var transaction = new Transaction
        {
            Amount = dto.Amount,
            Description = dto.Description,
            Date = dto.Date,
            Type = dto.Type,
            CategoryId = dto.CategoryId,
            AccountId = dto.AccountId
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = transaction.Id }, new TransactionDto
        {
            Id = transaction.Id,
            Amount = transaction.Amount,
            Description = transaction.Description,
            Date = transaction.Date,
            Type = transaction.Type,
            CategoryId = transaction.CategoryId,
            CategoryName = category.Name,
            CategoryIcon = category.Icon,
            CategoryColor = category.Color,
            AccountId = account.Id,
            AccountName = account.Name,
            ExternalId = transaction.ExternalId,
            IsImported = transaction.IsImported,
            CounterpartyName = transaction.CounterpartyName,
            IsExceptional = transaction.IsExceptional
        });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TransactionDto>> Update(int id, UpdateTransactionDto dto)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == userId);
        if (account == null) return BadRequest("Compte invalide.");

        var category = await _context.Categories.FirstOrDefaultAsync(
            c => c.Id == dto.CategoryId && (c.IsDefault || c.UserId == userId));
        if (category == null) return BadRequest("Catégorie invalide.");

        transaction.Amount = dto.Amount;
        transaction.Description = dto.Description;
        transaction.Date = dto.Date;
        transaction.Type = dto.Type;
        transaction.CategoryId = dto.CategoryId;
        transaction.AccountId = dto.AccountId;

        await _context.SaveChangesAsync();

        return Ok(new TransactionDto
        {
            Id = transaction.Id,
            Amount = transaction.Amount,
            Description = transaction.Description,
            Date = transaction.Date,
            Type = transaction.Type,
            CategoryId = transaction.CategoryId,
            CategoryName = category.Name,
            CategoryIcon = category.Icon,
            CategoryColor = category.Color,
            AccountId = account.Id,
            AccountName = account.Name,
            ExternalId = transaction.ExternalId,
            IsImported = transaction.IsImported,
            CounterpartyName = transaction.CounterpartyName,
            IsExceptional = transaction.IsExceptional,
            BankAccountName = transaction.BankAccount?.AccountName,
            BankInstitutionName = transaction.BankAccount?.BankConnection?.InstitutionName
        });
    }

    [HttpPut("{id}/exceptional")]
    public async Task<ActionResult<TransactionDto>> SetExceptional(int id, SetExceptionalDto dto)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        transaction.IsExceptional = dto.IsExceptional;
        await _context.SaveChangesAsync();

        return Ok(MapToDto(transaction));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions
            .Include(t => t.Account)
            .FirstOrDefaultAsync(t => t.Id == id && t.Account.UserId == userId);

        if (transaction == null) return NotFound();

        _context.Transactions.Remove(transaction);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("recategorize")]
    public async Task<ActionResult<object>> Recategorize()
    {
        var userId = GetUserId();

        var rules = await _context.CategoryRules
            .Where(cr => cr.UserId == userId)
            .ToListAsync();

        var defaultCategory = await _context.Categories
            .FirstOrDefaultAsync(c => c.Name == "Autres" && c.IsDefault);
        var defaultCategoryId = defaultCategory?.Id ?? 10;

        var accounts = await _context.Accounts
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync();

        // Ne toucher que les transactions encore en catégorie par défaut : tout le reste est
        // soit un match de règle antérieur, soit une correction manuelle qu'on ne doit pas écraser.
        var transactions = await _context.Transactions
            .Where(t => accounts.Contains(t.AccountId) && t.IsImported && t.CategoryId == defaultCategoryId)
            .ToListAsync();

        int updated = 0;
        foreach (var tx in transactions)
        {
            foreach (var rule in rules)
            {
                if (tx.Description.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase) ||
                    (tx.CounterpartyName != null && tx.CounterpartyName.Contains(rule.Keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    tx.CategoryId = rule.CategoryId;
                    updated++;
                    break;
                }
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { updated, total = transactions.Count });
    }

    [HttpGet("summary")]
    public async Task<ActionResult<TransactionSummaryDto>> GetSummary(
        [FromQuery] int? dashboardId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? bankAccountId,
        [FromQuery] bool includeExceptional = true)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any())
        {
            return Ok(new TransactionSummaryDto
            {
                TotalIncome = 0,
                TotalExpenses = 0,
                Balance = 0,
                CategoryBreakdown = new(),
                MonthlyBalance = new()
            });
        }

        // SQLite ne supporte pas Sum(decimal) en SQL → tout en client
        var baseQuery = _context.Transactions.Where(t => accountIds.Contains(t.AccountId));
        if (from.HasValue) baseQuery = baseQuery.Where(t => t.Date >= from.Value);
        if (to.HasValue) baseQuery = baseQuery.Where(t => t.Date <= to.Value);
        if (bankAccountId.HasValue) baseQuery = baseQuery.Where(t => t.BankAccountId == bankAccountId.Value);

        var rawAll = await baseQuery
            .Select(t => new
            {
                t.Type,
                t.Amount,
                CategoryId = t.Category.Id,
                CategoryName = t.Category.Name,
                CategoryIcon = t.Category.Icon,
                CategoryColor = t.Category.Color,
                IsTransfer = t.Category.IsTransfer,
                t.IsExceptional,
            })
            .ToListAsync();

        // Dépenses exceptionnelles (non-transfert) de la période — toujours calculée, sert au libellé "dont X € exceptionnels"
        var exceptionalExpenses = rawAll.Where(t => t.Type == TransactionType.Expense && !t.IsTransfer && t.IsExceptional).Sum(t => t.Amount);

        // Filtre flux : quand includeExceptional == false, on retire les transactions exceptionnelles des agrégats de flux
        // (mais PAS de la courbe solde total plus bas — ces dépenses ont réellement eu lieu)
        var flux = includeExceptional ? rawAll : rawAll.Where(t => !t.IsExceptional).ToList();

        // Exclure les transferts internes (épargne, comptes joints) des stats dépenses/revenus
        var totalIncome = flux.Where(t => t.Type == TransactionType.Income && !t.IsTransfer).Sum(t => t.Amount);
        var totalExpenses = flux.Where(t => t.Type == TransactionType.Expense && !t.IsTransfer).Sum(t => t.Amount);
        // Mise de côté = dépenses sur catégories transfert (Épargne perso, etc.) — exposée séparément pour visibilité
        var totalSavings = flux.Where(t => t.Type == TransactionType.Expense && t.IsTransfer).Sum(t => t.Amount);

        var expensesByCategory = flux
            .Where(t => t.Type == TransactionType.Expense && !t.IsTransfer)
            .GroupBy(t => new { t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor })
            .Select(g =>
            {
                var amount = g.Sum(t => t.Amount);
                return new CategoryBreakdownDto
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    CategoryIcon = g.Key.CategoryIcon,
                    CategoryColor = g.Key.CategoryColor,
                    Amount = amount,
                    Percentage = totalExpenses > 0 ? Math.Round(amount / totalExpenses * 100, 1) : 0,
                };
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        var savingsByCategory = flux
            .Where(t => t.Type == TransactionType.Expense && t.IsTransfer)
            .GroupBy(t => new { t.CategoryId, t.CategoryName, t.CategoryIcon, t.CategoryColor })
            .Select(g =>
            {
                var amount = g.Sum(t => t.Amount);
                return new CategoryBreakdownDto
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    CategoryIcon = g.Key.CategoryIcon,
                    CategoryColor = g.Key.CategoryColor,
                    Amount = amount,
                    Percentage = totalSavings > 0 ? Math.Round(amount / totalSavings * 100, 1) : 0,
                };
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-5);
        var startOfMonth = new DateTime(sixMonthsAgo.Year, sixMonthsAgo.Month, 1);

        // SQLite : agrégation client (exclut transferts internes)
        var rawMonthly = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= startOfMonth)
            .Select(t => new { t.Date.Year, t.Date.Month, t.Type, t.Amount, t.Category.IsTransfer, t.IsExceptional })
            .ToListAsync();

        // Barres Income/Expenses : mêmes exclusions que les KPI (transferts + exceptionnelles si includeExceptional == false)
        var monthlyData = rawMonthly
            .Where(t => !t.IsTransfer && (includeExceptional || !t.IsExceptional))
            .GroupBy(t => new { t.Year, t.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Income = g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                Expenses = g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount),
            })
            .ToList();

        // Solde total actuel du dashboard, ancré sur le solde booké (hors pending) plutôt que RealBalance/interimAvailable —
        // cohérence avec le net mensuel qui n'agrège que des transactions booked (sinon le pending décale tous les points passés).
        var currentTotalBalance = await GetTotalBookedBalanceAsync(dashboardId);

        // Net mensuel sur tous les comptes du dashboard (ignore le filtre bankAccountId — le solde reste consolidé)
        // Les IsTransfer s'annulent (transferts internes) ou n'existent pas (alimente un manuel, dont l'effet est dans currentTotal)
        var rawForBalance = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= startOfMonth)
            .Select(t => new { t.Date.Year, t.Date.Month, t.Type, t.Amount, IsTransfer = t.Category.IsTransfer })
            .ToListAsync();
        var monthlyNet = rawForBalance
            .Where(t => !t.IsTransfer)
            .GroupBy(t => new { t.Year, t.Month })
            .ToDictionary(
                g => (g.Key.Year, g.Key.Month),
                g => g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount)
                   - g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount));

        var monthlyBalance = Enumerable.Range(0, 6)
            .Select(i => startOfMonth.AddMonths(i))
            .Select(month =>
            {
                var data = monthlyData.FirstOrDefault(d => d.Year == month.Year && d.Month == month.Month);
                // Solde fin-de-mois = solde actuel - net cumulé des mois strictement postérieurs
                var netAfter = monthlyNet
                    .Where(kv => kv.Key.Year > month.Year || (kv.Key.Year == month.Year && kv.Key.Month > month.Month))
                    .Sum(kv => kv.Value);
                return new MonthlyBalanceDto
                {
                    Month = month.ToString("MMM yyyy"),
                    Income = data?.Income ?? 0,
                    Expenses = data?.Expenses ?? 0,
                    Balance = (data?.Income ?? 0) - (data?.Expenses ?? 0),
                    TotalBalance = currentTotalBalance - netAfter
                };
            })
            .ToList();

        return Ok(new TransactionSummaryDto
        {
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            Balance = totalIncome - totalExpenses,
            TotalSavings = totalSavings,
            ExceptionalExpenses = exceptionalExpenses,
            CategoryBreakdown = expensesByCategory,
            SavingsBreakdown = savingsByCategory,
            MonthlyBalance = monthlyBalance
        });
    }

    /// <summary>
    /// Total des soldes du dashboard, ancré sur le solde booké (hors pending) pour les comptes GoCardless.
    /// Réplique la résolution manuel/GoCardless de <see cref="GetAccountBalances"/> (branche non-historique),
    /// mais préfère BookedBalance à RealBalance — RealBalance (interimAvailable) reste utilisé partout ailleurs
    /// (KPI, liste de comptes) et n'est pas affecté par ce helper.
    /// </summary>
    private async Task<decimal> GetTotalBookedBalanceAsync(int? dashboardId)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return 0m;

        var userId = GetUserId();

        var bankAccounts = await _context.BankAccounts
            .Include(ba => ba.BankConnection)
            .Where(ba => ba.IsActive && (
                (ba.BankConnection != null && ba.BankConnection.UserId == userId)
                || (ba.IsManual && ba.UserId == userId)
            ))
            .ToListAsync();

        if (!bankAccounts.Any())
        {
            // Fallback : pas de banque connectée → solde calculé par Account interne (identique à GetAccountBalances)
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

        var manualAccounts = bankAccounts.Where(ba => ba.IsManual).ToList();
        var manualTransfers = new Dictionary<int, decimal>();
        foreach (var m in manualAccounts)
        {
            if (m.SourceBankAccountId == null || m.IncrementCategoryId == null) continue;
            var transfers = await _context.Transactions
                .Where(t => t.BankAccountId == m.SourceBankAccountId
                         && t.CategoryId == m.IncrementCategoryId
                         && t.Type == TransactionType.Expense
                         && (m.InitialBalanceDate == null || t.Date >= m.InitialBalanceDate))
                .Select(t => t.Amount)
                .ToListAsync();
            manualTransfers[m.Id] = transfers.Sum();
        }

        var total = 0m;
        foreach (var ba in bankAccounts)
        {
            if (ba.IsManual)
            {
                // Comptes manuels : résolution inchangée (pas de notion de booké/pending côté banque)
                total += (ba.InitialBalance ?? 0) + manualTransfers.GetValueOrDefault(ba.Id, 0);
            }
            else
            {
                var netFallback = byBank.GetValueOrDefault(ba.Id, 0);
                total += ba.BookedBalance ?? ba.RealBalance ?? netFallback;
            }
        }

        return total;
    }

    /// <summary>Transactions encore catégorisées en "Autres" (CategoryId == 10).</summary>
    [HttpGet("uncategorized")]
    public async Task<ActionResult<List<TransactionDto>>> GetUncategorized(
        [FromQuery] int? dashboardId,
        [FromQuery] int? bankAccountId,
        [FromQuery] int limit = 50)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<TransactionDto>());

        const int othersCategoryId = 10;
        var query = _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Where(t => accountIds.Contains(t.AccountId) && t.CategoryId == othersCategoryId);
        if (bankAccountId.HasValue) query = query.Where(t => t.BankAccountId == bankAccountId.Value);

        var raw = await query.ToListAsync();
        var txns = raw.OrderByDescending(t => t.Amount).Take(limit).ToList();
        return Ok(txns.Select(MapToDto).ToList());
    }

    /// <summary>5 dernières transactions, indépendamment de tout filtre.</summary>
    [HttpGet("recent")]
    public async Task<ActionResult<List<TransactionDto>>> GetRecent(
        [FromQuery] int? dashboardId,
        [FromQuery] int? bankAccountId,
        [FromQuery] int limit = 5)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<TransactionDto>());

        var query = _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Where(t => accountIds.Contains(t.AccountId));
        if (bankAccountId.HasValue) query = query.Where(t => t.BankAccountId == bankAccountId.Value);

        var txns = await query
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .Take(limit)
            .ToListAsync();

        return Ok(txns.Select(MapToDto).ToList());
    }

    /// <summary>Top dépenses inhabituelles : 3 plus grosses au-dessus de 2× la médiane catégorie.</summary>
    [HttpGet("anomalies")]
    public async Task<ActionResult<List<TransactionDto>>> GetAnomalies(
        [FromQuery] int? dashboardId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int limit = 3)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<TransactionDto>());

        var now = DateTime.UtcNow;
        var fromDate = from ?? new DateTime(now.Year, now.Month, 1);
        var toDate = to ?? fromDate.AddMonths(1).AddTicks(-1);

        var historyStart = fromDate.AddMonths(-6);
        var allExpenses = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId)
                     && t.Type == TransactionType.Expense
                     && !t.Category.IsTransfer
                     && t.Date >= historyStart
                     && t.Date <= toDate)
            .Select(t => new { t.Id, t.CategoryId, t.Amount, t.Date })
            .ToListAsync();

        var avgByCategory = allExpenses
            .Where(e => e.Date < fromDate)
            .GroupBy(e => e.CategoryId)
            .ToDictionary(g => g.Key, g => g.Average(e => e.Amount));

        var anomalyIds = allExpenses
            .Where(e => e.Date >= fromDate && e.Date <= toDate)
            .Where(e => avgByCategory.TryGetValue(e.CategoryId, out var avg) && e.Amount > avg * 2 && e.Amount > 30)
            .OrderByDescending(e => e.Amount)
            .Take(limit)
            .Select(e => e.Id)
            .ToList();

        if (!anomalyIds.Any()) return Ok(new List<TransactionDto>());

        // SQLite ne sait pas trier par decimal → tri client
        var rawTxns = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Where(t => anomalyIds.Contains(t.Id))
            .ToListAsync();
        var txns = rawTxns.OrderByDescending(t => t.Amount).ToList();

        return Ok(txns.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Soldes par compte bancaire physique (un par BankAccount). Préfère le solde réel banque.
    /// Si <paramref name="to"/> est passé et antérieur à maintenant, calcule le solde rétrospectif à cette date.
    /// </summary>
    [HttpGet("account-balances")]
    public async Task<ActionResult<List<AccountBalanceDto>>> GetAccountBalances(
        [FromQuery] int? dashboardId,
        [FromQuery] DateTime? to)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<AccountBalanceDto>());

        var userId = GetUserId();
        var now = DateTime.UtcNow;
        var asOf = to;
        // Si la borne dépasse maintenant (ex: période "Cette année" jusqu'à 31 déc), on plafonne à now
        if (asOf.HasValue && asOf.Value > now) asOf = null;
        var historical = asOf.HasValue;

        // Comptes connectés via BankConnection + comptes manuels (BankConnection.UserId == userId OU IsManual && UserId == userId)
        var bankAccounts = await _context.BankAccounts
            .Include(ba => ba.BankConnection)
            .Where(ba => ba.IsActive && (
                (ba.BankConnection != null && ba.BankConnection.UserId == userId)
                || (ba.IsManual && ba.UserId == userId)
            ))
            .ToListAsync();

        if (!bankAccounts.Any())
        {
            // Fallback : pas de banque connectée → un solde calculé par Account interne
            var fallback = await _context.Accounts
                .Where(a => accountIds.Contains(a.Id))
                .ToListAsync();
            var rawTxns = await _context.Transactions
                .Where(t => accountIds.Contains(t.AccountId)
                         && (!historical || t.Date <= asOf!.Value))
                .Select(t => new { t.AccountId, t.Type, t.Amount, t.Date })
                .ToListAsync();
            return Ok(fallback.Select(a =>
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
            }).ToList());
        }

        // Stats par BankAccountId (pour les comptes connectés : Σ income/expense, et stats post-asOf pour rétrospective)
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

        // Pour les comptes manuels : charger les transferts entrants (transactions cat=IncrementCategory, ba=SourceBankAccount, expense, date>=InitialDate)
        // Si asOf est passé, on borne à asOf — solde rétrospectif au passage de la borne.
        var manualAccounts = bankAccounts.Where(ba => ba.IsManual).ToList();
        var manualTransfers = new Dictionary<int, (decimal Sum, DateTime? LastDate)>();
        foreach (var m in manualAccounts)
        {
            if (m.SourceBankAccountId == null || m.IncrementCategoryId == null) continue;
            var transfers = await _context.Transactions
                .Where(t => t.BankAccountId == m.SourceBankAccountId
                         && t.CategoryId == m.IncrementCategoryId
                         && t.Type == TransactionType.Expense
                         && (m.InitialBalanceDate == null || t.Date >= m.InitialBalanceDate)
                         && (!historical || t.Date <= asOf!.Value))
                .Select(t => new { t.Amount, t.Date })
                .ToListAsync();
            manualTransfers[m.Id] = (
                transfers.Sum(t => t.Amount),
                transfers.Any() ? transfers.Max(t => t.Date) : (DateTime?)null
            );
        }

        var result = bankAccounts.Select(ba =>
        {
            decimal balance;
            DateTime? lastDate;
            bool isReal;

            if (ba.IsManual)
            {
                var (sum, last) = manualTransfers.GetValueOrDefault(ba.Id, (0, null));
                balance = (ba.InitialBalance ?? 0) + sum;
                lastDate = last;
                isReal = false; // calculé, pas un solde banque
            }
            else
            {
                var stats = byBank.GetValueOrDefault(ba.Id);
                if (historical && ba.RealBalance.HasValue && stats != null)
                {
                    // Solde rétrospectif : RealBalance d'aujourd'hui - net des transactions postérieures à asOf
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

        return Ok(result);
    }

    /// <summary>Force la récupération des soldes réels via GoCardless (sans attendre le sync 6h).</summary>
    [HttpPost("refresh-balances")]
    public async Task<ActionResult<List<AccountBalanceDto>>> RefreshBalances(
        [FromQuery] int? dashboardId,
        [FromServices] GoCardlessClient goCardless)
    {
        var accountIds = await GetAccountIds(dashboardId);
        if (!accountIds.Any()) return Ok(new List<AccountBalanceDto>());

        var bankAccounts = await _context.BankAccounts
            .Include(ba => ba.BankConnection)
            .Where(ba => ba.IsActive)
            .ToListAsync();

        foreach (var ba in bankAccounts)
        {
            try
            {
                var data = await goCardless.GetBalancesAsync(ba.ExternalAccountId);
                if (data.TryGetProperty("balances", out var arr))
                {
                    foreach (var b in arr.EnumerateArray())
                    {
                        if (b.TryGetProperty("balanceAmount", out var amt) && amt.TryGetProperty("amount", out var v))
                        {
                            if (decimal.TryParse(v.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var bal))
                            {
                                ba.RealBalance = bal;
                                ba.BalanceUpdatedAt = DateTime.UtcNow;
                                break;
                            }
                        }
                    }
                }
            }
            catch { /* silencieux : on garde le fallback */ }
        }
        await _context.SaveChangesAsync();

        return await GetAccountBalances(dashboardId, null);
    }
}

