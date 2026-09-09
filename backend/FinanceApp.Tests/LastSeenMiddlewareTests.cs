using System.Security.Claims;
using FinanceApp.API.Data;
using FinanceApp.API.Models;
using FinanceApp.API.Services.Calendar;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FinanceApp.Tests;

/// <summary>User.LastSeenAt : une écriture au plus toutes les cinq minutes par utilisateur, jamais pour un anonyme, jamais bloquante.</summary>
public class LastSeenMiddlewareTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public LastSeenMiddlewareTests()
    {
        (_connection, _options) = TestHousehold.OpenInMemory();
    }

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() => new(_options);

    private static HttpContext Request(int? userId)
    {
        var ctx = new DefaultHttpContext();
        if (userId.HasValue)
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()) }, "Test"));
        return ctx;
    }

    private async Task<DateTime?> LastSeenAsync(int userId)
    {
        using var ctx = NewContext();
        return (await ctx.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).LastSeenAt;
    }

    [Fact]
    public async Task UtilisateurAuthentifie_EcritUneFois_PuisPasAvantCinqMinutes()
    {
        Household h;
        using (var ctx = NewContext()) h = await TestHousehold.SeedAsync(ctx, "seen@test.local");
        var clock = new FixedTimeProvider(AgendaTestSupport.Now);
        var suivant = 0;
        var middleware = new LastSeenMiddleware(_ => { suivant++; return Task.CompletedTask; }, clock);

        using (var ctx = NewContext()) await middleware.InvokeAsync(Request(h.UserId), ctx, NullLogger<LastSeenMiddleware>.Instance);
        Assert.Equal(AgendaTestSupport.Now.UtcDateTime, await LastSeenAsync(h.UserId));

        // Quatre minutes plus tard : pas d'écriture, la valeur ne bouge pas.
        clock.Now = AgendaTestSupport.Now.AddMinutes(4);
        using (var ctx = NewContext()) await middleware.InvokeAsync(Request(h.UserId), ctx, NullLogger<LastSeenMiddleware>.Instance);
        Assert.Equal(AgendaTestSupport.Now.UtcDateTime, await LastSeenAsync(h.UserId));

        // Cinq minutes : on écrit.
        clock.Now = AgendaTestSupport.Now.AddMinutes(5);
        using (var ctx = NewContext()) await middleware.InvokeAsync(Request(h.UserId), ctx, NullLogger<LastSeenMiddleware>.Instance);
        Assert.Equal(AgendaTestSupport.Now.AddMinutes(5).UtcDateTime, await LastSeenAsync(h.UserId));

        Assert.Equal(3, suivant);
    }

    [Fact]
    public async Task Anonyme_NEcritRien_EtPasseLaMain()
    {
        Household h;
        using (var ctx = NewContext()) h = await TestHousehold.SeedAsync(ctx, "anon@test.local");
        var suivant = 0;
        var middleware = new LastSeenMiddleware(_ => { suivant++; return Task.CompletedTask; }, new FixedTimeProvider(AgendaTestSupport.Now));

        using (var ctx = NewContext()) await middleware.InvokeAsync(Request(null), ctx, NullLogger<LastSeenMiddleware>.Instance);

        Assert.Null(await LastSeenAsync(h.UserId));
        Assert.Equal(1, suivant);
    }

    [Fact]
    public async Task DeuxUtilisateurs_ChacunSonCreneau()
    {
        Household a, b;
        using (var ctx = NewContext())
        {
            a = await TestHousehold.SeedAsync(ctx, "a@test.local");
            b = await TestHousehold.SeedAsync(ctx, "b@test.local");
        }
        var clock = new FixedTimeProvider(AgendaTestSupport.Now);
        var middleware = new LastSeenMiddleware(_ => Task.CompletedTask, clock);

        using (var ctx = NewContext()) await middleware.InvokeAsync(Request(a.UserId), ctx, NullLogger<LastSeenMiddleware>.Instance);
        clock.Now = AgendaTestSupport.Now.AddMinutes(2);
        using (var ctx = NewContext()) await middleware.InvokeAsync(Request(b.UserId), ctx, NullLogger<LastSeenMiddleware>.Instance);

        Assert.Equal(AgendaTestSupport.Now.UtcDateTime, await LastSeenAsync(a.UserId));
        Assert.Equal(AgendaTestSupport.Now.AddMinutes(2).UtcDateTime, await LastSeenAsync(b.UserId));
    }

    [Fact]
    public void Throttle_UnSeulGagnant_ParCreneau()
    {
        var throttle = new LastSeenThrottle();
        var t0 = AgendaTestSupport.Now.UtcDateTime;
        Assert.True(throttle.TryClaim(1, t0));
        Assert.False(throttle.TryClaim(1, t0.AddMinutes(4).AddSeconds(59)));
        Assert.True(throttle.TryClaim(1, t0.AddMinutes(5)));
        Assert.False(throttle.TryClaim(1, t0.AddMinutes(9)));
        Assert.True(throttle.TryClaim(2, t0));
    }
}
