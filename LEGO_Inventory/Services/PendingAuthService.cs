namespace LEGO_Inventory.Services;

/// <summary>
/// Singleton that bridges the HTTP OAuth redirect back to the Blazor circuit.
/// After a successful OAuth callback the controller stores a short-lived token
/// here; the /auth/complete Blazor page consumes it to restore the session.
/// </summary>
public class PendingAuthService
{
    private readonly Dictionary<string, (int UserId, DateTime Expiry)> _pending = new();
    private readonly object _lock = new();

    public IReadOnlyList<string> EnabledProviders { get; init; } = [];

    public string Store(int userId)
    {
        var token = Guid.NewGuid().ToString("N");
        lock (_lock)
        {
            // Prune expired entries
            foreach (var key in _pending.Where(kv => kv.Value.Expiry < DateTime.UtcNow)
                                        .Select(kv => kv.Key).ToList())
                _pending.Remove(key);

            _pending[token] = (userId, DateTime.UtcNow.AddMinutes(5));
        }
        return token;
    }

    public int? Consume(string token)
    {
        lock (_lock)
        {
            if (_pending.TryGetValue(token, out var entry))
            {
                _pending.Remove(token);
                if (entry.Expiry > DateTime.UtcNow)
                    return entry.UserId;
            }
            return null;
        }
    }
}
