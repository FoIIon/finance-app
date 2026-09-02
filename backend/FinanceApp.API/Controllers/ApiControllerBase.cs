using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceApp.API.Controllers;

/// <summary>
/// Socle des contrôleurs authentifiés. Porte la lecture de l'identifiant utilisateur depuis le jeton,
/// qui était recopiée dans quinze contrôleurs en deux variantes (l'une levait une exception claire,
/// l'autre un FormatException sur un jeton mal formé). Une seule version désormais, la stricte.
/// </summary>
[ApiController]
[Authorize]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>L'identifiant de l'utilisateur authentifié, lu dans la claim NameIdentifier du jeton.</summary>
    protected int GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(raw, out var userId))
            throw new InvalidOperationException("Claim NameIdentifier absent ou invalide.");
        return userId;
    }
}
