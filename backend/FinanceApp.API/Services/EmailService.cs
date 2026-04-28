using System.Net;
using System.Net.Mail;

namespace FinanceApp.API.Services;

public interface IEmailService
{
    Task SendEmailConfirmationAsync(string toEmail, string confirmationToken);
    Task SendDashboardInvitationAsync(string toEmail, string inviterEmail, string dashboardName, string invitationToken);
}

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IConfiguration configuration, ILogger<SmtpEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendEmailConfirmationAsync(string toEmail, string confirmationToken)
    {
        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:5173";
        var confirmUrl = $"{frontendUrl}/confirm-email?token={confirmationToken}";

        var subject = "FinanceApp — Confirmez votre adresse email";
        var body = $"""
            <h2>Bienvenue sur FinanceApp !</h2>
            <p>Cliquez sur le lien ci-dessous pour confirmer votre adresse email :</p>
            <p><a href="{confirmUrl}" style="display:inline-block;padding:12px 24px;background:#f59e0b;color:#fff;text-decoration:none;border-radius:8px;font-weight:bold;">Confirmer mon email</a></p>
            <p>Ou copiez ce lien : {confirmUrl}</p>
            <p>Ce lien expire dans 48 heures.</p>
            """;

        await SendEmailAsync(toEmail, subject, body);
    }

    public async Task SendDashboardInvitationAsync(string toEmail, string inviterEmail, string dashboardName, string invitationToken)
    {
        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:5173";
        var acceptUrl = $"{frontendUrl}/invitation/accept?token={invitationToken}";

        var subject = $"FinanceApp — {inviterEmail} vous invite à rejoindre \"{dashboardName}\"";
        var body = $"""
            <h2>Invitation au dashboard "{dashboardName}"</h2>
            <p><strong>{inviterEmail}</strong> vous invite à rejoindre son dashboard sur FinanceApp.</p>
            <p><a href="{acceptUrl}" style="display:inline-block;padding:12px 24px;background:#f59e0b;color:#fff;text-decoration:none;border-radius:8px;font-weight:bold;">Accepter l'invitation</a></p>
            <p>Ou copiez ce lien : {acceptUrl}</p>
            <p>Cette invitation expire dans 7 jours.</p>
            """;

        await SendEmailAsync(toEmail, subject, body);
    }

    private async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
    {
        var smtpConfig = _configuration.GetSection("Smtp");
        var host = smtpConfig["Host"] ?? "localhost";
        var port = int.Parse(smtpConfig["Port"] ?? "587");
        var username = smtpConfig["Username"] ?? "";
        var password = smtpConfig["Password"] ?? "";
        var fromEmail = smtpConfig["FromEmail"] ?? "noreply@financeapp.local";
        var fromName = smtpConfig["FromName"] ?? "FinanceApp";
        var useSsl = bool.Parse(smtpConfig["UseSsl"] ?? "true");

        using var client = new SmtpClient(host, port)
        {
            Credentials = new NetworkCredential(username, password),
            EnableSsl = useSsl
        };

        var message = new MailMessage
        {
            From = new MailAddress(fromEmail, fromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(toEmail);

        try
        {
            await client.SendMailAsync(message);
            _logger.LogInformation("Email envoyé à {Email} : {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de l'envoi de l'email à {Email}", toEmail);
            throw;
        }
    }
}

public class DevEmailService : IEmailService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<DevEmailService> _logger;

    public DevEmailService(IConfiguration configuration, ILogger<DevEmailService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public Task SendEmailConfirmationAsync(string toEmail, string confirmationToken)
    {
        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:5173";
        var confirmUrl = $"{frontendUrl}/confirm-email?token={confirmationToken}";
        _logger.LogWarning("=== [DEV EMAIL] Confirmation pour {Email} ===\nLien : {Url}\n===", toEmail, confirmUrl);
        return Task.CompletedTask;
    }

    public Task SendDashboardInvitationAsync(string toEmail, string inviterEmail, string dashboardName, string invitationToken)
    {
        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:5173";
        var acceptUrl = $"{frontendUrl}/invitation/accept?token={invitationToken}";
        _logger.LogWarning("=== [DEV EMAIL] Invitation de {Inviter} pour {Email} — {Dashboard} ===\nLien : {Url}\n===", inviterEmail, toEmail, dashboardName, acceptUrl);
        return Task.CompletedTask;
    }
}
