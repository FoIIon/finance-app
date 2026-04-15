using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;

namespace FinanceApp.API.Services;

public class TrCardTransaction
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}

public class TradeRepublicClient : IDisposable
{
    private readonly string _webSocketUrl;
    private readonly IDataProtector _protector;
    private readonly ILogger<TradeRepublicClient> _logger;
    private readonly HttpClient _httpClient;
    private ClientWebSocket? _ws;
    private int _subscriptionId;

    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);

    // Liste blanche des types de messages WebSocket autorisés (auth et refresh désormais via HTTP)
    private static readonly HashSet<string> AllowedMessageTypes = new()
    {
        "timeline"
    };

    private static readonly Regex EchoPattern = new(@"^echo \d+$", RegexOptions.Compiled);

    public TradeRepublicClient(
        IConfiguration configuration,
        IDataProtectionProvider dataProtection,
        ILogger<TradeRepublicClient> logger,
        HttpClient httpClient)
    {
        _webSocketUrl = configuration["TradeRepublic:WebSocketUrl"] ?? "wss://api.traderepublic.com/";
        _protector = dataProtection.CreateProtector("TradeRepublic.Tokens");
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.traderepublic.com");
        _httpClient.DefaultRequestHeaders.Add(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36");
    }

    /// <summary>
    /// Initie le login via HTTP — déclenche l'envoi d'un SMS de vérification.
    /// </summary>
    public async Task<string> InitiateLoginAsync(string phoneNumber, string pin, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct);
        var payload = JsonSerializer.Serialize(new { phoneNumber, pin });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/api/v1/auth/web/login", content, timeoutCts.Token);
        var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Erreur Trade Republic ({(int)response.StatusCode}) : {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("processId").GetString()!;
    }

    /// <summary>
    /// Vérifie le code SMS via HTTP.
    /// TR retourne les tokens dans des cookies Set-Cookie (pas dans le body).
    /// </summary>
    public async Task<(string SessionToken, string RefreshToken, string DeviceToken)> ConfirmTwoFactorAsync(string processId, string code, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct);

        var response = await _httpClient.PostAsync(
            $"/api/v1/auth/web/login/{processId}/{code}",
            null,
            timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            throw new InvalidOperationException($"Code invalide ou session expirée ({(int)response.StatusCode}) : {errorBody}");
        }

        if (!response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
            throw new InvalidOperationException("Aucun cookie de session dans la réponse Trade Republic.");

        var cookies = cookieHeaders.ToList();
        var sessionToken = ExtractCookieValue(cookies, "tr_session") ?? "";
        var refreshToken = ExtractCookieValue(cookies, "tr_refresh")
            ?? throw new InvalidOperationException("Cookie tr_refresh manquant dans la réponse Trade Republic.");
        var deviceToken = ExtractCookieValue(cookies, "tr_device") ?? "";

        return (sessionToken, refreshToken, deviceToken);
    }

    /// <summary>
    /// Extrait la valeur d'un cookie depuis les headers Set-Cookie.
    /// </summary>
    private static string? ExtractCookieValue(List<string> setCookieHeaders, string cookieName)
    {
        var prefix = $"{cookieName}=";
        foreach (var header in setCookieHeaders)
        {
            if (!header.StartsWith(prefix)) continue;
            var value = header[prefix.Length..];
            var semicolonIdx = value.IndexOf(';');
            return semicolonIdx >= 0 ? value[..semicolonIdx] : value;
        }
        return null;
    }

    /// <summary>
    /// Connecte le WebSocket en injectant les cookies TR dans le handshake HTTP,
    /// reproduisant le comportement d'un navigateur authentifié.
    /// </summary>
    public async Task ConnectAsync(string? refreshToken = null, string? deviceToken = null, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct);

        _ws = new ClientWebSocket();

        // Injecter les cookies TR dans la connexion WebSocket comme le ferait un vrai navigateur
        if (!string.IsNullOrEmpty(refreshToken) || !string.IsNullOrEmpty(deviceToken))
        {
            var jar = new System.Net.CookieContainer();
            var baseUri = new Uri("https://api.traderepublic.com");
            if (!string.IsNullOrEmpty(refreshToken))
                jar.Add(baseUri, new System.Net.Cookie("tr_refresh", refreshToken));
            if (!string.IsNullOrEmpty(deviceToken))
                jar.Add(baseUri, new System.Net.Cookie("tr_device", deviceToken));
            _ws.Options.Cookies = jar;
        }

        await _ws.ConnectAsync(new Uri(_webSocketUrl), timeoutCts.Token);

        var connectMsg = "connect 31 {\"locale\":\"en\",\"platformId\":\"webtrading\",\"platformVersion\":\"chrome - 145.0.0\",\"clientId\":\"app.traderepublic.com\",\"clientVersion\":\"13.40.5\"}";
        await SendRawAsync(connectMsg, timeoutCts.Token);

        // Boucler jusqu'à "connected" — ignorer les echo keepalive éventuels
        while (true)
        {
            var response = await ReceiveAsync(timeoutCts.Token);
            _logger.LogInformation("TR connect response: {response}", response[..Math.Min(300, response.Length)]);

            if (response.StartsWith("connected")) break;

            if (response.StartsWith("echo") && EchoPattern.IsMatch(response))
            {
                await SendRawAsync(response, timeoutCts.Token);
                continue;
            }

            throw new InvalidOperationException($"Connexion Trade Republic échouée : {response[..Math.Min(300, response.Length)]}");
        }
    }

    /// <summary>
    /// Rafraîchit la session via HTTP — le refresh token est envoyé en body JSON.
    /// Si la réponse contient un tr_session dans Set-Cookie, le retourne ;
    /// sinon tente de lire le sessionToken dans le body JSON.
    /// </summary>
    public async Task<string> RefreshSessionAsync(string refreshToken, string deviceToken, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct);

        var body = JsonSerializer.Serialize(new { refreshToken });
        var content = new StringContent(body, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/web/refresh");
        request.Content = content;

        // tr_device identifie l'appareil — envoyé en cookie s'il est disponible
        if (!string.IsNullOrEmpty(deviceToken))
            request.Headers.Add("Cookie", $"tr_device={deviceToken}");

        var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Erreur refresh token ({(int)response.StatusCode}) : {responseBody}");

        // Cas 1 : session token dans Set-Cookie
        if (response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
        {
            var sessionFromCookie = ExtractCookieValue(cookieHeaders.ToList(), "tr_session");
            if (sessionFromCookie != null) return sessionFromCookie;
        }

        // Cas 2 : session token dans le body JSON
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("sessionToken", out var sessionProp))
                return sessionProp.GetString()
                    ?? throw new InvalidOperationException("sessionToken vide dans la réponse de refresh.");
        }

        throw new InvalidOperationException($"Impossible d'extraire le session token de la réponse de refresh. Body: {responseBody[..Math.Min(200, responseBody.Length)]}");
    }

    /// <summary>
    /// Récupère les transactions via l'API REST TR (/api/v2/timeline/transactions).
    /// Le session token est envoyé en Bearer. Le timestamp est au format ISO 8601.
    /// </summary>
    public async Task<List<TrCardTransaction>> GetCardTransactionsHttpAsync(string sessionToken, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct, TimeSpan.FromSeconds(60));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v2/timeline/transactions");
        // tr_session est un cookie de session — TR vérifie le cookie, pas un Bearer token
        request.Headers.Add("Cookie", $"tr_session={sessionToken}");

        var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

        if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403)
            throw new InvalidOperationException($"Session TR expirée ({(int)response.StatusCode}) — veuillez relancer la connexion.");

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"TR timeline HTTP {(int)response.StatusCode}: {body[..Math.Min(200, body.Length)]}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("items", out var items))
            return new List<TrCardTransaction>();

        var list = new List<TrCardTransaction>();
        foreach (var item in items.EnumerateArray())
        {
            var amount = item.TryGetProperty("amount", out var amt)
                ? amt.TryGetProperty("value", out var val) ? val.GetDecimal() : 0m
                : 0m;

            // Ignorer les items sans montant (séparateurs, headers de mois…)
            if (amount == 0m) continue;

            // Timestamp au format ISO 8601 (ex: "2026-03-02T23:12:12.429+0000")
            var txDate = item.TryGetProperty("timestamp", out var ts)
                ? DateTimeOffset.Parse(ts.GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind).UtcDateTime
                : DateTime.UtcNow;

            var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            var txId = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";

            list.Add(new TrCardTransaction { Id = txId, Amount = amount, Title = title, Date = txDate });
        }
        return list;
    }

    /// <summary>
    /// Récupère les transactions carte via WebSocket.
    /// Si authToken est fourni, l'envoie dans la souscription.
    /// Sinon, l'authentification est assurée par les cookies injectés lors de ConnectAsync.
    /// </summary>
    public async Task<List<TrCardTransaction>> GetCardTransactionsAsync(string? authToken = null, DateTime? since = null, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct, TimeSpan.FromSeconds(60));
        var id = NextId();

        object subscriptionPayload = string.IsNullOrEmpty(authToken)
            ? new { type = "timeline" }
            : (object)new { type = "timeline", token = authToken };

        await SendSubscriptionAsync(id, subscriptionPayload, timeoutCts.Token);

        var transactions = await ReadResponseAsync(id, timeoutCts.Token, root =>
        {
            if (!root.TryGetProperty("items", out var items))
                return new List<TrCardTransaction>();

            var list = new List<TrCardTransaction>();
            foreach (var item in items.EnumerateArray())
            {
                var eventType = item.TryGetProperty("eventType", out var et) ? et.GetString() : null;
                if (eventType != "CARD_TRANSACTION") continue;

                var txDate = item.TryGetProperty("timestamp", out var ts)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ts.GetInt64()).UtcDateTime
                    : DateTime.UtcNow;

                if (since.HasValue && txDate < since.Value) continue;

                var amount = item.TryGetProperty("amount", out var amt)
                    ? amt.TryGetProperty("value", out var val)
                        ? val.GetDecimal()
                        : 0m
                    : 0m;

                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var txId = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";

                list.Add(new TrCardTransaction
                {
                    Id = txId,
                    Amount = amount,
                    Title = title,
                    Date = txDate
                });
            }
            return list;
        }) ?? new List<TrCardTransaction>();

        await SendRawAsync($"unsub {id}", timeoutCts.Token);
        return transactions;
    }

    public string EncryptToken(string token) => _protector.Protect(token);
    public string DecryptToken(string encrypted) => _protector.Unprotect(encrypted);

    private int NextId() => ++_subscriptionId;

    private async Task SendSubscriptionAsync(int id, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("type", out var typeProp))
        {
            var messageType = typeProp.GetString();
            if (messageType == null || !AllowedMessageTypes.Contains(messageType))
                throw new InvalidOperationException($"Type de message WebSocket non autorisé : {messageType}");
        }

        await SendRawAsync($"sub {id} {json}", ct);
    }

    private async Task<T?> ReadResponseAsync<T>(int id, CancellationToken ct, Func<JsonElement, T?> parser) where T : class
    {
        var idPrefix = $"{id} ";

        while (!ct.IsCancellationRequested)
        {
            var response = await ReceiveAsync(ct);

            if (response.StartsWith("echo"))
            {
                if (EchoPattern.IsMatch(response))
                    await SendRawAsync(response, ct);
                continue;
            }

            if (response.StartsWith(idPrefix))
            {
                var payload = ParsePayload(response[idPrefix.Length..]);

                using var doc = JsonDocument.Parse(payload.Json);
                var root = doc.RootElement;

                if (payload.IsError || root.TryGetProperty("errors", out _))
                    throw new InvalidOperationException($"Erreur Trade Republic : {payload.Json}");

                var result = parser(root);
                if (result != null)
                    return result;
            }
        }

        throw new OperationCanceledException("Opération annulée (timeout).");
    }

    private async Task<T?> ReadResponseAsync<T>(int id, CancellationToken ct, Func<JsonElement, T?> parser) where T : struct
    {
        var idPrefix = $"{id} ";

        while (!ct.IsCancellationRequested)
        {
            var response = await ReceiveAsync(ct);

            if (response.StartsWith("echo"))
            {
                if (EchoPattern.IsMatch(response))
                    await SendRawAsync(response, ct);
                continue;
            }

            if (response.StartsWith(idPrefix))
            {
                var payload = ParsePayload(response[idPrefix.Length..]);

                using var doc = JsonDocument.Parse(payload.Json);
                var root = doc.RootElement;

                if (payload.IsError || root.TryGetProperty("errors", out _))
                    throw new InvalidOperationException($"Erreur Trade Republic : {payload.Json}");

                var result = parser(root);
                if (result.HasValue)
                    return result.Value;
            }
        }

        throw new OperationCanceledException("Opération annulée (timeout).");
    }

    /// <summary>
    /// Analyse le préfixe de type TR ("A " ou "E ") et retourne le JSON brut avec un flag d'erreur.
    /// </summary>
    private static (string Json, bool IsError) ParsePayload(string remainder)
    {
        if (remainder.StartsWith("A ")) return (remainder[2..], false);
        if (remainder.StartsWith("E ")) return (remainder[2..], true);
        return (remainder, false);
    }

    private async Task SendRawAsync(string message, CancellationToken ct)
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
            throw new InvalidOperationException("WebSocket non connecté.");

        var bytes = Encoding.UTF8.GetBytes(message);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
    }

    private async Task<string> ReceiveAsync(CancellationToken ct)
    {
        if (_ws == null)
            throw new InvalidOperationException("WebSocket non connecté.");

        var buffer = new byte[8192];
        var result = new StringBuilder();

        WebSocketReceiveResult wsResult;
        do
        {
            wsResult = await _ws.ReceiveAsync(buffer, ct);
            result.Append(Encoding.UTF8.GetString(buffer, 0, wsResult.Count));
        }
        while (!wsResult.EndOfMessage);

        return result.ToString();
    }

    private static CancellationTokenSource CreateTimeoutToken(CancellationToken ct, TimeSpan? timeout = null)
    {
        var timeoutCts = new CancellationTokenSource(timeout ?? OperationTimeout);
        return CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
    }

    public void Dispose()
    {
        if (_ws?.State == WebSocketState.Open)
        {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Fermeture", CancellationToken.None).GetAwaiter().GetResult(); }
            catch { /* Ignorer les erreurs à la fermeture */ }
        }
        _ws?.Dispose();
    }
}
