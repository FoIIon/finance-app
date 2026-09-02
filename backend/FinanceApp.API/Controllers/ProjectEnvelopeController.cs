using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/projectenvelope")]
[Authorize]
public class ProjectEnvelopeController : ApiControllerBase
{
    private readonly AppDbContext _context;
    private readonly IDashboardService _dashboardService;

    public ProjectEnvelopeController(AppDbContext context, IDashboardService dashboardService)
    {
        _context = context;
        _dashboardService = dashboardService;
    }

    private async Task<bool> UserCanAccessDashboard(int dashboardId, int userId) =>
        await _context.DashboardMembers.AnyAsync(m => m.DashboardId == dashboardId && m.UserId == userId);

    private static ProjectEnvelopeDto Map(ProjectEnvelope e) => new()
    {
        Id = e.Id,
        DashboardId = e.DashboardId,
        Name = e.Name,
        Icon = e.Icon,
        TargetBudget = e.TargetBudget,
        FundingNote = e.FundingNote,
        IsArchived = e.IsArchived,
        CreatedAt = e.CreatedAt,
    };

    /// <summary>
    /// Liste des enveloppes du dashboard avec progression : engagé (dépenses − remboursements rattachés),
    /// restant vs cible, compteur de transactions. Actives d'abord, puis archivées.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ProjectEnvelopeProgressDto>>> GetAll([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var envelopes = await _context.ProjectEnvelopes
            .Where(e => e.DashboardId == dashboardId)
            .OrderBy(e => e.IsArchived)
            .ThenByDescending(e => e.CreatedAt)
            .ToListAsync();

        // SQLite ne sait pas Sum(decimal) en SQL → agrégation client (même pattern que le reste de l'app)
        var envelopeIds = envelopes.Select(e => e.Id).ToList();
        var rawTx = await _context.Transactions
            .Where(t => t.ProjectEnvelopeId != null && envelopeIds.Contains(t.ProjectEnvelopeId.Value))
            .Select(t => new { EnvelopeId = t.ProjectEnvelopeId!.Value, t.Type, t.Amount })
            .ToListAsync();

        var byEnvelope = rawTx
            .GroupBy(t => t.EnvelopeId)
            .ToDictionary(g => g.Key, g => new
            {
                Spent = g.Where(t => t.Type == TransactionType.Expense).Sum(t => t.Amount)
                      - g.Where(t => t.Type == TransactionType.Income).Sum(t => t.Amount),
                Count = g.Count(),
            });

        var result = envelopes.Select(e =>
        {
            var stats = byEnvelope.GetValueOrDefault(e.Id);
            var spent = stats?.Spent ?? 0;
            return new ProjectEnvelopeProgressDto
            {
                Id = e.Id,
                DashboardId = e.DashboardId,
                Name = e.Name,
                Icon = e.Icon,
                TargetBudget = e.TargetBudget,
                FundingNote = e.FundingNote,
                IsArchived = e.IsArchived,
                CreatedAt = e.CreatedAt,
                Spent = spent,
                Remaining = e.TargetBudget.HasValue ? e.TargetBudget.Value - spent : (decimal?)null,
                TransactionCount = stats?.Count ?? 0,
            };
        }).ToList();

        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ProjectEnvelopeDto>> Create(CreateProjectEnvelopeDto dto)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dto.DashboardId, userId)) return Forbid();

        var envelope = new ProjectEnvelope
        {
            DashboardId = dto.DashboardId,
            Name = dto.Name,
            Icon = string.IsNullOrWhiteSpace(dto.Icon) ? "🏖️" : dto.Icon,
            TargetBudget = dto.TargetBudget,
            FundingNote = dto.FundingNote,
        };

        _context.ProjectEnvelopes.Add(envelope);
        await _context.SaveChangesAsync();

        return Ok(Map(envelope));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ProjectEnvelopeDto>> Update(int id, UpdateProjectEnvelopeDto dto)
    {
        var userId = GetUserId();
        var envelope = await _context.ProjectEnvelopes.FirstOrDefaultAsync(e => e.Id == id);
        if (envelope == null) return NotFound();
        if (!await UserCanAccessDashboard(envelope.DashboardId, userId)) return Forbid();

        if (dto.Name != null) envelope.Name = dto.Name;
        if (dto.Icon != null) envelope.Icon = string.IsNullOrWhiteSpace(dto.Icon) ? envelope.Icon : dto.Icon;
        if (dto.TargetBudget.HasValue) envelope.TargetBudget = dto.TargetBudget.Value;
        if (dto.FundingNote != null) envelope.FundingNote = dto.FundingNote;
        if (dto.IsArchived.HasValue) envelope.IsArchived = dto.IsArchived.Value;

        await _context.SaveChangesAsync();
        return Ok(Map(envelope));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var envelope = await _context.ProjectEnvelopes.FirstOrDefaultAsync(e => e.Id == id);
        if (envelope == null) return NotFound();
        if (!await UserCanAccessDashboard(envelope.DashboardId, userId)) return Forbid();

        // Les transactions rattachées sont détachées automatiquement (FK SetNull)
        _context.ProjectEnvelopes.Remove(envelope);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Transactions rattachées à une enveloppe, triées par date décroissante.</summary>
    [HttpGet("{id}/transactions")]
    public async Task<ActionResult<List<TransactionDto>>> GetTransactions(int id)
    {
        var userId = GetUserId();
        var envelope = await _context.ProjectEnvelopes.FirstOrDefaultAsync(e => e.Id == id);
        if (envelope == null) return NotFound();
        if (!await UserCanAccessDashboard(envelope.DashboardId, userId)) return Forbid();

        var txns = await _context.Transactions
            .Include(t => t.Category)
            .Include(t => t.Account)
            .Include(t => t.BankAccount).ThenInclude(ba => ba!.BankConnection)
            .Include(t => t.ProjectEnvelope)
            .Where(t => t.ProjectEnvelopeId == id)
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.Id)
            .Select(t => new TransactionDto
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
                IsExceptional = t.IsExceptional,
                BankAccountName = t.BankAccount != null ? t.BankAccount.AccountName : null,
                BankInstitutionName = t.BankAccount != null && t.BankAccount.BankConnection != null ? t.BankAccount.BankConnection.InstitutionName : null,
                ProjectEnvelopeId = t.ProjectEnvelopeId,
                ProjectEnvelopeName = t.ProjectEnvelope != null ? t.ProjectEnvelope.Name : null,
            })
            .ToListAsync();

        return Ok(txns);
    }
}
