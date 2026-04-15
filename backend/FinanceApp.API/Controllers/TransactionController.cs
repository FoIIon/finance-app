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
            CounterpartyName = transaction.CounterpartyName
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
            BankAccountName = transaction.BankAccount?.AccountName,
            BankInstitutionName = transaction.BankAccount?.BankConnection?.InstitutionName
        });
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

    [HttpGet("summary")]
    public async Task<ActionResult<TransactionSummaryDto>> GetSummary([FromQuery] int? dashboardId)
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

        var totalIncome = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Type == TransactionType.Income)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        var totalExpenses = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Type == TransactionType.Expense)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        var expensesByCategory = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Type == TransactionType.Expense)
            .GroupBy(t => new { t.Category.Name, t.Category.Icon, t.Category.Color })
            .Select(g => new CategoryBreakdownDto
            {
                CategoryName = g.Key.Name,
                CategoryIcon = g.Key.Icon,
                CategoryColor = g.Key.Color,
                Amount = g.Sum(t => t.Amount),
                Percentage = totalExpenses > 0 ? Math.Round(g.Sum(t => t.Amount) / totalExpenses * 100, 1) : 0
            })
            .OrderByDescending(c => c.Amount)
            .ToListAsync();

        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-5);
        var startOfMonth = new DateTime(sixMonthsAgo.Year, sixMonthsAgo.Month, 1);

        var monthlyData = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId) && t.Date >= startOfMonth)
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Income = g.Where(t => t.Type == TransactionType.Income).Sum(t => (decimal?)t.Amount) ?? 0,
                Expenses = g.Where(t => t.Type == TransactionType.Expense).Sum(t => (decimal?)t.Amount) ?? 0
            })
            .ToListAsync();

        var monthlyBalance = Enumerable.Range(0, 6)
            .Select(i => startOfMonth.AddMonths(i))
            .Select(month =>
            {
                var data = monthlyData.FirstOrDefault(d => d.Year == month.Year && d.Month == month.Month);
                return new MonthlyBalanceDto
                {
                    Month = month.ToString("MMM yyyy"),
                    Income = data?.Income ?? 0,
                    Expenses = data?.Expenses ?? 0,
                    Balance = (data?.Income ?? 0) - (data?.Expenses ?? 0)
                };
            })
            .ToList();

        return Ok(new TransactionSummaryDto
        {
            TotalIncome = totalIncome,
            TotalExpenses = totalExpenses,
            Balance = totalIncome - totalExpenses,
            CategoryBreakdown = expensesByCategory,
            MonthlyBalance = monthlyBalance
        });
    }
}

