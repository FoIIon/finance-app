using FinanceApp.API.Data;
using FinanceApp.API.Models;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services;

public interface IInvitationService
{
    Task<DashboardInvitation> InviteToDashboard(int dashboardId, string email, int invitedByUserId);
    Task AcceptInvitation(string token, int userId);
    Task<List<DashboardInvitation>> GetPendingInvitations(int dashboardId, int userId);
    Task CancelInvitation(int invitationId, int userId);
}

public class InvitationService : IInvitationService
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;

    public InvitationService(AppDbContext context, IEmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    public async Task<DashboardInvitation> InviteToDashboard(int dashboardId, string email, int invitedByUserId)
    {
        var dashboard = await _context.Dashboards
            .Include(d => d.Members)
            .FirstOrDefaultAsync(d => d.Id == dashboardId && d.Members.Any(m => m.UserId == invitedByUserId));

        if (dashboard == null)
            throw new InvalidOperationException("Dashboard introuvable ou accès non autorisé.");

        // Vérifier si l'invité est déjà membre
        var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (existingUser != null && dashboard.Members.Any(m => m.UserId == existingUser.Id))
            throw new InvalidOperationException("Cet utilisateur est déjà membre du dashboard.");

        // Vérifier s'il y a déjà une invitation en attente
        var existingInvitation = await _context.DashboardInvitations
            .FirstOrDefaultAsync(i => i.DashboardId == dashboardId && i.InvitedEmail == email && i.Status == InvitationStatus.Pending);
        if (existingInvitation != null)
            throw new InvalidOperationException("Une invitation est déjà en attente pour cet email.");

        var inviter = await _context.Users.FindAsync(invitedByUserId);
        var token = Guid.NewGuid().ToString();

        var invitation = new DashboardInvitation
        {
            DashboardId = dashboardId,
            InvitedEmail = email,
            InvitedByUserId = invitedByUserId,
            Token = token,
            Status = InvitationStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        _context.DashboardInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        // Charger les navigations pour le DTO de réponse
        invitation.Dashboard = dashboard;
        invitation.InvitedByUser = inviter!;

        await _emailService.SendDashboardInvitationAsync(email, inviter!.Email, dashboard.Name, token);

        return invitation;
    }

    public async Task AcceptInvitation(string token, int userId)
    {
        var invitation = await _context.DashboardInvitations
            .Include(i => i.Dashboard)
            .FirstOrDefaultAsync(i => i.Token == token && i.Status == InvitationStatus.Pending);

        if (invitation == null)
            throw new InvalidOperationException("Invitation invalide ou déjà utilisée.");

        if (invitation.ExpiresAt < DateTime.UtcNow)
        {
            invitation.Status = InvitationStatus.Expired;
            await _context.SaveChangesAsync();
            throw new InvalidOperationException("L'invitation a expiré.");
        }

        var user = await _context.Users.FindAsync(userId);
        if (user == null || user.Email != invitation.InvitedEmail)
            throw new InvalidOperationException("Cette invitation n'est pas destinée à votre compte.");

        // Vérifier si déjà membre
        var alreadyMember = await _context.DashboardMembers
            .AnyAsync(dm => dm.DashboardId == invitation.DashboardId && dm.UserId == userId);

        if (!alreadyMember)
        {
            _context.DashboardMembers.Add(new DashboardMember
            {
                DashboardId = invitation.DashboardId,
                UserId = userId
            });
        }

        invitation.Status = InvitationStatus.Accepted;
        await _context.SaveChangesAsync();
    }

    public async Task<List<DashboardInvitation>> GetPendingInvitations(int dashboardId, int userId)
    {
        var isMember = await _context.DashboardMembers
            .AnyAsync(dm => dm.DashboardId == dashboardId && dm.UserId == userId);

        if (!isMember)
            throw new InvalidOperationException("Accès non autorisé.");

        return await _context.DashboardInvitations
            .Include(i => i.InvitedByUser)
            .Include(i => i.Dashboard)
            .Where(i => i.DashboardId == dashboardId && i.Status == InvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();
    }

    public async Task CancelInvitation(int invitationId, int userId)
    {
        var invitation = await _context.DashboardInvitations
            .Include(i => i.Dashboard).ThenInclude(d => d.Members)
            .FirstOrDefaultAsync(i => i.Id == invitationId);

        if (invitation == null)
            throw new InvalidOperationException("Invitation introuvable.");

        if (!invitation.Dashboard.Members.Any(m => m.UserId == userId))
            throw new InvalidOperationException("Accès non autorisé.");

        _context.DashboardInvitations.Remove(invitation);
        await _context.SaveChangesAsync();
    }
}
