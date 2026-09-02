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
[Route("api/[controller]")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IDashboardService _dashboardService;

    public DashboardController(AppDbContext context, IDashboardService dashboardService)
    {
        _context = context;
        _dashboardService = dashboardService;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<List<DashboardDto>>> GetAll()
    {
        var userId = GetUserId();
        var dashboards = await _dashboardService.GetUserDashboards(userId);
        return Ok(dashboards.Select(d => new DashboardDto
        {
            Id = d.Id,
            Name = d.Name,
            IsCreator = d.CreatorId == userId,
            IsPersonal = d.IsPersonal,
            CreatedAt = d.CreatedAt
        }));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<DashboardDetailDto>> GetById(int id)
    {
        var userId = GetUserId();
        var dashboard = await _dashboardService.GetDashboardDetail(id, userId);

        if (dashboard == null) return NotFound();

        return Ok(new DashboardDetailDto
        {
            Id = dashboard.Id,
            Name = dashboard.Name,
            IsCreator = dashboard.CreatorId == userId,
            IsPersonal = dashboard.IsPersonal,
            CreatedAt = dashboard.CreatedAt,
            Members = dashboard.Members.Select(m => new DashboardMemberDto
            {
                UserId = m.UserId,
                Email = m.User.Email,
                JoinedAt = m.JoinedAt
            }).ToList(),
            Accounts = dashboard.DashboardAccounts.Select(da => new AccountDto
            {
                Id = da.Account.Id,
                Name = da.Account.Name,
                CreatedAt = da.Account.CreatedAt
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<ActionResult<DashboardDto>> Create(CreateDashboardDto dto)
    {
        var userId = GetUserId();
        var dashboard = await _dashboardService.CreateDashboard(userId, dto.Name);
        return CreatedAtAction(nameof(GetById), new { id = dashboard.Id }, new DashboardDto
        {
            Id = dashboard.Id,
            Name = dashboard.Name,
            IsCreator = true,
            IsPersonal = false,
            CreatedAt = dashboard.CreatedAt
        });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<DashboardDto>> Update(int id, UpdateDashboardDto dto)
    {
        var userId = GetUserId();
        var dashboard = await _context.Dashboards
            .FirstOrDefaultAsync(d => d.Id == id && d.Members.Any(m => m.UserId == userId));

        if (dashboard == null) return NotFound();

        dashboard.Name = dto.Name;
        await _context.SaveChangesAsync();

        return Ok(new DashboardDto
        {
            Id = dashboard.Id,
            Name = dashboard.Name,
            IsCreator = dashboard.CreatorId == userId,
            IsPersonal = dashboard.IsPersonal,
            CreatedAt = dashboard.CreatedAt
        });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        try
        {
            await _dashboardService.DeleteDashboard(id, GetUserId());
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id}/accounts")]
    public async Task<ActionResult> AddAccount(int id, AddAccountToDashboardDto dto)
    {
        var userId = GetUserId();
        var dashboard = await _context.Dashboards
            .Include(d => d.Members)
            .FirstOrDefaultAsync(d => d.Id == id && d.Members.Any(m => m.UserId == userId));

        if (dashboard == null) return NotFound();

        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == dto.AccountId && a.UserId == userId);
        if (account == null) return BadRequest("Compte introuvable.");

        var exists = await _context.DashboardAccounts
            .AnyAsync(da => da.DashboardId == id && da.AccountId == dto.AccountId);
        if (exists) return BadRequest("Ce compte est déjà lié à ce dashboard.");

        _context.DashboardAccounts.Add(new DashboardAccount
        {
            DashboardId = id,
            AccountId = dto.AccountId
        });
        await _context.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("{id}/accounts/{accountId}")]
    public async Task<ActionResult> RemoveAccount(int id, int accountId)
    {
        var userId = GetUserId();
        var dashboard = await _context.Dashboards
            .Include(d => d.Members)
            .FirstOrDefaultAsync(d => d.Id == id && d.Members.Any(m => m.UserId == userId));

        if (dashboard == null) return NotFound();

        var link = await _context.DashboardAccounts
            .FirstOrDefaultAsync(da => da.DashboardId == id && da.AccountId == accountId);
        if (link == null) return NotFound();

        _context.DashboardAccounts.Remove(link);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
