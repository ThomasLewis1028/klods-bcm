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

    private static string BuildUrl(string path, string? search, int page, int pageSize)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
            parts.Add($"search={Uri.EscapeDataString(search)}");
        if (page > 0)
            parts.Add($"page={page}");
        if (pageSize > 0)
            parts.Add($"pageSize={pageSize}");
        return parts.Count == 0 ? path : $"{path}?{string.Join('&', parts)}";
    }

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

    private async Task<T?> PostAsync<T>(string path, object? body = null)
    {
        try
        {
            var client = await GetHttpAsync();
            var req = new HttpRequestMessage(HttpMethod.Post, $"{Base}{path}");
            if (body is not null)
                req.Content = JsonContent.Create(body, options: JsonOpts);
            var resp = await client.SendAsync(req);
            return resp.IsSuccessStatusCode
                ? await resp.Content.ReadFromJsonAsync<T>(JsonOpts)
                : default;
        }
        catch { return default; }
    }

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

    public Task<PagedResult<MyOwnedSetDto>?> GetMyOwnedSetsAsync(string? search = null, int page = 0, int pageSize = 0) =>
        GetAsync<PagedResult<MyOwnedSetDto>>(BuildUrl("/api/sets/my-owned", search, page, pageSize));

    // ── Bricks ───────────────────────────────────────────────────────────────

    public Task<PagedResult<MyBrickDto>?> GetOwnedBricksAsync(string? search = null, int page = 0, int pageSize = 0) =>
        GetAsync<PagedResult<MyBrickDto>>(BuildUrl("/api/mybricks", search, page, pageSize));

    public Task<bool> UpdateLooseBrickStockAsync(string partNum, string colorId, int stock) =>
        SendAsync(HttpMethod.Put,
            $"/api/mybricks/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}/stock",
            new { Stock = stock });

    // Returns sets the user owns that require this brick (user-scoped).
    public Task<MyBrickSetDetailDto[]?> GetBrickSetsAsync(string partNum, string colorId) =>
        GetAsync<MyBrickSetDetailDto[]>($"/api/mybricks/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}/sets");

    // ── Minifigs ──────────────────────────────────────────────────────────────

    public Task<PagedResult<MyMinifigDto>?> GetMyMinifigsAsync(string? search = null, int page = 0, int pageSize = 0) =>
        GetAsync<PagedResult<MyMinifigDto>>(BuildUrl("/api/myminifigs", search, page, pageSize));

    public Task<bool> UpdateMinifigStockAsync(string minifigId, int stock) =>
        SendAsync(HttpMethod.Put,
            $"/api/myminifigs/{Uri.EscapeDataString(minifigId)}/stock",
            new { Stock = stock });

    public Task<MinifigBrickDto[]?> GetMinifigBricksAsync(string minifigId) =>
        GetAsync<MinifigBrickDto[]>($"/api/myminifigs/{Uri.EscapeDataString(minifigId)}/bricks");

    // ── Global Catalog ────────────────────────────────────────────────────────

    public Task<SetCatalogViewResponse?> GetSetCatalogViewAsync(string? search = null, int page = 0, int pageSize = 0) =>
        GetAsync<SetCatalogViewResponse>(BuildUrl("/api/sets/catalog-view", search, page, pageSize));

    public Task<PagedResult<BrickCatalogViewDto>?> GetBrickCatalogViewAsync(string? search = null, int page = 0, int pageSize = 0) =>
        GetAsync<PagedResult<BrickCatalogViewDto>>(BuildUrl("/api/bricks/catalog-view", search, page, pageSize));

    public Task<PagedResult<MinifigCatalogViewDto>?> GetMinifigCatalogViewAsync(string? search = null, int page = 0, int pageSize = 0) =>
        GetAsync<PagedResult<MinifigCatalogViewDto>>(BuildUrl("/api/minifigs/catalog-view", search, page, pageSize));

    // ── Owned set management ─────────────────────────────────────────────────

    public Task<bool> AddOwnedSetAsync(string setId, bool applyBricks) =>
        SendAsync(HttpMethod.Post, "/api/sets/owned", new { SetId = setId, ApplyBricks = applyBricks });

    public Task<bool> RemoveLastOwnedSetAsync(string setId) =>
        SendAsync(HttpMethod.Delete, $"/api/sets/owned/{Uri.EscapeDataString(setId)}/last");

    // ── Import ────────────────────────────────────────────────────────────────

    public Task<ResolveSetResponse?> ResolveSetAsync(string query, int page = 0) =>
        PostAsync<ResolveSetResponse>("/api/sets/resolve", new { Query = query, Page = page });

    public Task<bool> ImportSetAsync(string setId) =>
        SendAsync(HttpMethod.Post, "/api/sets/import", new { SetId = setId });

    // ── BoM ───────────────────────────────────────────────────────────────────

    public Task<BomDto?> GetBomAsync(string setId, int setIndex) =>
        GetAsync<BomDto>($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}");

    public Task<bool> UpdateSetBrickStockAsync(string setId, int setIndex, string partNum, string colorId, int stock) =>
        SendAsync(HttpMethod.Patch,
            $"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/bricks/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}",
            new { Stock = stock });

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

    public record MyBrickDto(string PartNum, string Name, string? PartImg, string? ColorId,
        string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId,
        int Stock, int UserNeeded, int UserSetCount);

    public record MyBrickSetDetailDto(string SetId, string SetName, string? SetImg, int BrickCount, int CopiesOwned);

    public record MyMinifigDto(string MinifigId, string MinifigName, string? ImgUrl,
        int Stock, int UserNeeded, int UserSetCount, int PartCount);

    public record MinifigBrickDto(string BrickId, string ColorId, string Name, string? PartImg,
        string? ColorName, string? HexColor, int Quantity);

    public record BomBrickDto(string PartNum, string ColorId, string Name, string? PartImg,
        string? ColorName, string? HexColor, int Count, int SpareCount,
        int SetStock, int LooseStock, string? BricklinkId);

    public record BomMinifigDto(string MinifigId, string Name, string? ImgUrl, int Count, int OwnedStock);

    public record BomDto(string SetId, int SetIndex, string SetName, string ManualUrl,
        List<int> OwnedInstances, List<string> OwnedSetIds,
        List<BomBrickDto> Bricks, List<BomMinifigDto> Minifigs);

    public record SetCatalogViewDto(string SetId, string Name, string? SetImg, int NumBricks,
        int ReleaseYear, string? ThemeName, string ManualUrl, int UserOwnedCount);

    public record PagedResult<T>(List<T> Items, bool HasMore);

    public record SetCatalogViewResponse(List<SetCatalogViewDto> Sets,
        int TotalOwnedInstances, int TotalOwners, int TotalPieces, bool HasMore = false);

    public record BrickCatalogViewDto(string PartNum, string Name, string? PartImg, string? ColorId,
        string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId,
        int TotalStock, int TotalNeeded, int SetCount);

    public record MinifigCatalogViewDto(string MinifigId, string MinifigName, string? ImgUrl,
        string MinifigUrl, int PartCount);

    public record SetCandidate(string SetNum, string Name, int Year, string? ImageUrl);

    public record ResolveSetResponse(List<SetCandidate> Results, bool Resolved, bool HasMore);
}
