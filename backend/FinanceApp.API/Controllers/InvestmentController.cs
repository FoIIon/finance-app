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
[Route("api/investment")]
[Authorize]
public class InvestmentController : ControllerBase
{
    private readonly AppDbContext _context;

    public InvestmentController(AppDbContext context)
    {
        _context = context;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<bool> UserCanAccessDashboard(int dashboardId, int userId) =>
        await _context.DashboardMembers.AnyAsync(m => m.DashboardId == dashboardId && m.UserId == userId);

    /// <summary>Projette une ligne et sa dernière valorisation vers le DTO enrichi.</summary>
    private static InvestmentDto Map(Investment i, InvestmentValuation? latest, DateTime now)
    {
        var marketValue = latest?.MarketValue;
        var (gainAmount, gainPercent) = InvestmentCalculator.ComputeGain(i.CostBasis, marketValue);

        return new InvestmentDto
        {
            Id = i.Id,
            DashboardId = i.DashboardId,
            Name = i.Name,
            Holder = i.Holder,
            Kind = i.Kind,
            Isin = i.Isin,
            MetalCode = i.MetalCode,
            Quantity = i.Quantity,
            Unit = i.Unit,
            CostBasis = i.CostBasis,
            FirstPurchaseDate = i.FirstPurchaseDate,
            Source = i.Source,
            IsArchived = i.IsArchived,
            CreatedAt = i.CreatedAt,
            UnitCost = InvestmentCalculator.ComputeUnitCost(i.Kind, i.CostBasis, i.Quantity),
            MarketValue = marketValue,
            ValuationAsOf = latest?.AsOf,
            IsStale = latest != null && InvestmentCalculator.IsStale(latest.Source, latest.AsOf, now),
            GainAmount = gainAmount,
            GainPercent = gainPercent,
            AnnualizedReturn = latest == null
                ? null
                : InvestmentCalculator.ComputeCagr(i.CostBasis, marketValue, i.FirstPurchaseDate, latest.AsOf),
        };
    }

    [HttpGet]
    public async Task<ActionResult<List<InvestmentDto>>> GetAll([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var investments = await _context.Investments
            .Where(i => i.DashboardId == dashboardId)
            .OrderBy(i => i.IsArchived)
            .ThenBy(i => i.Holder)
            .ThenBy(i => i.Name)
            .ToListAsync();

        var ids = investments.Select(i => i.Id).ToList();

        // Agrégation côté client : SQLite ne sait pas grouper sur decimal en SQL.
        var valuations = await _context.InvestmentValuations
            .Where(v => ids.Contains(v.InvestmentId))
            .ToListAsync();

        var latestByInvestment = valuations
            .GroupBy(v => v.InvestmentId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.AsOf).First());

        var now = DateTime.UtcNow;
        var result = investments
            .Select(i => Map(i, latestByInvestment.GetValueOrDefault(i.Id), now))
            .ToList();

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<InvestmentDto>> Create(CreateInvestmentDto dto)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dto.DashboardId, userId)) return Forbid();

        // Un contrat d'assurance-vie n'a pas de quantité naturelle : 1 par convention.
        var quantity = dto.Kind == InvestmentKind.InsuranceContract ? 1m : dto.Quantity;
        var unit = dto.Kind == InvestmentKind.InsuranceContract ? InvestmentUnit.Contract : dto.Unit;

        var investment = new Investment
        {
            DashboardId = dto.DashboardId,
            Name = dto.Name,
            Holder = dto.Holder,
            Kind = dto.Kind,
            Isin = dto.Isin,
            MetalCode = dto.MetalCode,
            Quantity = quantity,
            Unit = unit,
            CostBasis = dto.CostBasis,
            FirstPurchaseDate = dto.FirstPurchaseDate,
            Source = InvestmentSource.Manual,
        };

        _context.Investments.Add(investment);
        await _context.SaveChangesAsync();

