using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/savings-goals")]
[Authorize]
public class SavingsGoalController : ApiControllerBase
{
    private readonly AppDbContext _context;
    private readonly IDashboardService _dashboardService;

    public SavingsGoalController(AppDbContext context, IDashboardService dashboardService)
    {
        _context = context;
        _dashboardService = dashboardService;
    }

    private async Task<bool> UserCanAccessDashboard(int dashboardId, int userId) =>
        await _context.DashboardMembers.AnyAsync(m => m.DashboardId == dashboardId && m.UserId == userId);

    private static SavingsGoalDto Map(SavingsGoal s, decimal? autoCurrent = null) => new()
    {
        Id = s.Id,
        DashboardId = s.DashboardId,
        Name = s.Name,
        Icon = s.Icon,
        TargetAmount = s.TargetAmount,
        CurrentAmount = autoCurrent ?? s.CurrentAmount,
        IsAutoCalculated = autoCurrent.HasValue,
        TargetDate = s.TargetDate,
        CategoryId = s.CategoryId,
        CategoryName = s.Category?.Name,
        CategoryIcon = s.Category?.Icon,
        CreatedAt = s.CreatedAt,
    };

    /// <summary>
    /// Calcule le cumulé d'épargne par objectif, à partir des transactions de la catégorie liée
    /// (sur les comptes du dashboard, depuis CreatedAt jusqu'à min(now, TargetDate)).
    /// Utilise la valeur absolue : un transfert vers épargne ou une dépense engagée comptent autant.
    /// </summary>
    private async Task<Dictionary<int, decimal>> ComputeAutoCurrentAmountsAsync(int dashboardId, IReadOnlyList<SavingsGoal> goals)
    {
        var withCategory = goals.Where(g => g.CategoryId.HasValue).ToList();
        if (withCategory.Count == 0) return new Dictionary<int, decimal>();

        var accountIds = await _context.DashboardAccounts
            .Where(da => da.DashboardId == dashboardId)
            .Select(da => da.AccountId)
            .ToListAsync();
        if (accountIds.Count == 0) return new Dictionary<int, decimal>();

        var categoryIds = withCategory.Select(g => g.CategoryId!.Value).Distinct().ToList();
        var minCreatedAt = withCategory.Min(g => g.CreatedAt);

        // SQLite ne supporte pas Sum(decimal) côté SQL → agrégation client
        var rawTx = await _context.Transactions
            .Where(t => accountIds.Contains(t.AccountId)
                     && categoryIds.Contains(t.CategoryId)
                     && t.Date >= minCreatedAt)
            .Select(t => new { t.CategoryId, t.Date, t.Amount })
            .ToListAsync();

        var now = DateTime.UtcNow;
        var result = new Dictionary<int, decimal>();
        foreach (var goal in withCategory)
        {
            var endDate = goal.TargetDate < now ? goal.TargetDate : now;
            result[goal.Id] = rawTx
                .Where(t => t.CategoryId == goal.CategoryId!.Value
                         && t.Date >= goal.CreatedAt
                         && t.Date <= endDate)
                .Sum(t => Math.Abs(t.Amount));
        }
        return result;
    }

    [HttpGet]
    public async Task<ActionResult<List<SavingsGoalDto>>> GetAll([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var goals = await _context.SavingsGoals
            .Include(s => s.Category)
            .Where(s => s.DashboardId == dashboardId)
            .OrderBy(s => s.TargetDate)
            .ToListAsync();

        var autoAmounts = await ComputeAutoCurrentAmountsAsync(dashboardId, goals);
        return Ok(goals.Select(g => Map(g, autoAmounts.TryGetValue(g.Id, out var v) ? v : (decimal?)null)).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<SavingsGoalDto>> Create(CreateSavingsGoalDto dto)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dto.DashboardId, userId)) return Forbid();

        var goal = new SavingsGoal
        {
            DashboardId = dto.DashboardId,
            Name = dto.Name,
            Icon = dto.Icon,
            TargetAmount = dto.TargetAmount,
            TargetDate = dto.TargetDate,
            CategoryId = dto.CategoryId,
        };

        _context.SavingsGoals.Add(goal);
        await _context.SaveChangesAsync();
        if (goal.CategoryId.HasValue)
            await _context.Entry(goal).Reference(g => g.Category).LoadAsync();

        return Ok(Map(goal));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<SavingsGoalDto>> Update(int id, UpdateSavingsGoalDto dto)
    {
        var userId = GetUserId();
        var goal = await _context.SavingsGoals.Include(s => s.Category).FirstOrDefaultAsync(s => s.Id == id);
        if (goal == null) return NotFound();
        if (!await UserCanAccessDashboard(goal.DashboardId, userId)) return Forbid();

        if (dto.Name != null) goal.Name = dto.Name;
        if (dto.Icon != null) goal.Icon = dto.Icon;
        if (dto.TargetAmount.HasValue) goal.TargetAmount = dto.TargetAmount.Value;
        if (dto.CurrentAmount.HasValue) goal.CurrentAmount = dto.CurrentAmount.Value;
        if (dto.TargetDate.HasValue) goal.TargetDate = dto.TargetDate.Value;
        if (dto.CategoryId.HasValue) goal.CategoryId = dto.CategoryId;

        await _context.SaveChangesAsync();
        return Ok(Map(goal));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var goal = await _context.SavingsGoals.FindAsync(id);
        if (goal == null) return NotFound();
        if (!await UserCanAccessDashboard(goal.DashboardId, userId)) return Forbid();

        _context.SavingsGoals.Remove(goal);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Progression de tous les objectifs avec calculs dérivés (mensualité, jours restants).</summary>
    [HttpGet("progress")]
    public async Task<ActionResult<List<SavingsGoalProgressDto>>> GetProgress([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var goals = await _context.SavingsGoals
            .Where(s => s.DashboardId == dashboardId)
            .OrderBy(s => s.TargetDate)
            .ToListAsync();

        var autoAmounts = await ComputeAutoCurrentAmountsAsync(dashboardId, goals);

        var now = DateTime.UtcNow;
        var result = goals.Select(g =>
        {
            var current = autoAmounts.TryGetValue(g.Id, out var v) ? v : g.CurrentAmount;
            var remaining = Math.Max(0, g.TargetAmount - current);
            var pct = g.TargetAmount > 0 ? Math.Round(current / g.TargetAmount * 100, 1) : 0;
            var daysRemaining = Math.Max(0, (int)(g.TargetDate - now).TotalDays);
            var monthsRemaining = Math.Max(1, (int)Math.Ceiling((g.TargetDate - now).TotalDays / 30.0));
            var monthly = monthsRemaining > 0 ? Math.Round(remaining / monthsRemaining, 2) : remaining;
            return new SavingsGoalProgressDto
            {
                Id = g.Id,
                Name = g.Name,
                Icon = g.Icon,
                TargetAmount = g.TargetAmount,
                CurrentAmount = current,
                IsAutoCalculated = autoAmounts.ContainsKey(g.Id),
                Remaining = remaining,
                ProgressPct = pct,
                TargetDate = g.TargetDate,
                DaysRemaining = daysRemaining,
                MonthsRemaining = monthsRemaining,
                RequiredMonthlySavings = monthly,
            };
        }).ToList();

        return Ok(result);
    }
}
