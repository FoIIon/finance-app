using System.ComponentModel.DataAnnotations;
using FinanceApp.API.Models;

namespace FinanceApp.API.DTOs;

public class InviteDto
{
    [Required]
    public int DashboardId { get; set; }

    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; set; } = string.Empty;
}

public class AcceptInvitationDto
{
    [Required]
    public string Token { get; set; } = string.Empty;
}

public class InvitationDto
{
    public int Id { get; set; }
    public int DashboardId { get; set; }
    public string DashboardName { get; set; } = string.Empty;
    public string InvitedEmail { get; set; } = string.Empty;
    public string InvitedByEmail { get; set; } = string.Empty;
    public InvitationStatus Status { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