        return Ok(Map(investment, null, DateTime.UtcNow));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<InvestmentDto>> Update(int id, UpdateInvestmentDto dto)
    {
        var userId = GetUserId();
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();
        if (!await UserCanAccessDashboard(investment.DashboardId, userId)) return Forbid();

        if (dto.Name != null) investment.Name = dto.Name;
        if (dto.Holder != null) investment.Holder = dto.Holder;
        if (dto.Isin != null) investment.Isin = dto.Isin;
        if (dto.MetalCode != null) investment.MetalCode = dto.MetalCode;
        if (dto.Quantity.HasValue && investment.Kind != InvestmentKind.InsuranceContract)
            investment.Quantity = dto.Quantity.Value;
        if (dto.CostBasis.HasValue) investment.CostBasis = dto.CostBasis.Value;
        if (dto.FirstPurchaseDate.HasValue) investment.FirstPurchaseDate = dto.FirstPurchaseDate.Value;
        if (dto.IsArchived.HasValue) investment.IsArchived = dto.IsArchived.Value;

        await _context.SaveChangesAsync();

        var latest = await _context.InvestmentValuations
            .Where(v => v.InvestmentId == id)
            .OrderByDescending(v => v.AsOf)
            .FirstOrDefaultAsync();

        return Ok(Map(investment, latest, DateTime.UtcNow));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();
        if (!await UserCanAccessDashboard(investment.DashboardId, userId)) return Forbid();

        // Les valorisations partent en cascade (configuré dans OnModelCreating).
        _context.Investments.Remove(investment);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Enregistre une valeur datée. Une valorisation existante à la même date est remplacée
    /// (contrainte unique InvestmentId + AsOf), les autres dates ne sont jamais touchées :
    /// l'historique reste intact et la courbe ne se réécrit pas rétroactivement.
    /// </summary>
    [HttpPost("{id}/valuation")]
    public async Task<ActionResult<InvestmentDto>> AddValuation(int id, CreateValuationDto dto)
    {
        var userId = GetUserId();
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();
        if (!await UserCanAccessDashboard(investment.DashboardId, userId)) return Forbid();

        // Une date future rendrait cette valorisation la plus récente pour toujours, la
        // ligne resterait figée sur cette valeur, IsStale ne se déclencherait jamais (la date
        // n'est jamais dépassée) et ComputeCagr prendrait cet horizon comme référence. Seule
        // une suppression complète de la ligne permettrait d'en sortir, la corriger ne suffit
        // pas. On la rejette donc à l'entrée, avec une journée de marge par rapport à UtcNow
        // pour ne pas pénaliser un utilisateur dont le fuseau horaire local est en avance sur
        // l'UTC (le soir en Europe, la date locale a déjà changé alors que l'UTC est encore
        // la veille).
        if (dto.AsOf > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            return BadRequest("La date de valorisation ne peut pas être dans le futur.");

        // AsOf porte la date de la valeur, pas la date de saisie. Le DTO est typé DateOnly :
        // toute ambiguïté de fuseau horaire (offset dans la chaîne reçue) est rejetée par le
        // binding avant d'arriver ici, la contrainte d'unicité ne peut plus être contournée
        // par un décalage qui ferait glisser la date d'un jour.
        var asOf = dto.AsOf.ToDateTime(TimeOnly.MinValue);

        var existing = await _context.InvestmentValuations
            .FirstOrDefaultAsync(v => v.InvestmentId == id && v.AsOf == asOf);

        if (existing != null)
        {
            existing.MarketValue = dto.MarketValue;
            existing.UnitPrice = dto.UnitPrice;
            existing.Source = ValuationSource.Manual;
        }
        else
        {
            _context.InvestmentValuations.Add(new InvestmentValuation
            {
                InvestmentId = id,
                AsOf = asOf,
                MarketValue = dto.MarketValue,
                UnitPrice = dto.UnitPrice,
                Source = ValuationSource.Manual,
            });
        }

        await _context.SaveChangesAsync();

        var latest = await _context.InvestmentValuations
            .Where(v => v.InvestmentId == id)
            .OrderByDescending(v => v.AsOf)
            .FirstAsync();

        return Ok(Map(investment, latest, DateTime.UtcNow));
    }

    /// <summary>
    /// Courbe agrégée du patrimoine investi du dashboard, un point par date de valorisation.
    /// LinesTotal (lignes non archivées) est constant sur tous les points : il permet au
    /// frontend d'annoncer une courbe partielle (« X lignes sur Y valorisées »).
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<List<InvestmentHistoryPointDto>>> GetHistory([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var investments = await _context.Investments
            .Where(i => i.DashboardId == dashboardId)
            .ToListAsync();

        var ids = investments.Select(i => i.Id).ToList();

        // Agrégation côté client : SQLite ne sait pas grouper sur decimal en SQL.
        var valuations = await _context.InvestmentValuations
            .Where(v => ids.Contains(v.InvestmentId))
            .ToListAsync();

        var valuationsByInvestment = valuations
            .GroupBy(v => v.InvestmentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<(DateTime, decimal)>)g
                .Select(v => (v.AsOf, v.MarketValue))
                .ToList());

        var lines = investments
            .Select(i => new PortfolioLine(
                i.CostBasis,
                i.IsArchived,
                valuationsByInvestment.GetValueOrDefault(i.Id) ?? Array.Empty<(DateTime, decimal)>()))
            .ToList();

        var history = InvestmentCalculator.ComputePortfolioHistory(lines);
        var linesTotal = investments.Count(i => !i.IsArchived);

        var result = history
            .Select(p => new InvestmentHistoryPointDto
            {
                AsOf = p.AsOf,
                Value = p.Value,
                Invested = p.Invested,
                LinesIncluded = p.LinesIncluded,
                LinesTotal = linesTotal,
            })
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Toutes les valorisations des lignes non archivées du dashboard, par date croissante.
    /// Sert aux sparklines du tableau : une requête au lieu d'une par ligne.
    /// </summary>
    [HttpGet("valuations")]
    public async Task<ActionResult<List<InvestmentValuationDto>>> GetAllValuations([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var valuations = await _context.InvestmentValuations
            .Where(v => v.Investment.DashboardId == dashboardId && !v.Investment.IsArchived)
            .OrderBy(v => v.AsOf)
            .ThenBy(v => v.InvestmentId)
            .Select(v => new InvestmentValuationDto
            {
                Id = v.Id,
                InvestmentId = v.InvestmentId,
                AsOf = v.AsOf,
                UnitPrice = v.UnitPrice,
                MarketValue = v.MarketValue,
                Source = v.Source,
            })
            .ToListAsync();

        return Ok(valuations);
    }

    /// <summary>Historique des valorisations d'une ligne, de la plus récente à la plus ancienne.</summary>
    [HttpGet("{id}/valuations")]
    public async Task<ActionResult<List<InvestmentValuationDto>>> GetValuations(int id)
    {
        var userId = GetUserId();
        var investment = await _context.Investments.FirstOrDefaultAsync(i => i.Id == id);
        if (investment == null) return NotFound();
        if (!await UserCanAccessDashboard(investment.DashboardId, userId)) return Forbid();

        var valuations = await _context.InvestmentValuations
            .Where(v => v.InvestmentId == id)
            .OrderByDescending(v => v.AsOf)
            .Select(v => new InvestmentValuationDto
            {
                Id = v.Id,
                InvestmentId = v.InvestmentId,
                AsOf = v.AsOf,
                UnitPrice = v.UnitPrice,
                MarketValue = v.MarketValue,
                Source = v.Source,
            })
            .ToListAsync();

        return Ok(valuations);
    }
}
