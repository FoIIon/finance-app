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
                CategoryName = cr.Category.Name,
                MarkAsFixed = cr.MarkAsFixed,
                RouteToPerso = cr.RouteToPerso
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
            CategoryId = dto.CategoryId,
            MarkAsFixed = dto.MarkAsFixed,
            RouteToPerso = dto.RouteToPerso
        };

        _context.CategoryRules.Add(rule);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new CategoryRuleDto
        {
            Id = rule.Id,
            Keyword = rule.Keyword,
            CategoryId = rule.CategoryId,
            CategoryName = category.Name,
            MarkAsFixed = rule.MarkAsFixed,
            RouteToPerso = rule.RouteToPerso
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

        if (dto.MarkAsFixed.HasValue)
            rule.MarkAsFixed = dto.MarkAsFixed.Value;

        if (dto.RouteToPerso.HasValue)
            rule.RouteToPerso = dto.RouteToPerso.Value;

        await _context.SaveChangesAsync();

        // Recharger la catégorie pour le DTO
        await _context.Entry(rule).Reference(r => r.Category).LoadAsync();

        return Ok(new CategoryRuleDto
        {
            Id = rule.Id,
            Keyword = rule.Keyword,
            CategoryId = rule.CategoryId,
            CategoryName = rule.Category.Name,
            MarkAsFixed = rule.MarkAsFixed,
            RouteToPerso = rule.RouteToPerso
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

        var defaults = new List<(string Keyword, int CategoryId, bool MarkAsFixed)>
        {
            // Alimentation (1)
            ("COLRUYT", 1, false), ("CARREFOUR", 1, false), ("DELHAIZE", 1, false), ("LIDL", 1, false), ("ALDI", 1, false),
            ("SPAR", 1, false), ("CORA", 1, false), ("PROXY DELHAIZE", 1, false), ("ALBERT HEIJN", 1, false),
            ("INTERMARCHE", 1, false), ("MAKRO", 1, false), ("PICARD", 1, false),

            // Transport (2)
            ("SNCB", 2, false), ("NMBS", 2, false), ("STIB", 2, false), ("DE LIJN", 2, false),
            ("SHELL", 2, false), ("TOTAL ENERGIES", 2, false), ("Q8", 2, false), ("ESSO", 2, false), ("TEXACO", 2, false),
            ("IONITY", 2, false), ("BLUPOINT", 2, false),

            // Logement (3) — récurrents fixes
            ("LOYER", 3, true), ("SYNDIC", 3, true), ("HYPOTHECAIRE", 3, true), ("COPROPRIETE", 3, true),

            // Loisirs (4)
            ("KINEPOLIS", 4, false), ("UGC", 4, false), ("PATHÉ", 4, false), ("THEATRE", 4, false), ("CONCERT", 4, false),

            // Santé (5) — cotisations mutuelle fixes, le reste variable
            ("PHARMACIE", 5, false), ("APOTHEEK", 5, false), ("MUTUALITE", 5, true), ("MUTUELLE", 5, true),
            ("KINESITHERAPEUTE", 5, false), ("DENTISTE", 5, false), ("OPTICIEN", 5, false),
            ("LABORATOIRE", 5, false), ("MEDISPRING", 5, false),

            // Shopping (7)
            ("AMAZON", 7, false), ("BOL.COM", 7, false), ("ZALANDO", 7, false), ("IKEA", 7, false), ("ACTION", 7, false),
            ("PRIMARK", 7, false), ("MEDIAMARKT", 7, false), ("ZARA", 7, false), ("H&M", 7, false), ("FNAC", 7, false),

            // Salaire (8)
            ("SALAIRE", 8, false), ("LOON", 8, false),

            // Restaurants (11)
            ("MCDONALD", 11, false), ("BURGER KING", 11, false), ("QUICK RESTAURANT", 11, false),
            ("DELIVEROO", 11, false), ("UBER EATS", 11, false), ("TAKEAWAY", 11, false),
            ("STARBUCKS", 11, false), ("DOMINOS", 11, false), ("LE PAIN QUOTIDIEN", 11, false),

            // Abonnements (12) — fixes par nature
            ("NETFLIX", 12, true), ("SPOTIFY", 12, true), ("DISNEY+", 12, true), ("DEEZER", 12, true),
            ("YOUTUBE PREMIUM", 12, true), ("APPLE.COM/BILL", 12, true), ("MICROSOFT 365", 12, true),
            ("ADOBE", 12, true), ("PROXIMUS", 12, true), ("TELENET", 12, true), ("VOO", 12, true),
            ("ORANGE BELGIUM", 12, true), ("BASE COMPANY", 12, true), ("AMAZON PRIME", 12, true),

            // Assurances (13) — primes fixes
            ("AXA", 13, true), ("AG INSURANCE", 13, true), ("ETHIAS", 13, true), ("FEDERALE ASSURANCE", 13, true),
            ("BALOISE", 13, true), ("ALLIANZ", 13, true), ("DVV", 13, true), ("P&V ASSURANCES", 13, true),
            ("BELFIUS INSURANCE", 13, true),

            // Énergie (14) — acomptes fixes (les régularisations créditrices suivent la même règle)
            ("ENGIE", 14, true), ("LUMINUS", 14, true), ("FLUVIUS", 14, true), ("ORES", 14, true), ("ELIA", 14, true),

            // Enfants (15) — crèche fixe, extrascolaire variable
            ("CRECHE", 15, true), ("CRÈCHE", 15, true), ("GARDERIE", 15, false),
            ("ACCUEIL EXTRASCOLAIRE", 15, false), ("CENTRE CULTUREL", 15, false),

            // Épargne (16)
            ("EPARGNE", 16, false), ("SPAARREKENING", 16, false), ("ARGENTA", 16, false),
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
                CategoryId = d.CategoryId,
                MarkAsFixed = d.MarkAsFixed
            })
            .ToList();

        _context.CategoryRules.AddRange(newRules);
        await _context.SaveChangesAsync();

        return Ok(new { created = newRules.Count, skipped = defaults.Count - newRules.Count });
    }
}
