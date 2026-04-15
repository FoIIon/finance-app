using System.Collections.Concurrent;

namespace FinanceApp.API.Services;

public class PendingLogin
{
    public string ProcessId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class TradeRepublicAuthStore
{
    private readonly ConcurrentDictionary<int, PendingLogin> _pendingLogins = new();
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(5);

    public void Store(int connectionId, PendingLogin login)
    {
        Cleanup();

        // Un seul PendingLogin par utilisateur — supprimer l'ancien s'il existe
        foreach (var kvp in _pendingLogins)
        {
            if (kvp.Value.UserId == login.UserId)
                _pendingLogins.TryRemove(kvp.Key, out _);
        }

        _pendingLogins[connectionId] = login;
    }

    public PendingLogin? Get(int connectionId)
    {
        if (_pendingLogins.TryGetValue(connectionId, out var login))
        {
            if (DateTime.UtcNow - login.CreatedAt > Expiry)
            {
                _pendingLogins.TryRemove(connectionId, out _);
                return null;
            }
            return login;
        }
        return null;
    }

    public void Remove(int connectionId) =>
        _pendingLogins.TryRemove(connectionId, out _);

    private void Cleanup()
    {
        foreach (var kvp in _pendingLogins)
        {
            if (DateTime.UtcNow - kvp.Value.CreatedAt > Expiry)
                _pendingLogins.TryRemove(kvp.Key, out _);
        }
    }
}
