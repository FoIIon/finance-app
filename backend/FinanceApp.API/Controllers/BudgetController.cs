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
[Route("api/budgets")]
[Authorize]
public class BudgetController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IDashboardService _dashboardService;

    public BudgetController(AppDbContext context, IDashboardService dashboardService)
    {
        _context = context;
        _dashboardService = dashboardService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<bool> UserCanAccessDashboard(int dashboardId, int userId)
    {
        return await _context.DashboardMembers.AnyAsync(m => m.DashboardId == dashboardId && m.UserId == userId);
    }

    private static BudgetDto Map(Budget b) => new()
    {
        Id = b.Id,
        DashboardId = b.DashboardId,
        CategoryId = b.CategoryId,
        CategoryName = b.Category.Name,
        CategoryIcon = b.Category.Icon,
        CategoryColor = b.Category.Color,
        Amount = b.Amount,
        Period = b.Period.ToString(),
        CreatedAt = b.CreatedAt,
    };

    [HttpGet]
    public async Task<ActionResult<List<BudgetDto>>> GetAll([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var budgets = await _context.Budgets
            .Include(b => b.Category)
            .Where(b => b.DashboardId == dashboardId)
            .OrderBy(b => b.Category.Name)
            .ToListAsync();

        return Ok(budgets.Select(Map).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<BudgetDto>> Create(CreateBudgetDto dto)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dto.DashboardId, userId)) return Forbid();

        var category = await _context.Categories.FindAsync(dto.CategoryId);
        if (category == null) return BadRequest("Catégorie introuvable.");

        var period = dto.Period == "Yearly" ? BudgetPeriod.Yearly : BudgetPeriod.Monthly;

        var existing = await _context.Budgets
            .FirstOrDefaultAsync(b => b.DashboardId == dto.DashboardId && b.CategoryId == dto.CategoryId && b.Period == period);
        if (existing != null) return Conflict("Un budget existe déjà pour cette catégorie et cette période.");

        var budget = new Budget
        {
            DashboardId = dto.DashboardId,
            CategoryId = dto.CategoryId,
            Amount = dto.Amount,
            Period = period,
        };

        _context.Budgets.Add(budget);
        await _context.SaveChangesAsync();
        await _context.Entry(budget).Reference(b => b.Category).LoadAsync();

        return Ok(Map(budget));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<BudgetDto>> Update(int id, UpdateBudgetDto dto)
    {
        var userId = GetUserId();
        var budget = await _context.Budgets.Include(b => b.Category).FirstOrDefaultAsync(b => b.Id == id);
        if (budget == null) return NotFound();
        if (!await UserCanAccessDashboard(budget.DashboardId, userId)) return Forbid();

        if (dto.Amount.HasValue) budget.Amount = dto.Amount.Value;
        if (dto.Period != null) budget.Period = dto.Period == "Yearly" ? BudgetPeriod.Yearly : BudgetPeriod.Monthly;

        await _context.SaveChangesAsync();
        return Ok(Map(budget));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var budget = await _context.Budgets.FindAsync(id);
        if (budget == null) return NotFound();
        if (!await UserCanAccessDashboard(budget.DashboardId, userId)) return Forbid();

        _context.Budgets.Remove(budget);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Progression des budgets pour une période donnée (par défaut : ce mois-ci).
    /// </summary>
    [HttpGet("progress")]
    public async Task<ActionResult<List<BudgetProgressDto>>> GetProgress(
        [FromQuery] int dashboardId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var now = DateTime.UtcNow;
        var fromDate = from ?? new DateTime(now.Year, now.Month, 1);
        var toDate = to ?? fromDate.AddMonths(1).AddTicks(-1);

        var accountIds = await _dashboardService.GetDashboardAccountIds(dashboardId, userId);

        var budgets = await _context.Budgets
            .Include(b => b.Category)
            .Where(b => b.DashboardId == dashboardId && b.Period == BudgetPeriod.Monthly)
            .ToListAsync();

        // SQLite ne sait pas faire Sum(decimal) dans une projection GroupBy → aggrège en mémoire.
        // Exclure les catégories de transfert (épargne) — pas de vraies dépenses.
        var rawSpent = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId)
                     && t.Type == TransactionType.Expense
                     && !t.Category.IsTransfer
                     && t.Date >= fromDate && t.Date <= toDate)
            .Select(t => new { t.CategoryId, t.Amount })
            .ToListAsync();
        var spentByCategory = rawSpent
            .GroupBy(t => t.CategoryId)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));

        var result = budgets.Select(b =>
        {
            var spent = spentByCategory.GetValueOrDefault(b.CategoryId, 0);
            var remaining = b.Amount - spent;
            var pct = b.Amount > 0 ? Math.Round(spent / b.Amount * 100, 1) : 0;
            var status = pct >= 100 ? "exceeded" : pct >= 80 ? "warning" : "ok";
            return new BudgetProgressDto
            {
                BudgetId = b.Id,
                CategoryId = b.CategoryId,
                CategoryName = b.Category.Name,
                CategoryIcon = b.Category.Icon,
                CategoryColor = b.Category.Color,
                Budget = b.Amount,
                Spent = spent,
                Remaining = remaining,
                ProgressPct = pct,
                Status = status,
            };
        }).OrderByDescending(p => p.ProgressPct).ToList();

        return Ok(result);
    }
}
