using System.Net.Http.Json;

namespace Klods.Mobile.Services;

public class AuthService(HttpClient http)
{
    private const string TokenKey = "auth_token";
    private const string ServerKey = "server_url";

    public string? ServerUrl
    {
        get => Preferences.Get(ServerKey, null);
        set
        {
            if (value is not null)
                Preferences.Set(ServerKey, value);
            else
                Preferences.Remove(ServerKey);
        }
    }

    public async Task<bool> LoginAsync(string serverUrl, string username, string password)
    {
        ServerUrl = serverUrl.TrimEnd('/');
        var response = await http.PostAsJsonAsync(
            $"{ServerUrl}/api/auth/login",
            new { Username = username, Password = password });

        if (!response.IsSuccessStatusCode) return false;

        var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (result?.Token is null) return false;

        await SecureStorage.SetAsync(TokenKey, result.Token);
        return true;
    }

    public Task<string?> GetTokenAsync() => SecureStorage.GetAsync(TokenKey);

    public async Task<bool> IsAuthenticatedAsync() => await GetTokenAsync() is not null;

    public void Logout()
    {
        SecureStorage.Remove(TokenKey);
        ServerUrl = null;
    }

    private record TokenResponse(string Token);
}
