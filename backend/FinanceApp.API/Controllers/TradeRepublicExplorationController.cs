using System.Security.Claims;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Controllers;

public class TradeRepublicProbeRequest
{
    public int ConnectionId { get; set; }
    /// <summary>Chemins REST à sonder (GET avec le cookie de session), ex. "/api/v1/portfolio".</summary>
    public List<string> Rest { get; set; } = new();
    /// <summary>Payloads JSON de souscription WebSocket à sonder, ex. {"type":"compactPortfolio"}.</summary>
    public List<string> Ws { get; set; } = new();
}

public class TradeRepublicProbeResult
{
    public string Probe { get; set; } = string.Empty;
    public int? Status { get; set; }
    public string Body { get; set; } = string.Empty;
}

/// <summary>
/// Harnais d'exploration du lot 4 : l'API Trade Republic n'est ni publique ni documentée,
/// la forme des réponses portefeuille s'observe en session interactive avant d'écrire le
/// parsing (exigence de la spec du 2026-07-28). Réservé à l'environnement Development,
/// n'existe pas fonctionnellement en production.
/// </summary>
[ApiController]
[Route("api/banking/traderepublic")]
[Authorize]
public class TradeRepublicExplorationController : ControllerBase
{
    private const int BodyTruncation = 12000;

    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;

    public TradeRepublicExplorationController(AppDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    private int GetUserId() =>
        int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Ajoute le token de session dans l'objet JSON d'une souscription WebSocket.</summary>
    private static string InjectToken(string payloadJson, string sessionToken)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
        var dict = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => prop.Value.GetString(),
                System.Text.Json.JsonValueKind.Number => prop.Value.GetRawText(),
                _ => prop.Value.GetRawText(),
            };
        dict["token"] = sessionToken;
        return System.Text.Json.JsonSerializer.Serialize(dict);
    }

    [HttpPost("probe")]
    public async Task<ActionResult<List<TradeRepublicProbeResult>>> Probe(
        TradeRepublicProbeRequest dto,
        [FromServices] TradeRepublicClient trClient)
    {
        if (!_env.IsDevelopment()) return NotFound();

        var userId = GetUserId();
        // Statut Error toléré : le sync de démarrage bascule la connexion en Error dès que
        // l'ancien refresh répond 405, alors que les tokens restent présents et exploitables.
        var connection = await _context.BankConnections
            .FirstOrDefaultAsync(bc => bc.Id == dto.ConnectionId && bc.UserId == userId
                && bc.Provider == BankProvider.TradeRepublic
                && (bc.Status == BankConnectionStatus.Linked || bc.Status == BankConnectionStatus.Error));

        if (connection == null) return NotFound("Aucune connexion Trade Republic liée.");
        if (string.IsNullOrEmpty(connection.EncryptedRefreshToken))
            return BadRequest("Pas de refresh token stocké.");

        var refreshToken = trClient.DecryptToken(connection.EncryptedRefreshToken);
        var deviceToken = string.IsNullOrEmpty(connection.EncryptedDeviceToken)
            ? ""
            : trClient.DecryptToken(connection.EncryptedDeviceToken);

        // Session fraîche issue du login v2 : on l'utilise directement. L'ancien endpoint
        // de refresh répond 405 (TR l'a retiré), le rafraîchissement passera par un autre
        // mécanisme, à déterminer une fois la forme du portefeuille connue.
        var sessionToken = string.IsNullOrEmpty(connection.EncryptedSessionToken)
            ? throw new InvalidOperationException("Pas de session token stocké — relance la connexion.")
            : trClient.DecryptToken(connection.EncryptedSessionToken);

        var results = new List<TradeRepublicProbeResult>();

        foreach (var path in dto.Rest)
        {
            try
            {
                var (status, body) = await trClient.ProbeRestAsync(sessionToken, path);
                results.Add(new TradeRepublicProbeResult
                {
                    Probe = $"REST {path}",
                    Status = status,
                    Body = body.Length > BodyTruncation ? body[..BodyTruncation] + "…[tronqué]" : body,
                });
            }
            catch (Exception ex)
            {
                results.Add(new TradeRepublicProbeResult { Probe = $"REST {path}", Body = $"EXCEPTION: {ex.Message}" });
            }
        }

        if (dto.Ws.Count > 0)
        {
            try
            {
                await trClient.ConnectAsync(refreshToken, deviceToken);
                foreach (var payload in dto.Ws)
                {
                    try
                    {
                        // Les topics authentifiés exigent le session token dans la souscription
                        // (« No auth token » sinon). On l'injecte dans l'objet JSON fourni.
                        var withToken = InjectToken(payload, sessionToken);
                        var body = await trClient.ProbeSubscriptionAsync(withToken);
                        results.Add(new TradeRepublicProbeResult
                        {
                            Probe = $"WS {payload}",
                            Body = body.Length > BodyTruncation ? body[..BodyTruncation] + "…[tronqué]" : body,
                        });
                    }
                    catch (Exception ex)
                    {
                        results.Add(new TradeRepublicProbeResult { Probe = $"WS {payload}", Body = $"EXCEPTION: {ex.Message}" });
                    }
                }
            }
            catch (Exception ex)
            {
                results.Add(new TradeRepublicProbeResult { Probe = "WS connect", Body = $"EXCEPTION: {ex.Message}" });
            }
        }

        return Ok(results);
    }
}
