using System.Collections.Concurrent;
using System.Security.Claims;
using FinanceApp.API.Data;
using Microsoft.EntityFrameworkCore;

namespace FinanceApp.API.Services.Calendar;

/// <summary>
/// Décide si la présence d'un utilisateur mérite une écriture : au plus une toutes les
/// <see cref="MinInterval"/> par utilisateur. Pure et testable, mémoire de processus.
/// </summary>
public sealed class LastSeenThrottle
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<int, DateTime> _lastWrite = new();

    /// <summary>Vrai si l'appelant doit écrire maintenant. Réserve le créneau, un seul appelant gagne.</summary>
    public bool TryClaim(int userId, DateTime nowUtc)
    {
        while (true)
        {
            if (!_lastWrite.TryGetValue(userId, out var last))
            {
                if (_lastWrite.TryAdd(userId, nowUtc)) return true;
                continue;
            }
            if (nowUtc - last < MinInterval) return false;
            if (_lastWrite.TryUpdate(userId, nowUtc, last)) return true;
        }
    }
}

/// <summary>
/// Après l'authentification : pose User.LastSeenAt (UTC) au plus une fois toutes les cinq minutes par
/// utilisateur, en une seule UPDATE sans suivi. C'est la mesure du test d'usage : fiable et bon marché.
/// Un échec d'écriture ne fait jamais échouer la requête.
/// </summary>
public sealed class LastSeenMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TimeProvider _clock;
    private readonly LastSeenThrottle _throttle = new();

    public LastSeenMiddleware(RequestDelegate next, TimeProvider clock)
    {
        _next = next;
        _clock = clock;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db, ILogger<LastSeenMiddleware> logger)
    {
        if (context.User?.Identity?.IsAuthenticated == true
            && int.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            if (_throttle.TryClaim(userId, now))
            {
                try
                {
                    await db.Users
                        .Where(u => u.Id == userId)
                        .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, now), context.RequestAborted);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning("LastSeenAt non écrit pour l'utilisateur {UserId} : {Type}.", userId, ex.GetType().Name);
                }
            }
        }

        await _next(context);
    }
}
