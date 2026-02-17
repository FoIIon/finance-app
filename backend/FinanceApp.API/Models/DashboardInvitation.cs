namespace FinanceApp.API.Models;

public enum InvitationStatus
{
    Pending,
    Accepted,
    Expired
}

public class DashboardInvitation
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string InvitedEmail { get; set; } = string.Empty;
    public int InvitedByUserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Dashboard Dashboard { get; set; } = null!;
    public User InvitedByUser { get; set; } = null!;
}
