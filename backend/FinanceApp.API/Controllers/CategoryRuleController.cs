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

    [HttpPost("seed-defaults")]
    public async Task<ActionResult<object>> SeedDefaults()
    {
        var userId = GetUserId();

        var defaults = new List<(string Keyword, int CategoryId)>
        {
            // Alimentation (1)
            ("COLRUYT", 1), ("CARREFOUR", 1), ("DELHAIZE", 1), ("LIDL", 1), ("ALDI", 1),
            ("SPAR", 1), ("CORA", 1), ("PROXY DELHAIZE", 1), ("ALBERT HEIJN", 1),
            ("INTERMARCHE", 1), ("MAKRO", 1), ("PICARD", 1),

            // Transport (2)
            ("SNCB", 2), ("NMBS", 2), ("STIB", 2), ("DE LIJN", 2),
            ("SHELL", 2), ("TOTAL ENERGIES", 2), ("Q8", 2), ("ESSO", 2), ("TEXACO", 2),
            ("IONITY", 2), ("BLUPOINT", 2),

            // Logement (3)
            ("LOYER", 3), ("SYNDIC", 3), ("HYPOTHECAIRE", 3), ("COPROPRIETE", 3),

            // Loisirs (4)
            ("KINEPOLIS", 4), ("UGC", 4), ("PATHÉ", 4), ("THEATRE", 4), ("CONCERT", 4),

            // Santé (5)
            ("PHARMACIE", 5), ("APOTHEEK", 5), ("MUTUALITE", 5), ("MUTUELLE", 5),
            ("KINESITHERAPEUTE", 5), ("DENTISTE", 5), ("OPTICIEN", 5),
            ("LABORATOIRE", 5), ("MEDISPRING", 5),

            // Shopping (7)
            ("AMAZON", 7), ("BOL.COM", 7), ("ZALANDO", 7), ("IKEA", 7), ("ACTION", 7),
            ("PRIMARK", 7), ("MEDIAMARKT", 7), ("ZARA", 7), ("H&M", 7), ("FNAC", 7),

            // Salaire (8)
            ("SALAIRE", 8), ("LOON", 8),

            // Restaurants (11)
            ("MCDONALD", 11), ("BURGER KING", 11), ("QUICK RESTAURANT", 11),
            ("DELIVEROO", 11), ("UBER EATS", 11), ("TAKEAWAY", 11),
            ("STARBUCKS", 11), ("DOMINOS", 11), ("LE PAIN QUOTIDIEN", 11),

            // Abonnements (12)
            ("NETFLIX", 12), ("SPOTIFY", 12), ("DISNEY+", 12), ("DEEZER", 12),
            ("YOUTUBE PREMIUM", 12), ("APPLE.COM/BILL", 12), ("MICROSOFT 365", 12),
            ("ADOBE", 12), ("PROXIMUS", 12), ("TELENET", 12), ("VOO", 12),
            ("ORANGE BELGIUM", 12), ("BASE COMPANY", 12), ("AMAZON PRIME", 12),

            // Assurances (13)
            ("AXA", 13), ("AG INSURANCE", 13), ("ETHIAS", 13), ("FEDERALE ASSURANCE", 13),
            ("BALOISE", 13), ("ALLIANZ", 13), ("DVV", 13), ("P&V ASSURANCES", 13),
            ("BELFIUS INSURANCE", 13),

            // Énergie (14)
            ("ENGIE", 14), ("LUMINUS", 14), ("FLUVIUS", 14), ("ORES", 14), ("ELIA", 14),

            // Enfants (15)
            ("CRECHE", 15), ("CRÈCHE", 15), ("GARDERIE", 15),
            ("ACCUEIL EXTRASCOLAIRE", 15), ("CENTRE CULTUREL", 15),

            // Épargne (16)
            ("EPARGNE", 16), ("SPAARREKENING", 16), ("ARGENTA", 16),
        };

        var existingKeywords = await _context.CategoryRules
            .Where(cr => cr.UserId == userId)
            .Select(cr => cr.Keyword.ToLower())
            .ToListAsync();

        var newRules = defaults
            .Where(d => !existingKeywords.Contains(d.Keyword.ToLower()))
            .Select(d => new CategoryRule
            {
                UserId = userId,
                Keyword = d.Keyword,
                CategoryId = d.CategoryId
            })
            .ToList();

        _context.CategoryRules.AddRange(newRules);
        await _context.SaveChangesAsync();

        return Ok(new { created = newRules.Count, skipped = defaults.Count - newRules.Count });
    }
}
