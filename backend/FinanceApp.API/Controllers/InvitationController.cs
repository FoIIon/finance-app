using FinanceApp.API.DTOs;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class InvitationController : ApiControllerBase
{
    private readonly IInvitationService _invitationService;

    public InvitationController(IInvitationService invitationService)
    {
        _invitationService = invitationService;
    }

    [HttpPost("send")]
    public async Task<ActionResult<InvitationDto>> Send(InviteDto dto)
    {
        try
        {
            var invitation = await _invitationService.InviteToDashboard(dto.DashboardId, dto.Email, GetUserId());
            return Ok(new InvitationDto
            {
                Id = invitation.Id,
                DashboardId = invitation.DashboardId,
                DashboardName = invitation.Dashboard.Name,
                InvitedEmail = invitation.InvitedEmail,
                InvitedByEmail = invitation.InvitedByUser.Email,
                Status = invitation.Status,
                ExpiresAt = invitation.ExpiresAt,
                CreatedAt = invitation.CreatedAt
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new MessageDto { Message = ex.Message });
        }
    }

    [HttpPost("accept")]
    public async Task<ActionResult<MessageDto>> Accept(AcceptInvitationDto dto)
    {
        try
        {
            await _invitationService.AcceptInvitation(dto.Token, GetUserId());
            return Ok(new MessageDto { Message = "Invitation acceptée avec succès." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new MessageDto { Message = ex.Message });
        }
    }

    [HttpGet("pending/{dashboardId}")]
    public async Task<ActionResult<List<InvitationDto>>> GetPending(int dashboardId)
    {
        try
        {
            var invitations = await _invitationService.GetPendingInvitations(dashboardId, GetUserId());
            return Ok(invitations.Select(i => new InvitationDto
            {
                Id = i.Id,
                DashboardId = i.DashboardId,
                DashboardName = i.Dashboard.Name,
                InvitedEmail = i.InvitedEmail,
                InvitedByEmail = i.InvitedByUser.Email,
                Status = i.Status,
                ExpiresAt = i.ExpiresAt,
                CreatedAt = i.CreatedAt
            }));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new MessageDto { Message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Cancel(int id)
    {
        try
        {
            await _invitationService.CancelInvitation(id, GetUserId());
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new MessageDto { Message = ex.Message });
        }
    }
}
