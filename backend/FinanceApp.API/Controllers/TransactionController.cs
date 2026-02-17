using System.Security.Claims;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
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

    public TransactionController(AppDbContext context)
    {
        _context = context;
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId))
            throw new InvalidOperationException("Claim NameIdentifier absent ou invalide.");
        return userId;
    }

    private static TransactionDto MapToDto(Transaction t, Category? category = null)
    {
        var cat = category ?? t.Category;
        return new TransactionDto
        {
            Id = t.Id,
            Amount = t.Amount,
            Description = t.Description,
            Date = t.Date,
            Type = t.Type,
            CategoryId = t.CategoryId,
            CategoryName = cat.Name,
            CategoryIcon = cat.Icon,
            CategoryColor = cat.Color
        };
    }

    [HttpGet]
    public async Task<ActionResult<List<TransactionDto>>> GetAll(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? categoryId,
        [FromQuery] TransactionType? type)
    {
        var userId = GetUserId();
        var query = _context.Transactions
            .Include(t => t.Category)
            .Where(t => t.UserId == userId);

        if (from.HasValue) query = query.Where(t => t.Date >= from.Value);
        if (to.HasValue) query = query.Where(t => t.Date <= to.Value);
        if (categoryId.HasValue) query = query.Where(t => t.CategoryId == categoryId.Value);
        if (type.HasValue) query = query.Where(t => t.Type == type.Value);

        var transactions = await query
            .OrderByDescending(t => t.Date)
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
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (transaction == null) return NotFound();

        return Ok(MapToDto(transaction));
    }

    [HttpPost]
    public async Task<ActionResult<TransactionDto>> Create(CreateTransactionDto dto)
    {
        var userId = GetUserId();
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
            UserId = userId
        };

        _context.Transactions.Add(transaction);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = transaction.Id }, MapToDto(transaction, category));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TransactionDto>> Update(int id, UpdateTransactionDto dto)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions.FirstOrDefaultAsync(
            t => t.Id == id && t.UserId == userId);

        if (transaction == null) return NotFound();

        var category = await _context.Categories.FirstOrDefaultAsync(
            c => c.Id == dto.CategoryId && (c.IsDefault || c.UserId == userId));
        if (category == null) return BadRequest("Catégorie invalide.");

        transaction.Amount = dto.Amount;
        transaction.Description = dto.Description;
        transaction.Date = dto.Date;
        transaction.Type = dto.Type;
        transaction.CategoryId = dto.CategoryId;

        await _context.SaveChangesAsync();

        return Ok(MapToDto(transaction, category));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var transaction = await _context.Transactions.FirstOrDefaultAsync(
            t => t.Id == id && t.UserId == userId);

        if (transaction == null) return NotFound();

        _context.Transactions.Remove(transaction);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("summary")]
    public async Task<ActionResult<TransactionSummaryDto>> GetSummary()
    {
        var userId = GetUserId();

        var totalIncome = await _context.Transactions
            .Where(t => t.UserId == userId && t.Type == TransactionType.Income)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        var totalExpenses = await _context.Transactions
            .Where(t => t.UserId == userId && t.Type == TransactionType.Expense)
            .SumAsync(t => (decimal?)t.Amount) ?? 0;

        var expensesByCategory = await _context.Transactions
            .Where(t => t.UserId == userId && t.Type == TransactionType.Expense)
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

        // Évolution sur les 6 derniers mois — charger uniquement les données nécessaires
        var sixMonthsAgo = DateTime.UtcNow.AddMonths(-5);
        var startOfMonth = new DateTime(sixMonthsAgo.Year, sixMonthsAgo.Month, 1);

        var monthlyData = await _context.Transactions
            .Where(t => t.UserId == userId && t.Date >= startOfMonth)
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
