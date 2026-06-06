using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Klods.Mobile.Services;

public class ApiClient(HttpClient http, AuthService auth)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<HttpClient> GetHttpAsync()
    {
        var server = auth.ActiveServer ?? throw new InvalidOperationException("No active server.");
        var token = await auth.GetTokenAsync(server.Id);
        http.DefaultRequestHeaders.Authorization =
            token is not null ? new AuthenticationHeaderValue("Bearer", token) : null;
        return http;
    }

    private string Base => auth.ActiveServer!.Url.TrimEnd('/');

    private async Task<T?> GetAsync<T>(string path)
    {
        try
        {
            var client = await GetHttpAsync();
            var resp = await client.GetAsync($"{Base}{path}");
            return resp.IsSuccessStatusCode
                ? await resp.Content.ReadFromJsonAsync<T>(JsonOpts)
                : default;
        }
        catch { return default; }
    }

    private async Task<bool> SendAsync(HttpMethod method, string path, object? body = null)
    {
        try
        {
            var client = await GetHttpAsync();
            var req = new HttpRequestMessage(method, $"{Base}{path}");
            if (body is not null)
                req.Content = JsonContent.Create(body, options: JsonOpts);
            var resp = await client.SendAsync(req);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private Task<bool> PatchAsync(string path, object? body = null) => SendAsync(HttpMethod.Patch, path, body);
    private Task<bool> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path);

    // ── Profile ───────────────────────────────────────────────────────────────

    public Task<UserProfileDto?> GetMyProfileAsync() =>
        GetAsync<UserProfileDto>("/api/auth/me");

    public Task<LinkedLoginDto[]?> GetLinkedLoginsAsync() =>
        GetAsync<LinkedLoginDto[]>("/api/auth/me/logins");

    public Task<bool> ChangePasswordAsync(string current, string newPassword) =>
        PatchAsync("/api/auth/me/password", new { CurrentPassword = current, NewPassword = newPassword });

    public Task<bool> ChangePictureAsync(string? url) =>
        PatchAsync("/api/auth/me/picture", new { Url = url });

    public Task<bool> ChangeThemeAsync(string? color) =>
        PatchAsync("/api/auth/me/theme", new { Color = color });

    public Task<bool> UnlinkLoginAsync(string provider) =>
        DeleteAsync($"/api/auth/me/logins/{Uri.EscapeDataString(provider)}");

    // ── Sets ─────────────────────────────────────────────────────────────────

    public Task<MyOwnedSetDto[]?> GetMyOwnedSetsAsync() =>
        GetAsync<MyOwnedSetDto[]>("/api/sets/my-owned");

    // Mirrors ImageStorageService.ResolveUrl but turns server-relative paths into absolute URLs
    // using the active server's base URL, since the mobile app has no implicit origin.
    public string? ResolveImageUrl(string? stored) =>
        stored is null ? null :
        stored.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? stored :
        $"{Base}/media/{stored}";

    // ── DTOs ─────────────────────────────────────────────────────────────────

    public record UserProfileDto(
        int UserId,
        string UserName,
        string Role,
        string? ProfilePictureUrl,
        string? PrimaryColor,
        bool HasPassword);

    public record LinkedLoginDto(string Provider);

    public record OwnedInstanceDto(int SetIndex, int MissingPieceCount, int StockCount);

    public record MyOwnedSetDto(
        string SetId, string Name, string? SetImg, int NumBricks,
        int ReleaseYear, string? ThemeName, string ManualUrl,
        List<OwnedInstanceDto> Instances);
}
