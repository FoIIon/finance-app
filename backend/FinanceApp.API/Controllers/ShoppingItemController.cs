using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/shoppingitem")]
[Authorize]
public class ShoppingItemController : ApiControllerBase
{
    private readonly AppDbContext _context;

    public ShoppingItemController(AppDbContext context)
    {
        _context = context;
    }

    private async Task<bool> UserCanAccessDashboard(int dashboardId, int userId) =>
        await _context.DashboardMembers.AnyAsync(m => m.DashboardId == dashboardId && m.UserId == userId);

    private static ShoppingItemDto Map(ShoppingItem s) => new()
    {
        Id = s.Id,
        DashboardId = s.DashboardId,
        Label = s.Label,
        EstimatedCost = s.EstimatedCost,
        IsDone = s.IsDone,
        CreatedAt = s.CreatedAt,
    };

    [HttpGet]
    public async Task<ActionResult<List<ShoppingItemDto>>> GetAll([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var items = await _context.ShoppingItems
            .Where(s => s.DashboardId == dashboardId)
            .OrderBy(s => s.IsDone)
            .ThenByDescending(s => s.CreatedAt)
            .ToListAsync();

        return Ok(items.Select(Map).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<ShoppingItemDto>> Create(CreateShoppingItemDto dto)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dto.DashboardId, userId)) return Forbid();

        var item = new ShoppingItem
        {
            DashboardId = dto.DashboardId,
            Label = dto.Label,
            EstimatedCost = dto.EstimatedCost,
        };

        _context.ShoppingItems.Add(item);
        await _context.SaveChangesAsync();

        return Ok(Map(item));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ShoppingItemDto>> Update(int id, UpdateShoppingItemDto dto)
    {
        var userId = GetUserId();
        var item = await _context.ShoppingItems.FirstOrDefaultAsync(s => s.Id == id);
        if (item == null) return NotFound();
        if (!await UserCanAccessDashboard(item.DashboardId, userId)) return Forbid();

        if (dto.Label != null) item.Label = dto.Label;
        if (dto.EstimatedCost.HasValue) item.EstimatedCost = dto.EstimatedCost.Value;
        if (dto.IsDone.HasValue) item.IsDone = dto.IsDone.Value;

        await _context.SaveChangesAsync();
        return Ok(Map(item));
    }

    /// <summary>Bascule l'état « fait » d'un article.</summary>
    [HttpPut("{id}/toggle")]
    public async Task<ActionResult<ShoppingItemDto>> Toggle(int id)
    {
        var userId = GetUserId();
        var item = await _context.ShoppingItems.FirstOrDefaultAsync(s => s.Id == id);
        if (item == null) return NotFound();
        if (!await UserCanAccessDashboard(item.DashboardId, userId)) return Forbid();

        item.IsDone = !item.IsDone;
        await _context.SaveChangesAsync();
        return Ok(Map(item));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var item = await _context.ShoppingItems.FirstOrDefaultAsync(s => s.Id == id);
        if (item == null) return NotFound();
        if (!await UserCanAccessDashboard(item.DashboardId, userId)) return Forbid();

        _context.ShoppingItems.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
