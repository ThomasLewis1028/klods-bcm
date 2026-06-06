using System.Net.Http.Json;
using Klods.Mobile.Models;

namespace Klods.Mobile.Services;

public class AuthService(HttpClient http)
{
    public ServerProfile? ActiveServer { get; private set; }

    private static string TokenKey(string serverId) => $"token_{serverId}";

    public async Task<bool> LoginAsync(ServerProfile server, string username, string password)
    {
        var response = await http.PostAsJsonAsync(
            $"{server.Url.TrimEnd('/')}/api/auth/login",
            new { Username = username, Password = password });

        if (!response.IsSuccessStatusCode) return false;
        var result = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (result?.Token is null) return false;

        await SecureStorage.SetAsync(TokenKey(server.Id), result.Token);
        ActiveServer = server;
        return true;
    }

    public async Task<bool> TryResumeAsync(ServerProfile server)
    {
        var token = await GetTokenAsync(server.Id);
        if (token is null) return false;
        ActiveServer = server;
        return true;
    }

    public async Task<string?> GetTokenAsync(string serverId)
    {
        try { return await SecureStorage.GetAsync(TokenKey(serverId)); }
        catch { SecureStorage.Remove(TokenKey(serverId)); return null; }
    }

    public async Task<bool> IsAuthenticatedAsync(string serverId) =>
        await GetTokenAsync(serverId) is not null;

    public void Logout(string serverId)
    {
        SecureStorage.Remove(TokenKey(serverId));
        if (ActiveServer?.Id == serverId) ActiveServer = null;
    }

    private record TokenResponse(string Token);
}
