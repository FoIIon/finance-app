using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinanceApp.API.Services;

public class GoCardlessClient
{
    private readonly HttpClient _httpClient;
    private readonly string _secretId;
    private readonly string _secretKey;
    private readonly string _baseUrl;
    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public GoCardlessClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _secretId = configuration["GoCardless:SecretId"]!;
        _secretKey = configuration["GoCardless:SecretKey"]!;
        _baseUrl = configuration["GoCardless:BaseUrl"]!;
    }

    private async Task EnsureAccessTokenAsync()
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry)
            return;

        var payload = JsonSerializer.Serialize(new { secret_id = _secretId, secret_key = _secretKey });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}/token/new/", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        _accessToken = doc.RootElement.GetProperty("access").GetString();
        // Utiliser l'expiration réelle du token avec marge de sécurité
        var accessExpires = doc.RootElement.GetProperty("access_expires").GetInt64();
        _tokenExpiry = DateTimeOffset.FromUnixTimeSeconds(accessExpires).UtcDateTime.AddSeconds(-60);
    }

    private async Task<HttpRequestMessage> CreateAuthenticatedRequest(HttpMethod method, string url)
    {
        await EnsureAccessTokenAsync();
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        return request;
    }

    public async Task<JsonElement> GetInstitutionsAsync(string country)
    {
        var request = await CreateAuthenticatedRequest(HttpMethod.Get, $"{_baseUrl}/institutions/?country={country}");
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> CreateAgreementAsync(string institutionId)
    {
        var request = await CreateAuthenticatedRequest(HttpMethod.Post, $"{_baseUrl}/agreements/enduser/");
        var payload = JsonSerializer.Serialize(new
        {
            institution_id = institutionId,
            max_historical_days = 90,
            access_valid_for_days = 90,
            access_scope = new[] { "balances", "details", "transactions" }
        });
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> CreateRequisitionAsync(string institutionId, string agreementId, string redirect, string reference)
    {
        var request = await CreateAuthenticatedRequest(HttpMethod.Post, $"{_baseUrl}/requisitions/");
        var payload = JsonSerializer.Serialize(new
        {
            redirect,
            institution_id = institutionId,
            reference,
            agreement = agreementId,
            user_language = "FR"
        });
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> GetRequisitionAsync(string requisitionId)
    {
        var request = await CreateAuthenticatedRequest(HttpMethod.Get, $"{_baseUrl}/requisitions/{requisitionId}/");
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> GetAccountDetailsAsync(string accountId)
    {
        var request = await CreateAuthenticatedRequest(HttpMethod.Get, $"{_baseUrl}/accounts/{accountId}/details/");
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> GetBalancesAsync(string accountId)
    {
        var request = await CreateAuthenticatedRequest(HttpMethod.Get, $"{_baseUrl}/accounts/{accountId}/balances/");
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public async Task<JsonElement> GetTransactionsAsync(string accountId, DateTime? dateFrom = null, DateTime? dateTo = null)
    {
        var url = $"{_baseUrl}/accounts/{accountId}/transactions/";
        var queryParams = new List<string>();
        if (dateFrom.HasValue) queryParams.Add($"date_from={dateFrom.Value:yyyy-MM-dd}");
        if (dateTo.HasValue) queryParams.Add($"date_to={dateTo.Value:yyyy-MM-dd}");
        if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);

        var request = await CreateAuthenticatedRequest(HttpMethod.Get, url);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
