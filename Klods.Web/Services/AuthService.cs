using System.Text;
using System.Text.Json;

namespace Klods.Services;

/// <summary>
/// Scoped per-circuit auth state. Stores the JWT and decoded user profile.
/// Does not touch the database — all persistence goes through the API.
/// </summary>
public class AuthService
{
    public UserInfo? CurrentUser { get; private set; }
    public string? Token { get; private set; }
    public bool IsSessionRestored { get; private set; }

    public event Action? OnChange;
    public event Action? SessionRestored;

    public void SetSession(UserInfo user, string token)
    {
        CurrentUser = user;
        Token = token;
        OnChange?.Invoke();
    }

    public void UpdateCurrentUser(UserInfo user)
    {
        CurrentUser = user;
        OnChange?.Invoke();
    }

    public void Logout()
    {
        CurrentUser = null;
        Token = null;
        OnChange?.Invoke();
    }

    public void MarkSessionRestored()
    {
        IsSessionRestored = true;
        SessionRestored?.Invoke();
    }

    /// <summary>
    /// Decodes the JWT payload without signature verification.
    /// Used only to bootstrap the token before the API profile call confirms identity.
    /// </summary>
    public static Dictionary<string, string> ParseJwtClaims(string jwt)
    {
        var parts = jwt.Split('.');
        if (parts.Length != 3) return [];
        var base64 = parts[1].Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)?
            .ToDictionary(kv => kv.Key, kv => kv.Value.ToString()) ?? [];
    }
}
