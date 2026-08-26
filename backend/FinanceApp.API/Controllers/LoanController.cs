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
[Route("api/loans")]
[Authorize]
public class LoanController : ControllerBase
{
    private readonly AppDbContext _context;

    public LoanController(AppDbContext context)
    {
        _context = context;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<bool> UserCanAccessDashboard(int dashboardId, int userId) =>
        await _context.DashboardMembers.AnyAsync(m => m.DashboardId == dashboardId && m.UserId == userId);

    // Le client envoie toujours l'objet complet : sans ces deux conversions, un champ vidé
    // à l'écran serait relu comme « non fourni » et l'ancienne valeur reviendrait après le refetch.
    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static decimal? Blank(decimal? value) => value is null or 0m ? null : value;

    private static LoanDto Map(Loan l, DateTime asOf)
    {
        var summary = LoanCalculator.Summarize(l, asOf);
        var repaid = l.InitialPrincipal is > 0
            ? Math.Round((l.InitialPrincipal.Value - summary.RemainingPrincipal) / l.InitialPrincipal.Value * 100, 1)
            : (decimal?)null;

        return new LoanDto
        {
            Id = l.Id,
            DashboardId = l.DashboardId,
            Name = l.Name,
            Holder = l.Holder,
            Kind = l.Kind,
            Lender = l.Lender,
            Reference = l.Reference,
            InitialPrincipal = l.InitialPrincipal,
            AnnualRatePercent = l.AnnualRatePercent,
            MonthlyPayment = l.MonthlyPayment,
            AnchorDate = l.AnchorDate,
            AnchorPrincipal = l.AnchorPrincipal,
            DebitIban = l.DebitIban,
            IsArchived = l.IsArchived,
            RemainingPrincipal = summary.RemainingPrincipal,
            RemainingInstallments = summary.RemainingInstallments,
            FinalDueDate = summary.FinalDueDate,
            RemainingInterest = summary.RemainingInterest,
            RemainingPayments = summary.RemainingPayments,
            NextDueDate = summary.NextDueDate,
            NextPayment = summary.NextPayment,
            RepaidPercent = repaid,
        };
    }

    /// <summary>
    /// Un emprunt dont la mensualité ne couvre pas l'intérêt ne s'amortit jamais. On le refuse
    /// à l'écriture, sinon le calculateur lèverait à chaque lecture de la liste.
    /// </summary>
    private ActionResult? ValidateAmortization(Loan loan)
    {
        try
        {
            LoanCalculator.Summarize(loan, DateTime.UtcNow);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<LoanDto>>> GetAll([FromQuery] int dashboardId, [FromQuery] bool includeArchived = false)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var loans = await _context.Loans
            .Where(l => l.DashboardId == dashboardId && (includeArchived || !l.IsArchived))
            .OrderBy(l => l.Name)
            .ToListAsync();

        var asOf = DateTime.UtcNow;
        return Ok(loans.Select(l => Map(l, asOf)).ToList());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<LoanDto>> GetOne(int id)
    {
        var userId = GetUserId();
        var loan = await _context.Loans.FindAsync(id);
        if (loan == null) return NotFound();
        if (!await UserCanAccessDashboard(loan.DashboardId, userId)) return Forbid();

        return Ok(Map(loan, DateTime.UtcNow));
    }

    /// <summary>Tableau d'amortissement à venir. Sans months, tout jusqu'à extinction.</summary>
    [HttpGet("{id}/schedule")]
    public async Task<ActionResult<List<LoanInstallmentDto>>> GetSchedule(int id, [FromQuery] int? months = null)
    {
        var userId = GetUserId();
        var loan = await _context.Loans.FindAsync(id);
        if (loan == null) return NotFound();
        if (!await UserCanAccessDashboard(loan.DashboardId, userId)) return Forbid();

        var schedule = LoanCalculator.RemainingSchedule(loan, DateTime.UtcNow);
        if (months.HasValue) schedule = schedule.Take(Math.Max(0, months.Value)).ToList();

        return Ok(schedule.Select(i => new LoanInstallmentDto
        {
            DueDate = i.DueDate,
            Payment = i.Payment,
            Interest = i.Interest,
            Principal = i.Principal,
            RemainingPrincipal = i.RemainingPrincipal,
        }).ToList());
    }

    /// <summary>Le passif consolidé du dashboard, à soustraire du patrimoine.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<DebtSummaryDto>> GetSummary([FromQuery] int dashboardId)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dashboardId, userId)) return Forbid();

        var loans = await _context.Loans
            .Where(l => l.DashboardId == dashboardId && !l.IsArchived)
            .ToListAsync();

        var asOf = DateTime.UtcNow;
        var summaries = loans.Select(l => LoanCalculator.Summarize(l, asOf)).ToList();
        var active = summaries.Where(s => s.RemainingInstallments > 0).ToList();

        return Ok(new DebtSummaryDto
        {
            TotalRemainingPrincipal = summaries.Sum(s => s.RemainingPrincipal),
            TotalMonthlyPayment = active.Sum(s => s.NextPayment ?? 0m),
            TotalRemainingInterest = summaries.Sum(s => s.RemainingInterest),
            DebtFreeDate = active.Count > 0 ? active.Max(s => s.FinalDueDate) : null,
            LoanCount = loans.Count,
        });
    }

    [HttpPost]
    public async Task<ActionResult<LoanDto>> Create(CreateLoanDto dto)
    {
        var userId = GetUserId();
        if (!await UserCanAccessDashboard(dto.DashboardId, userId)) return Forbid();

        var loan = new Loan
        {
            DashboardId = dto.DashboardId,
            Name = dto.Name,
            Holder = dto.Holder,
            Kind = dto.Kind,
            Lender = Blank(dto.Lender),
            Reference = Blank(dto.Reference),
            InitialPrincipal = Blank(dto.InitialPrincipal),
            AnnualRatePercent = dto.AnnualRatePercent,
            MonthlyPayment = dto.MonthlyPayment,
            AnchorDate = dto.AnchorDate.Date,
            AnchorPrincipal = dto.AnchorPrincipal,
            DebitIban = Blank(dto.DebitIban),
        };

        if (ValidateAmortization(loan) is { } error) return error;

        _context.Loans.Add(loan);
        await _context.SaveChangesAsync();

        return Ok(Map(loan, DateTime.UtcNow));
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<LoanDto>> Update(int id, UpdateLoanDto dto)
    {
        var userId = GetUserId();
        var loan = await _context.Loans.FindAsync(id);
        if (loan == null) return NotFound();
        if (!await UserCanAccessDashboard(loan.DashboardId, userId)) return Forbid();

        if (dto.Name != null) loan.Name = dto.Name;
        if (dto.Holder != null) loan.Holder = dto.Holder;
        if (dto.Kind.HasValue) loan.Kind = dto.Kind.Value;
        if (dto.Lender != null) loan.Lender = Blank(dto.Lender);
        if (dto.Reference != null) loan.Reference = Blank(dto.Reference);
        if (dto.InitialPrincipal.HasValue) loan.InitialPrincipal = Blank(dto.InitialPrincipal);
        if (dto.AnnualRatePercent.HasValue) loan.AnnualRatePercent = dto.AnnualRatePercent.Value;
        if (dto.MonthlyPayment.HasValue) loan.MonthlyPayment = dto.MonthlyPayment.Value;
        if (dto.AnchorDate.HasValue) loan.AnchorDate = dto.AnchorDate.Value.Date;
        if (dto.AnchorPrincipal.HasValue) loan.AnchorPrincipal = dto.AnchorPrincipal.Value;
        if (dto.DebitIban != null) loan.DebitIban = Blank(dto.DebitIban);
        if (dto.IsArchived.HasValue) loan.IsArchived = dto.IsArchived.Value;

        if (ValidateAmortization(loan) is { } error)
        {
            // L'entité suivie porte déjà les valeurs refusées : les oublier avant de rendre la main.
            _context.Entry(loan).State = EntityState.Detached;
            return error;
        }

        await _context.SaveChangesAsync();
        return Ok(Map(loan, DateTime.UtcNow));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var loan = await _context.Loans.FindAsync(id);
        if (loan == null) return NotFound();
        if (!await UserCanAccessDashboard(loan.DashboardId, userId)) return Forbid();

        _context.Loans.Remove(loan);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
