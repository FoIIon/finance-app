using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

public interface IDashboardService
{
    Task<Dashboard> CreateDefaultDashboardForUser(int userId, int defaultAccountId);
    Task<Dashboard> CreateDashboard(int userId, string name);
    Task<List<Dashboard>> GetUserDashboards(int userId);
    Task<Dashboard?> GetDashboardDetail(int dashboardId, int userId);
    Task<List<int>> GetDashboardAccountIds(int dashboardId, int userId);
    Task DeleteDashboard(int dashboardId, int userId);
}

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Dashboard> CreateDefaultDashboardForUser(int userId, int defaultAccountId)
    {
        var dashboard = new Dashboard
        {
            Name = "Personnel",
            CreatorId = userId
        };

        _context.Dashboards.Add(dashboard);
        await _context.SaveChangesAsync();

        // Ajouter le user comme membre
        _context.DashboardMembers.Add(new DashboardMember
        {
            DashboardId = dashboard.Id,
            UserId = userId
        });

        // Lier le compte par défaut
        _context.DashboardAccounts.Add(new DashboardAccount
        {
            DashboardId = dashboard.Id,
            AccountId = defaultAccountId
        });

        await _context.SaveChangesAsync();

        return dashboard;
    }

    public async Task<Dashboard> CreateDashboard(int userId, string name)
    {
        var dashboard = new Dashboard
        {
            Name = name,
            CreatorId = userId
        };

        _context.Dashboards.Add(dashboard);
        await _context.SaveChangesAsync();

        _context.DashboardMembers.Add(new DashboardMember
        {
            DashboardId = dashboard.Id,
            UserId = userId
        });

        await _context.SaveChangesAsync();

        return dashboard;
    }

    public async Task<List<Dashboard>> GetUserDashboards(int userId)
    {
        return await _context.Dashboards
            .Where(d => d.Members.Any(m => m.UserId == userId))
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();
    }

    public async Task<Dashboard?> GetDashboardDetail(int dashboardId, int userId)
    {
        return await _context.Dashboards
            .Include(d => d.Members).ThenInclude(m => m.User)
            .Include(d => d.DashboardAccounts).ThenInclude(da => da.Account)
            .FirstOrDefaultAsync(d => d.Id == dashboardId && d.Members.Any(m => m.UserId == userId));
    }

    public async Task<List<int>> GetDashboardAccountIds(int dashboardId, int userId)
    {
        var dashboard = await _context.Dashboards
            .Include(d => d.DashboardAccounts)
            .FirstOrDefaultAsync(d => d.Id == dashboardId && d.Members.Any(m => m.UserId == userId));

        if (dashboard == null) return new List<int>();

        return dashboard.DashboardAccounts.Select(da => da.AccountId).ToList();
    }

    public async Task DeleteDashboard(int dashboardId, int userId)
    {
        var dashboard = await _context.Dashboards
            .FirstOrDefaultAsync(d => d.Id == dashboardId && d.CreatorId == userId);

        if (dashboard == null)
            throw new InvalidOperationException("Dashboard introuvable ou non autorisé.");

        _context.Dashboards.Remove(dashboard);
        await _context.SaveChangesAsync();
    }
}
