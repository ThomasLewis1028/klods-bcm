namespace LEGO_Inventory.Api.Auth;

/// <summary>
/// Bridges the HTTP OAuth redirect back to whatever client is waiting (Blazor circuit, mobile deep link, etc.).
/// Stores a short-lived one-time token mapped to the user's JWT; the client exchanges it via /api/auth/exchange/{token}.
/// </summary>
public class PendingAuthService
{
    private readonly Dictionary<string, (string Jwt, DateTime Expiry)> _pending = new();
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
}
