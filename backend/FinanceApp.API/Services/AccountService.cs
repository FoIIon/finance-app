using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

public interface IAccountService
{
    Task<Account> CreateDefaultAccount(int userId);
    Task<Account> CreateAccount(int userId, string name);
    Task<List<Account>> GetUserAccounts(int userId);
}

public class AccountService : IAccountService
{
    private readonly AppDbContext _context;

    public AccountService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Account> CreateDefaultAccount(int userId)
    {
        var account = new Account { Name = "Compte principal", UserId = userId, IsPrimary = true };
        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();
        return account;
    }

    public async Task<Account> CreateAccount(int userId, string name)
    {
        var account = new Account
        {
            Name = name,
            UserId = userId
        };

        _context.Accounts.Add(account);
        await _context.SaveChangesAsync();

        return account;
    }

    public async Task<List<Account>> GetUserAccounts(int userId)
    {
        return await _context.Accounts
            .Where(a => a.UserId == userId)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();
    }
}
