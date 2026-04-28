using System.Security.Claims;
using FinanceApp.API.DTOs;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApp.API.Controllers;

[ApiController]
[Route("api/dashboards/{dashboardId}/recurring")]
[Authorize]
public class RecurringTransactionController : ControllerBase
{
    private readonly RecurringTransactionService _service;

    public RecurringTransactionController(RecurringTransactionService service)
    {
        _service = service;
    }

    private int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId))
            throw new InvalidOperationException("Claim NameIdentifier absent ou invalide.");
        return userId;
    }

    [HttpGet]
    public async Task<ActionResult<List<RecurringTransactionDto>>> GetAll(int dashboardId)
    {
        var userId = GetUserId();
        var result = await _service.GetAllAsync(userId, dashboardId);
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<RecurringTransactionDto>> GetById(int dashboardId, int id)
    {
        var userId = GetUserId();
        var result = await _service.GetByIdAsync(userId, dashboardId, id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<RecurringTransactionDto>> Create(int dashboardId, CreateRecurringTransactionDto dto)
    {
        var userId = GetUserId();
        // Forcer le dashboardId depuis la route
        dto.DashboardId = dashboardId;

        var (result, error) = await _service.CreateAsync(userId, dto);
        if (error != null) return BadRequest(error);
        if (result == null) return BadRequest("Erreur lors de la création.");

        return CreatedAtAction(nameof(GetById), new { dashboardId, id = result.Id }, result);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<RecurringTransactionDto>> Update(int dashboardId, int id, UpdateRecurringTransactionDto dto)
    {
        var userId = GetUserId();
        var (result, error) = await _service.UpdateAsync(userId, dashboardId, id, dto);
        if (error != null) return BadRequest(error);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int dashboardId, int id)
    {
        var userId = GetUserId();
        var success = await _service.DeleteAsync(userId, dashboardId, id);
        if (!success) return NotFound();
        return NoContent();
    }
}
