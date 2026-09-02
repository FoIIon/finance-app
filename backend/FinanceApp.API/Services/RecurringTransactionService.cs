using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

public class RecurringTransactionService
{
    private readonly AppDbContext _context;

    public RecurringTransactionService(AppDbContext context)
    {
        _context = context;
    }

    private async Task<bool> UserHasAccessToDashboard(int userId, int dashboardId)
    {
        return await _context.DashboardMembers
            .AnyAsync(dm => dm.DashboardId == dashboardId && dm.UserId == userId);
    }

    private static RecurringTransactionDto MapToDto(RecurringTransaction r)
    {
        return new RecurringTransactionDto
        {
            Id = r.Id,
            DashboardId = r.DashboardId,
            AccountId = r.AccountId,
            AccountName = r.Account?.Name,
            CategoryId = r.CategoryId,
            CategoryName = r.Category?.Name,
            CategoryIcon = r.Category?.Icon,
            CategoryColor = r.Category?.Color,
            Description = r.Description,
            Amount = r.Amount,
            Type = r.Type.ToString(),
            Frequency = r.Frequency.ToString(),
            DayOfMonth = r.DayOfMonth,
            StartDate = r.StartDate,
            EndDate = r.EndDate,
            IsActive = r.IsActive,
            ProvisionAtMonthStart = r.ProvisionAtMonthStart,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        };
    }

    public async Task<List<RecurringTransactionDto>> GetAllAsync(int userId, int dashboardId)
    {
        if (!await UserHasAccessToDashboard(userId, dashboardId))
            return new List<RecurringTransactionDto>();

        return await _context.RecurringTransactions
            .Include(r => r.Account)
            .Include(r => r.Category)
            .Where(r => r.DashboardId == dashboardId && r.IsActive)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => MapToDto(r))
            .ToListAsync();
    }

    public async Task<RecurringTransactionDto?> GetByIdAsync(int userId, int dashboardId, int id)
    {
        if (!await UserHasAccessToDashboard(userId, dashboardId))
            return null;

        var r = await _context.RecurringTransactions
            .Include(r => r.Account)
            .Include(r => r.Category)
            .FirstOrDefaultAsync(r => r.Id == id && r.DashboardId == dashboardId);

        return r == null ? null : MapToDto(r);
    }

    public async Task<(RecurringTransactionDto? result, string? error)> CreateAsync(int userId, CreateRecurringTransactionDto dto)
    {
        if (!await UserHasAccessToDashboard(userId, dto.DashboardId))
            return (null, "Dashboard introuvable ou accès non autorisé.");

        if (dto.AccountId.HasValue)
        {
            var account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.Id == dto.AccountId.Value && a.UserId == userId);
            if (account == null)
                return (null, "Compte invalide.");
        }

        if (dto.CategoryId.HasValue)
        {
            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == dto.CategoryId.Value && (c.IsDefault || c.UserId == userId));
            if (category == null)
                return (null, "Catégorie invalide.");
        }

        var recurring = new RecurringTransaction
        {
            UserId = userId,
            DashboardId = dto.DashboardId,
            AccountId = dto.AccountId,
            CategoryId = dto.CategoryId,
            Description = dto.Description,
            Amount = dto.Amount,
            Type = Enum.Parse<TransactionType>(dto.Type),
            Frequency = Enum.Parse<RecurringFrequency>(dto.Frequency),
            DayOfMonth = dto.DayOfMonth,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            IsActive = true,
            ProvisionAtMonthStart = dto.ProvisionAtMonthStart,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.RecurringTransactions.Add(recurring);
        await _context.SaveChangesAsync();

        // Recharger avec includes pour le retour
        var created = await _context.RecurringTransactions
            .Include(r => r.Account)
            .Include(r => r.Category)
            .FirstAsync(r => r.Id == recurring.Id);

        return (MapToDto(created), null);
    }

    public async Task<(RecurringTransactionDto? result, string? error)> UpdateAsync(int userId, int dashboardId, int id, UpdateRecurringTransactionDto dto)
    {
        if (!await UserHasAccessToDashboard(userId, dashboardId))
            return (null, "Dashboard introuvable ou accès non autorisé.");

        var recurring = await _context.RecurringTransactions
            .FirstOrDefaultAsync(r => r.Id == id && r.DashboardId == dashboardId);

        if (recurring == null)
            return (null, null); // NotFound

        if (dto.AccountId.HasValue)
        {
            var account = await _context.Accounts
                .FirstOrDefaultAsync(a => a.Id == dto.AccountId.Value && a.UserId == userId);
            if (account == null)
                return (null, "Compte invalide.");
        }

        if (dto.CategoryId.HasValue)
        {
            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == dto.CategoryId.Value && (c.IsDefault || c.UserId == userId));
            if (category == null)
                return (null, "Catégorie invalide.");
        }

        if (dto.Description != null) recurring.Description = dto.Description;
        if (dto.Amount.HasValue) recurring.Amount = dto.Amount.Value;
        if (dto.Type != null) recurring.Type = Enum.Parse<TransactionType>(dto.Type);
        if (dto.Frequency != null) recurring.Frequency = Enum.Parse<RecurringFrequency>(dto.Frequency);
        if (dto.DayOfMonth.HasValue) recurring.DayOfMonth = dto.DayOfMonth;
        if (dto.StartDate.HasValue) recurring.StartDate = dto.StartDate.Value;
        if (dto.EndDate.HasValue) recurring.EndDate = dto.EndDate;
        if (dto.IsActive.HasValue) recurring.IsActive = dto.IsActive.Value;
        if (dto.ProvisionAtMonthStart.HasValue) recurring.ProvisionAtMonthStart = dto.ProvisionAtMonthStart.Value;
        if (dto.AccountId != null) recurring.AccountId = dto.AccountId;
        if (dto.CategoryId != null) recurring.CategoryId = dto.CategoryId;
        recurring.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var updated = await _context.RecurringTransactions
            .Include(r => r.Account)
            .Include(r => r.Category)
            .FirstAsync(r => r.Id == recurring.Id);

        return (MapToDto(updated), null);
    }

    public async Task<bool> DeleteAsync(int userId, int dashboardId, int id)
    {
        if (!await UserHasAccessToDashboard(userId, dashboardId))
            return false;

        var recurring = await _context.RecurringTransactions
            .FirstOrDefaultAsync(r => r.Id == id && r.DashboardId == dashboardId);

        if (recurring == null)
            return false;

        // Soft delete
        recurring.IsActive = false;
        recurring.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return true;
    }
}
