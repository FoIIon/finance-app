using FinanceApp.API.Data;
using FinanceApp.API.DTOs;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly TokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly IAccountService _accountService;
    private readonly IDashboardService _dashboardService;

    public AuthController(
        AppDbContext context,
        TokenService tokenService,
        IEmailService emailService,
        IAccountService accountService,
        IDashboardService dashboardService)
    {
        _context = context;
        _tokenService = tokenService;
        _emailService = emailService;
        _accountService = accountService;
        _dashboardService = dashboardService;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<MessageDto>> Register(RegisterDto dto)
    {
        if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            return BadRequest("Impossible de créer le compte. Vérifiez vos informations.");

        var confirmationToken = Guid.NewGuid().ToString();

        var user = new User
        {
            Email = dto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            EmailConfirmed = false,
            EmailConfirmationToken = confirmationToken,
            EmailConfirmationTokenExpiry = DateTime.UtcNow.AddHours(48)
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // Créer le compte et dashboard par défaut
        var defaultAccount = await _accountService.CreateDefaultAccount(user.Id);
        await _dashboardService.CreateDefaultDashboardForUser(user.Id, defaultAccount.Id);

        await _emailService.SendEmailConfirmationAsync(user.Email, confirmationToken);

        return Ok(new MessageDto { Message = "Compte créé. Vérifiez votre email pour confirmer votre inscription." });
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized("Email ou mot de passe incorrect.");

        if (!user.EmailConfirmed)
            return StatusCode(403, new MessageDto { Message = "Veuillez confirmer votre adresse email avant de vous connecter." });

        var token = _tokenService.GenerateToken(user);

        return Ok(new AuthResponseDto { Token = token, Email = user.Email });
    }

    [HttpPost("confirm-email")]
    public async Task<ActionResult<MessageDto>> ConfirmEmail(ConfirmEmailDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.EmailConfirmationToken == dto.Token);

        if (user == null)
            return BadRequest(new MessageDto { Message = "Token de confirmation invalide." });

        if (user.EmailConfirmationTokenExpiry < DateTime.UtcNow)
            return BadRequest(new MessageDto { Message = "Le lien de confirmation a expiré. Demandez un nouveau lien." });

        user.EmailConfirmed = true;
        user.EmailConfirmationToken = null;
        user.EmailConfirmationTokenExpiry = null;
        await _context.SaveChangesAsync();

        return Ok(new MessageDto { Message = "Email confirmé avec succès. Vous pouvez maintenant vous connecter." });
    }

    [HttpPost("resend-confirmation")]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<MessageDto>> ResendConfirmation(ResendConfirmationDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

        // Réponse identique que le user existe ou non (sécurité)
        if (user == null || user.EmailConfirmed)
            return Ok(new MessageDto { Message = "Si un compte non confirmé existe avec cet email, un nouveau lien a été envoyé." });

        user.EmailConfirmationToken = Guid.NewGuid().ToString();
        user.EmailConfirmationTokenExpiry = DateTime.UtcNow.AddHours(48);
        await _context.SaveChangesAsync();

        await _emailService.SendEmailConfirmationAsync(user.Email, user.EmailConfirmationToken);

        return Ok(new MessageDto { Message = "Si un compte non confirmé existe avec cet email, un nouveau lien a été envoyé." });
    }
}
