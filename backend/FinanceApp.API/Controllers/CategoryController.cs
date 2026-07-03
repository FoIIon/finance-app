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
public class CategoryController : ControllerBase
{
    private readonly AppDbContext _context;

    public CategoryController(AppDbContext context)
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

    [HttpGet]
    public async Task<ActionResult<List<CategoryDto>>> GetAll()
    {
        var userId = GetUserId();
        var categories = await _context.Categories
            .Where(c => c.IsDefault || c.UserId == userId)
            .Select(c => new CategoryDto
            {
                Id = c.Id,
                Name = c.Name,
                Icon = c.Icon,
                Color = c.Color,
                IsDefault = c.IsDefault,
                IsFixed = c.IsFixed
            })
            .ToListAsync();

        return Ok(categories);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(CreateCategoryDto dto)
    {
        var userId = GetUserId();
        var category = new Category
        {
            Name = dto.Name,
            Icon = dto.Icon,
            Color = dto.Color,
            IsDefault = false,
            IsFixed = dto.IsFixed,
            UserId = userId
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), null, new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Icon = category.Icon,
            Color = category.Color,
            IsDefault = category.IsDefault,
            IsFixed = category.IsFixed
        });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CategoryDto>> Update(int id, UpdateCategoryDto dto)
    {
        var userId = GetUserId();
        var category = await _context.Categories.FirstOrDefaultAsync(
            c => c.Id == id && c.UserId == userId && !c.IsDefault);

        if (category == null) return NotFound();

        category.Name = dto.Name;
        category.Icon = dto.Icon;
        category.Color = dto.Color;
        category.IsFixed = dto.IsFixed;

        await _context.SaveChangesAsync();

        return Ok(new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Icon = category.Icon,
            Color = category.Color,
            IsDefault = category.IsDefault,
            IsFixed = category.IsFixed
        });
    }

    [HttpPut("{id}/fixed")]
    public async Task<ActionResult<CategoryDto>> SetFixed(int id, SetFixedDto dto)
    {
        var userId = GetUserId();
        var category = await _context.Categories.FirstOrDefaultAsync(
            c => c.Id == id && (c.IsDefault || c.UserId == userId));

        if (category == null) return NotFound();

        category.IsFixed = dto.IsFixed;
        await _context.SaveChangesAsync();

        return Ok(new CategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            Icon = category.Icon,
            Color = category.Color,
            IsDefault = category.IsDefault,
            IsFixed = category.IsFixed
        });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var category = await _context.Categories.FirstOrDefaultAsync(
            c => c.Id == id && c.UserId == userId && !c.IsDefault);

        if (category == null) return NotFound();

        var hasTransactions = await _context.Transactions.AnyAsync(t => t.CategoryId == id);
        if (hasTransactions)
            return BadRequest("Impossible de supprimer : des transactions utilisent cette catégorie.");

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
