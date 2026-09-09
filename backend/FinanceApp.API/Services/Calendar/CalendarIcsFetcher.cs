using System.Text;
using FinanceApp.API.Models;
using Microsoft.Extensions.Options;

namespace FinanceApp.API.Services.Calendar;

/// <summary>Issue d'un téléchargement : Ok avec le texte, ou un statut d'échec et une raison courte sans l'adresse.</summary>
public sealed record IcsFetchResult(CalendarSyncStatus Status, string? Ics, string? Error)
{
    public static IcsFetchResult Ok(string ics) => new(CalendarSyncStatus.Ok, ics, null);
    public static IcsFetchResult Fail(CalendarSyncStatus status, string error) => new(status, null, error);
}

public interface ICalendarIcsFetcher
{
    Task<IcsFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>
/// Télécharge un flux ICS par le client HTTP nommé <see cref="HttpClientName"/> (30 s, sans redirection,
/// sans aucun logger : l'adresse est un secret). Lecture en flux bornée à Calendar:MaxBytes, et les
/// premiers octets doivent être BEGIN:VCALENDAR. Aucun message d'exception n'est recopié : il pourrait
/// contenir l'adresse.
/// </summary>
public sealed class CalendarIcsFetcher : ICalendarIcsFetcher
{
    public const string HttpClientName = "CalendarIcsClient";
    private const string Prefix = "BEGIN:VCALENDAR";

    private readonly IHttpClientFactory _factory;
    private readonly IOptions<CalendarOptions> _options;

    public CalendarIcsFetcher(IHttpClientFactory factory, IOptions<CalendarOptions> options)
    {
        _factory = factory;
        _options = options;
    }

    public async Task<IcsFetchResult> FetchAsync(Uri url, CancellationToken cancellationToken)
    {
        var maxBytes = _options.Value.MaxBytes;
        var client = _factory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.ParseAdd("text/calendar");
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
                return IcsFetchResult.Fail(CalendarSyncStatus.HttpError, $"HTTP {(int)response.StatusCode}.");
            if (response.Content.Headers.ContentLength is { } announced && announced > maxBytes)
                return IcsFetchResult.Fail(CalendarSyncStatus.Invalid, "Flux trop volumineux.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var bytes = await ReadBoundedAsync(stream, maxBytes, cancellationToken);
            if (bytes == null) return IcsFetchResult.Fail(CalendarSyncStatus.Invalid, "Flux trop volumineux.");

            var text = Decode(bytes);
            if (!text.AsSpan().TrimStart().StartsWith(Prefix, StringComparison.Ordinal))
                return IcsFetchResult.Fail(CalendarSyncStatus.Invalid, "Le contenu n'est pas un flux iCalendar.");
            return IcsFetchResult.Ok(text);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return IcsFetchResult.Fail(CalendarSyncStatus.HttpError, "Délai dépassé.");
        }
        catch (HttpRequestException ex)
        {
            return IcsFetchResult.Fail(CalendarSyncStatus.HttpError,
                ex.StatusCode.HasValue ? $"HTTP {(int)ex.StatusCode.Value}." : "Serveur injoignable.");
        }
    }

    /// <summary>Lit au plus <paramref name="maxBytes"/> octets. Null si le flux en contient davantage.</summary>
    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static string Decode(byte[] bytes)
    {
        var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
    }
}
