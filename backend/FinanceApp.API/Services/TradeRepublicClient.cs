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
    private readonly string _webAppVersion;
    private readonly string _deviceId;

    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);

    // Liste blanche des types de messages WebSocket autorisés (auth et refresh désormais via HTTP)
    private static readonly HashSet<string> AllowedMessageTypes = new()
    {
        "timeline",
        "compactPortfolioByType",
        "instrument",
        "ticker",
        "aggregateHistoryLight",
        "availableCash",
        "cash"
    };

    private static readonly Regex EchoPattern = new(@"^echo \d+$", RegexOptions.Compiled);

    public TradeRepublicClient(
        IConfiguration configuration,
        IDataProtectionProvider dataProtection,
        ILogger<TradeRepublicClient> logger,
        HttpClient httpClient)
    {
        _webSocketUrl = configuration["TradeRepublic:WebSocketUrl"] ?? "wss://api.traderepublic.com/";
        _webAppVersion = configuration["TradeRepublic:WebAppVersion"] ?? "15.7.0";
        // Identifiant d'appareil : TR lie le processus de login v2 à un stableDeviceId.
        // Il doit rester constant d'un appel à l'autre pour la même connexion. Configurable
        // pour être fixé une fois la connexion établie, sinon dérivé de façon stable.
        _deviceId = configuration["TradeRepublic:DeviceId"]
            ?? "financeapp0000000000000000000000financeapp0000000000000000000000";
        _protector = dataProtection.CreateProtector("TradeRepublic.Tokens");
        _logger = logger;
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.traderepublic.com");
        _httpClient.DefaultRequestHeaders.Add(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36");
    }

    /// <summary>
    /// Initie le login. Flux v2 (2026) : plus de code SMS, l'utilisateur approuve la
    /// connexion par une notification push dans l'app mobile Trade Republic. L'ancien
    /// flux v1 répond 405/426 depuis que TR a posé AWS WAF devant le login.
    /// La réponse complète est journalisée : l'API n'est pas documentée, sa forme
    /// s'observe (exploration lot 4).
    /// </summary>
    public async Task<string> InitiateLoginAsync(string phoneNumber, string pin, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct);
        var payload = JsonSerializer.Serialize(new { phoneNumber, pin });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v2/auth/web/login") { Content = content };
        AddBrowserHeaders(request);
        AddTrV2Headers(request);

        var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);

        _logger.LogInformation("TR login v2: HTTP {status}, body: {body}",
            (int)response.StatusCode, json.Length > 500 ? json[..500] : json);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Erreur Trade Republic ({(int)response.StatusCode}) : {json}");

        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("processId").GetString()!;
    }

    /// <summary>
    /// Headers qu'un vrai navigateur enverrait depuis app.traderepublic.com. Le WAF de TR
    /// s'en sert pour distinguer un client web d'un script : sans eux, le login est rejeté
    /// avant même d'être examiné.
    /// </summary>
    private static void AddBrowserHeaders(HttpRequestMessage request)
    {
        request.Headers.Add("Origin", "https://app.traderepublic.com");
        request.Headers.Add("Referer", "https://app.traderepublic.com/");
        request.Headers.Add("Sec-Fetch-Site", "same-site");
        request.Headers.Add("Sec-Fetch-Mode", "cors");
        request.Headers.Add("Sec-Fetch-Dest", "empty");
        request.Headers.Add("Accept", "application/json");
        request.Headers.Add("Accept-Language", "fr-BE,fr;q=0.9,en;q=0.8");
    }

    /// <summary>
    /// Headers propres au flux v2 (2026). TR lie le processus de login à un identifiant
    /// d'appareil stable transmis en base64 dans x-tr-device-info, et exige la plateforme
    /// et la version de l'app web. Leur absence provoque MISSING_REQUIRED_HEADER.
    /// </summary>
    private void AddTrV2Headers(HttpRequestMessage request)
    {
        request.Headers.Add("x-tr-platform", "web");
        request.Headers.Add("x-tr-app-version", _webAppVersion);
        request.Headers.Add("x-tr-device-info", BuildDeviceInfo(_deviceId));
    }

    private static string BuildDeviceInfo(string deviceId)
    {
        var payload = new
        {
            stableDeviceId = deviceId,
            model = "Apple Macintosh",
            browser = "Chrome",
            browserVersion = "148.0.0.0",
            os = "Mac OS",
            osVersion = "10.15.7",
            timezone = "Europe/Brussels",
            timezoneOffset = -120,
            screen = "1800x1169x30",
            preferredLanguages = new[] { "fr", "fr-BE" },
            numberOfCores = 12,
            deviceMemory = 16,
        };
        var raw = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        return Convert.ToBase64String(raw);
    }

    /// <summary>
    /// Confirme le deuxième facteur (flux v2 : le code accompagne l'approbation mobile).
    /// TR retourne les tokens dans des cookies Set-Cookie (pas dans le body).
    /// </summary>
    public async Task<(string SessionToken, string RefreshToken, string DeviceToken)> ConfirmTwoFactorAsync(string processId, string code, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct);

        var confirmRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/v2/auth/web/login/{processId}/{code}");
        AddBrowserHeaders(confirmRequest);
        var response = await _httpClient.SendAsync(confirmRequest, timeoutCts.Token);

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
    /// Flux v2 : après approbation dans l'app mobile, TR ne renvoie plus de code. On interroge
    /// l'état du processus jusqu'à ce qu'il pose les cookies de session (tr_session, tr_refresh,
    /// tr_device) dans Set-Cookie. Boucle bornée : l'utilisateur peut approuver pendant l'appel.
    /// </summary>
    public async Task<(string SessionToken, string RefreshToken, string DeviceToken)> PollLoginApprovalV2Async(
        string processId, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(90);

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v2/auth/web/login/processes/{processId}");
            AddBrowserHeaders(request);
            AddTrV2Headers(request);

            var response = await _httpClient.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            _logger.LogInformation("TR poll v2: HTTP {status}, body: {body}",
                (int)response.StatusCode, body.Length > 300 ? body[..300] : body);

            if (response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
            {
                var cookies = cookieHeaders.ToList();
                var refresh = ExtractCookieValue(cookies, "tr_refresh");
                if (refresh != null)
                {
                    return (
                        ExtractCookieValue(cookies, "tr_session") ?? "",
                        refresh,
                        ExtractCookieValue(cookies, "tr_device") ?? "");
                }
            }

            if ((int)response.StatusCode is 401 or 403 or 404 or 410)
                throw new InvalidOperationException(
                    $"Processus de connexion expiré ({(int)response.StatusCode}). Relance la connexion et approuve plus vite.");

            await Task.Delay(2000, ct);
        }

        throw new InvalidOperationException(
            "Approbation Trade Republic non reçue à temps. Ouvre l'app TR, approuve la demande, puis réessaie.");
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
    /// Renouvelle la session Trade Republic.
    ///
    /// L'ancien <c>POST /api/v1/auth/web/refresh</c> répond <c>405 Method Not Allowed</c> depuis
    /// que TR a posé un WAF devant son API v1 (constaté le 23/08/2026, reconfirmé le 25/08).
    /// Le mécanisme réel est le keepalive utilisé par les clients de la communauté :
    /// <c>GET /api/v1/auth/web/session</c> avec les cookies <c>tr_refresh</c> et <c>tr_device</c>,
    /// qui fait tourner le cookie <c>tr_session</c>. Vérifié par pré-vol CORS le 25/08 :
    /// <c>OPTIONS</c> sur cette route renvoie <c>access-control-allow-methods: GET</c> et
    /// <c>access-control-allow-credentials: true</c>.
    ///
    /// Une session vit environ cinq minutes, ce renouvellement est donc le chemin normal,
    /// pas un rattrapage d'exception.
    /// </summary>
    public async Task<string> RefreshSessionAsync(string refreshToken, string deviceToken, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/web/session");
        AddBrowserHeaders(request);
        AddTrV2Headers(request);

        var cookies = new List<string>();
        if (!string.IsNullOrEmpty(refreshToken)) cookies.Add($"tr_refresh={refreshToken}");
        if (!string.IsNullOrEmpty(deviceToken)) cookies.Add($"tr_device={deviceToken}");
        if (cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", cookies));

        var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        var responseBody = await response.Content.ReadAsStringAsync(timeoutCts.Token);

        // Jamais le corps : le code va justement y chercher un sessionToken vivant vingt
        // lignes plus bas, et une ligne de journal annulerait le chiffrement fait ensuite.
        _logger.LogInformation("TR refresh session : HTTP {status}, {taille} octets de réponse.",
            (int)response.StatusCode, responseBody.Length);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Erreur renouvellement de session ({(int)response.StatusCode}) : {responseBody}");

        // Cas 1 : le nouveau tr_session arrive en Set-Cookie, c'est le cas nominal du keepalive.
        if (response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
        {
            var sessionFromCookie = ExtractCookieValue(cookieHeaders.ToList(), "tr_session");
            if (sessionFromCookie != null) return sessionFromCookie;
        }

        // Cas 2 : certaines réponses portent le jeton dans le corps.
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("sessionToken", out var sessionProp))
                return sessionProp.GetString()
                    ?? throw new InvalidOperationException("sessionToken vide dans la réponse de renouvellement.");
        }

        throw new InvalidOperationException(
            "Renouvellement accepté mais aucun jeton de session dans la réponse. Body : "
            + responseBody[..Math.Min(200, responseBody.Length)]);
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

    /// <summary>
    /// Sonde un chemin REST arbitraire avec la session courante et retourne la réponse brute.
    /// Exploration uniquement (lot 4) : l'API TR n'est pas documentée, la forme des réponses
    /// s'observe avant de s'écrire. Exposé par un endpoint réservé à l'environnement Development.
    /// </summary>
    public async Task<(int Status, string Body)> ProbeRestAsync(string sessionToken, string path, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct);

        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", $"tr_session={sessionToken}");

        var response = await _httpClient.SendAsync(request, timeoutCts.Token);
        var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        return ((int)response.StatusCode, body);
    }

    /// <summary>
    /// Sonde une souscription WebSocket arbitraire et retourne la première réponse brute.
    /// Contourne volontairement la liste blanche : exploration uniquement, jamais atteint
    /// en production (endpoint gardé par l'environnement). Nécessite ConnectAsync au préalable.
    /// </summary>
    public async Task<string> ProbeSubscriptionAsync(string payloadJson, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct);
        var id = NextId();
        await SendRawAsync($"sub {id} {payloadJson}", timeoutCts.Token);

        var idPrefix = $"{id} ";
        while (!timeoutCts.Token.IsCancellationRequested)
        {
            var response = await ReceiveAsync(timeoutCts.Token);

            if (response.StartsWith("echo"))
            {
                if (EchoPattern.IsMatch(response))
                    await SendRawAsync(response, timeoutCts.Token);
                continue;
            }

            if (response.StartsWith(idPrefix))
            {
                await SendRawAsync($"unsub {id}", timeoutCts.Token);
                return response[idPrefix.Length..];
            }
        }

        throw new OperationCanceledException("Sonde WebSocket sans réponse (timeout).");
    }

    /// <summary>
    /// Snapshot d'une position enrichie de son cours courant. MarketValue est null quand le
    /// cours n'a pas pu être récupéré : la position reste importable (quantité, prix de revient),
    /// seule sa valorisation du jour manque. Une valeur absente vaut mieux qu'une valeur inventée.
    /// </summary>
    public record TrPortfolioSnapshot(
        TrPortfolioPosition Position,
        decimal? CurrentPrice,
        decimal? MarketValue,
        IReadOnlyList<TradeRepublicPortfolioParser.TrPricePoint> PriceHistory);

    /// <summary>
    /// Récupère le portefeuille complet en une session WebSocket : positions
    /// (compactPortfolioByType), puis pour chaque ligne le cours courant via son instrument
    /// (place de cotation) et son ticker. La connexion s'authentifie par les cookies
    /// (refresh + device), chaque souscription porte le session token.
    /// </summary>
    public record TrPortfolioImport(List<TrPortfolioSnapshot> Positions, decimal? CashBalance);

    public async Task<TrPortfolioImport> ImportPortfolioSnapshotAsync(
        string sessionToken, string refreshToken, string deviceToken, CancellationToken ct = default)
    {
        using var timeoutCts = CreateTimeoutToken(ct, TimeSpan.FromSeconds(120));
        await ConnectAsync(refreshToken, deviceToken, timeoutCts.Token);

        var positionsJson = await SubscribeOnceRawAsync(
            new { type = "compactPortfolioByType", token = sessionToken }, timeoutCts.Token);
        var positions = TradeRepublicPortfolioParser.ParsePositions(positionsJson);

        // Solde espèces : le portefeuille ne contient que des positions, aucune catégorie
        // de liquidités. Un échec ici ne doit pas faire perdre l'import des positions.
        decimal? cash = null;
        try
        {
            var cashJson = await SubscribeOnceRawAsync(
                new { type = "availableCash", token = sessionToken }, timeoutCts.Token);
            cash = TradeRepublicPortfolioParser.ParseCashBalance(cashJson);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("TR solde espèces indisponible : {message}", ex.Message);
        }

        // Plage d'historique : « 1y » était codé en dur, d'où un « Max » qui ne dépassait
        // jamais un an. On retient la plus longue que Trade Republic accepte réellement,
        // déterminée sur la première position puis réutilisée pour les suivantes.
        string? plageRetenue = null;

        var result = new List<TrPortfolioSnapshot>(positions.Count);
        foreach (var position in positions)
        {
            decimal? price = null;
            IReadOnlyList<TradeRepublicPortfolioParser.TrPricePoint> history = [];
            try
            {
                var instrumentJson = await SubscribeOnceRawAsync(
                    new { type = "instrument", id = position.Isin, token = sessionToken }, timeoutCts.Token);
                var exchange = TradeRepublicPortfolioParser.ParseFirstExchange(instrumentJson);

                if (!string.IsNullOrEmpty(exchange))
                {
                    var tickerJson = await SubscribeOnceRawAsync(
                        new { type = "ticker", id = $"{position.Isin}.{exchange}", token = sessionToken }, timeoutCts.Token);
                    price = TradeRepublicPortfolioParser.ParseTickerLastPrice(tickerJson);

                    // Historique de cours sur un an. Un échec ici ne doit pas faire perdre
                    // la valorisation du jour, déjà obtenue.
                    // La plage retenue passe en tête, mais le repli reste disponible : une
                    // ligne récente peut refuser « max » que la première position acceptait.
                    var plagesAEssayer = plageRetenue is null
                        ? new[] { "max", "5y", "1y" }
                        : new[] { plageRetenue }.Concat(new[] { "max", "5y", "1y" }.Where(p => p != plageRetenue)).ToArray();

                    foreach (var plage in plagesAEssayer)
                    {
                        try
                        {
                            var historyJson = await SubscribeOnceRawAsync(
                                new
                                {
                                    type = "aggregateHistoryLight",
                                    id = $"{position.Isin}.{exchange}",
                                    range = plage,
                                    token = sessionToken
                                },
                                timeoutCts.Token);

                            var serie = TradeRepublicPortfolioParser.ParsePriceHistory(historyJson);
                            if (serie.Count == 0) continue;

                            history = serie;
                            if (plageRetenue is null)
                            {
                                plageRetenue = plage;
                                _logger.LogInformation(
                                    "TR historique : plage {plage} retenue, {points} points depuis le {debut:yyyy-MM-dd}.",
                                    plage, serie.Count, serie[0].AsOf);
                            }
                            break;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogInformation("TR historique {plage} refusé pour {id} : {message}",
                                plage, $"{position.Isin}.{exchange}", ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Un cours manquant ne doit pas faire échouer tout l'import.
                _logger.LogWarning("TR cours indisponible pour {isin}: {message}", position.Isin, ex.Message);
            }

            var marketValue = price.HasValue ? price.Value * position.Quantity : (decimal?)null;
            result.Add(new TrPortfolioSnapshot(position, price, marketValue, history));
        }

        return new TrPortfolioImport(result, cash);
    }

    /// <summary>Souscrit un topic, lit la première réponse, se désabonne, et renvoie le JSON brut.</summary>
    private async Task<string> SubscribeOnceRawAsync(object payload, CancellationToken ct)
    {
        var id = NextId();
        await SendSubscriptionAsync(id, payload, ct);
        var json = await ReadResponseAsync(id, ct, root => root.GetRawText());
        await SendRawAsync($"unsub {id}", ct);
        return json ?? "{}";
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

                // TryGetProperty lève sur un tableau au lieu de rendre faux : sans ce garde,
                // tout topic répondant par une liste (les soldes espèces, par exemple)
                // remontait « requires an element of type Object ».
                if (payload.IsError
                    || (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out _)))
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

                // TryGetProperty lève sur un tableau au lieu de rendre faux : sans ce garde,
                // tout topic répondant par une liste (les soldes espèces, par exemple)
                // remontait « requires an element of type Object ».
                if (payload.IsError
                    || (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out _)))
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
