using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AccountController : ApiControllerBase
{
    private readonly AppDbContext _context;
    private readonly IAccountService _accountService;

    public AccountController(AppDbContext context, IAccountService accountService)
    {
        _context = context;
        _accountService = accountService;
    }

    [HttpGet]
    public async Task<ActionResult<List<AccountDto>>> GetAll()
    {
        var accounts = await _accountService.GetUserAccounts(GetUserId());
        return Ok(accounts.Select(a => new AccountDto
        {
            Id = a.Id,
            Name = a.Name,
            CreatedAt = a.CreatedAt,
            IsPersonalScope = a.IsPersonalScope
        }));
    }

    [HttpPost]
    public async Task<ActionResult<AccountDto>> Create(CreateAccountDto dto)
    {
        var account = await _accountService.CreateAccount(GetUserId(), dto.Name);
        return CreatedAtAction(nameof(GetAll), new AccountDto
        {
            Id = account.Id,
            Name = account.Name,
            CreatedAt = account.CreatedAt,
            IsPersonalScope = account.IsPersonalScope
        });
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<AccountDto>> Update(int id, CreateAccountDto dto)
    {
        var userId = GetUserId();
        var account = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
        if (account == null) return NotFound();

        account.Name = dto.Name;
        await _context.SaveChangesAsync();

        return Ok(new AccountDto
        {
            Id = account.Id,
            Name = account.Name,
            CreatedAt = account.CreatedAt,
            IsPersonalScope = account.IsPersonalScope
        });
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var account = await _context.Accounts
            .Include(a => a.Transactions)
            .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);

        if (account == null) return NotFound();
        if (account.Transactions.Any())
            return BadRequest("Impossible de supprimer un compte qui contient des transactions.");
        if (account.IsPrimary || account.IsPersonalScope)
            return BadRequest("Le compte principal et le compte Perso sont structurels, ils ne se suppriment pas.");

        _context.Accounts.Remove(account);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
