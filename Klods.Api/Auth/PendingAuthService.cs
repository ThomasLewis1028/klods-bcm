namespace Klods.Api.Auth;

/// <summary>
/// Bridges the HTTP OAuth redirect back to whatever client is waiting (Blazor circuit, mobile deep link, etc.).
/// Stores a short-lived one-time token mapped to the user's JWT; the client exchanges it via /api/auth/exchange/{token}.
/// Also manages link-intent tokens: a logged-in user requests one before starting an OAuth link flow so the
/// callback can resolve the user ID without trusting user-supplied query parameters.
/// </summary>
public class PendingAuthService
{
    private readonly Dictionary<string, (string Jwt, DateTime Expiry)> _pending = new();
    private readonly Dictionary<string, (int UserId, DateTime Expiry)> _linkIntents = new();
    private readonly object _lock = new();

    public IReadOnlyList<string> EnabledProviders { get; init; } = [];

    public string Store(string jwt)
    {
        var token = Guid.NewGuid().ToString("N");
        lock (_lock)
        {
            foreach (var key in _pending.Where(kv => kv.Value.Expiry < DateTime.UtcNow)
                                        .Select(kv => kv.Key).ToList())
                _pending.Remove(key);

            _pending[token] = (jwt, DateTime.UtcNow.AddMinutes(5));
        }
        return token;
    }

    public string? Consume(string token)
    {
        lock (_lock)
        {
            if (_pending.TryGetValue(token, out var entry))
            {
                _pending.Remove(token);
                if (entry.Expiry > DateTime.UtcNow)
                    return entry.Jwt;
            }
            return null;
        }
    }

    /// <summary>Creates a short-lived token that proves the caller is allowed to link an OAuth account to <paramref name="userId"/>.</summary>
    public string StoreLinkIntent(int userId)
    {
        var token = Guid.NewGuid().ToString("N");
        lock (_lock)
        {
            foreach (var key in _linkIntents.Where(kv => kv.Value.Expiry < DateTime.UtcNow)
                                            .Select(kv => kv.Key).ToList())
                _linkIntents.Remove(key);

            _linkIntents[token] = (userId, DateTime.UtcNow.AddMinutes(5));
        }
        return token;
    }

    /// <summary>Consumes a link-intent token and returns the associated user ID, or null if expired/invalid.</summary>
    public int? ConsumeLinkIntent(string token)
    {
        lock (_lock)
        {
            if (_linkIntents.TryGetValue(token, out var entry))
            {
                _linkIntents.Remove(token);
                if (entry.Expiry > DateTime.UtcNow)
                    return entry.UserId;
            }
            return null;
        }
    }
}
