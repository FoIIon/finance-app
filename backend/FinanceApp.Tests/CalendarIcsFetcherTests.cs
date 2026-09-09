using System.Net;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Calendar;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>Le téléchargeur réel derrière un handler simulé : statut, taille, premiers octets, redirection non suivie.</summary>
public class CalendarIcsFetcherTests
{
    private static readonly Uri Url = new("https://calendar.google.com/calendar/ical/x/private-SECRET/basic.ics");

    private static (CalendarIcsFetcher Fetcher, FakeHttpClientFactory Factory) Build(Func<HttpRequestMessage, HttpResponseMessage> respond, long? maxBytes = null)
    {
        var factory = new FakeHttpClientFactory(new FakeHttpHandler(respond));
        var options = AgendaTestSupport.Options(o => { if (maxBytes.HasValue) o.MaxBytes = maxBytes.Value; });
        return (new CalendarIcsFetcher(factory, Options.Create(options)), factory);
    }

    [Fact]
    public async Task Flux200_QuiCommenceParBeginVcalendar_EstOk_ParLeClientNomme()
    {
        var (fetcher, factory) = Build(_ => AgendaTestSupport.Response(HttpStatusCode.OK, "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nEND:VCALENDAR\r\n"));
        var result = await fetcher.FetchAsync(Url, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.Ok, result.Status);
        Assert.StartsWith("BEGIN:VCALENDAR", result.Ics);
        Assert.Equal(CalendarIcsFetcher.HttpClientName, factory.LastName);
    }

    [Fact]
    public async Task BomUtf8_EnTete_EstTolere()
    {
        var (fetcher, _) = Build(_ => AgendaTestSupport.Response(HttpStatusCode.OK, "﻿BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n"));
        var result = await fetcher.FetchAsync(Url, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.Ok, result.Status);
        Assert.StartsWith("BEGIN:VCALENDAR", result.Ics);
    }

    [Fact]
    public async Task Contenu200_QuiNEstPasUnCalendrier_EstInvalid()
    {
        var (fetcher, _) = Build(_ => AgendaTestSupport.Response(HttpStatusCode.OK, "<html><body>Connexion requise</body></html>"));
        var result = await fetcher.FetchAsync(Url, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.Invalid, result.Status);
        Assert.Null(result.Ics);
        Assert.DoesNotContain("SECRET", result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "HTTP 404.")]
    [InlineData(HttpStatusCode.Forbidden, "HTTP 403.")]
    [InlineData(HttpStatusCode.Found, "HTTP 302.")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500.")]
    public async Task StatutAutreQue200_EstHttpError_AvecLeCodeSeul(HttpStatusCode code, string expected)
    {
        var (fetcher, _) = Build(_ =>
        {
            var r = AgendaTestSupport.Response(code, "BEGIN:VCALENDAR\r\nEND:VCALENDAR\r\n");
            if (code == HttpStatusCode.Found) r.Headers.Location = new Uri("https://calendar.google.com/ailleurs-SECRET");
            return r;
        });
        var result = await fetcher.FetchAsync(Url, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.HttpError, result.Status);
        Assert.Equal(expected, result.Error);
    }

    [Fact]
    public async Task CorpsPlusGrandQueMaxBytes_EstInvalid_SansContenu()
    {
        var gros = "BEGIN:VCALENDAR\r\n" + new string('x', 500) + "\r\nEND:VCALENDAR\r\n";
        var (fetcher, _) = Build(_ => AgendaTestSupport.Response(HttpStatusCode.OK, gros), maxBytes: 100);
        var result = await fetcher.FetchAsync(Url, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.Invalid, result.Status);
        Assert.Equal("Flux trop volumineux.", result.Error);
        Assert.Null(result.Ics);
    }

    [Fact]
    public async Task ContentLengthAnnonceTropGrand_EstInvalid_SansLireLeCorps()
    {
        var (fetcher, _) = Build(_ => AgendaTestSupport.Response(HttpStatusCode.OK, "BEGIN:VCALENDAR", contentLength: 50_000_000));
        var result = await fetcher.FetchAsync(Url, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.Invalid, result.Status);
        Assert.Equal("Flux trop volumineux.", result.Error);
    }

    [Fact]
    public async Task ReseauInjoignable_EstHttpError_SansMessageDException()
    {
        var (fetcher, _) = Build(_ => throw new HttpRequestException("No such host is known (private-SECRET)"));
        var result = await fetcher.FetchAsync(Url, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.HttpError, result.Status);
        Assert.Equal("Serveur injoignable.", result.Error);
    }

    /// <summary>Un corps qui se coupe pendant la lecture, après des en-têtes 200 corrects.</summary>
    private sealed class FluxCoupe : Stream
    {
        private bool _first = true;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_first)
            {
                _first = false;
                var head = "BEGIN:VCALENDAR\r\n"u8;
                head.CopyTo(buffer.AsSpan(offset));
                return head.Length;
            }
            throw new IOException("The response ended prematurely (private-SECRET).");
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task FluxCoupePendantLaLecture_EstHttpError_SansMessageDException()
    {
        var (fetcher, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new FluxCoupe()) });
        var result = await fetcher.FetchAsync(Url, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.HttpError, result.Status);
        Assert.Equal("Connexion interrompue.", result.Error);
        Assert.Null(result.Ics);
    }

    [Fact]
    public async Task DelaiDepasse_EstHttpError()
    {
        var (fetcher, _) = Build(_ => throw new TaskCanceledException("timeout"));
        var result = await fetcher.FetchAsync(Url, CancellationToken.None);
        Assert.Equal(CalendarSyncStatus.HttpError, result.Status);
        Assert.Equal("Délai dépassé.", result.Error);
    }

    [Fact]
    public async Task AnnulationDemandee_Remonte()
    {
        using var cts = new CancellationTokenSource();
        var (fetcher, _) = Build(_ => { cts.Cancel(); throw new TaskCanceledException(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fetcher.FetchAsync(Url, cts.Token));
    }
}
