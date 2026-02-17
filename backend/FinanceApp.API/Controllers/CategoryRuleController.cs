using System.Security.Claims;
using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/categoryrules")]
[Authorize]
public class CategoryRuleController : ControllerBase
{
    private readonly AppDbContext _context;

    public CategoryRuleController(AppDbContext context)
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
    public async Task<ActionResult<List<CategoryRuleDto>>> GetAll()
    {
        var userId = GetUserId();
        var rules = await _context.CategoryRules
            .Include(cr => cr.Category)
            .Where(cr => cr.UserId == userId)
            .Select(cr => new CategoryRuleDto
            {
                Id = cr.Id,
                Keyword = cr.Keyword,
                CategoryId = cr.CategoryId,
                CategoryName = cr.Category.Name
            })
            .ToListAsync();

        return Ok(rules);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryRuleDto>> Create(CreateCategoryRuleDto dto)
    {
        var userId = GetUserId();

        // Vérifier que la catégorie existe
        var category = await _context.Categories
            .FirstOrDefaultAsync(c => c.Id == dto.CategoryId && (c.IsDefault || c.UserId == userId));
        if (category == null)
            return BadRequest("Catégorie invalide.");

        var rule = new CategoryRule
        {
            UserId = userId,
            Keyword = dto.Keyword,
            CategoryId = dto.CategoryId
        };

        _context.CategoryRules.Add(rule);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new CategoryRuleDto
        {
            Id = rule.Id,
            Keyword = rule.Keyword,
            CategoryId = rule.CategoryId,
            CategoryName = category.Name
        });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<CategoryRuleDto>> Update(int id, UpdateCategoryRuleDto dto)
    {
        var userId = GetUserId();
        var rule = await _context.CategoryRules
            .Include(cr => cr.Category)
            .FirstOrDefaultAsync(cr => cr.Id == id && cr.UserId == userId);

        if (rule == null) return NotFound();

        if (dto.Keyword != null)
            rule.Keyword = dto.Keyword;

        if (dto.CategoryId.HasValue)
        {
            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == dto.CategoryId.Value && (c.IsDefault || c.UserId == userId));
            if (category == null)
                return BadRequest("Catégorie invalide.");
            rule.CategoryId = dto.CategoryId.Value;
        }

        await _context.SaveChangesAsync();

        // Recharger la catégorie pour le DTO
        await _context.Entry(rule).Reference(r => r.Category).LoadAsync();

        return Ok(new CategoryRuleDto
        {
            Id = rule.Id,
            Keyword = rule.Keyword,
            CategoryId = rule.CategoryId,
            CategoryName = rule.Category.Name
        });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var rule = await _context.CategoryRules
            .FirstOrDefaultAsync(cr => cr.Id == id && cr.UserId == userId);

        if (rule == null) return NotFound();

        _context.CategoryRules.Remove(rule);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
