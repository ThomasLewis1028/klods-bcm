using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Klods.Services;

/// <summary>
/// Scoped per-circuit HTTP client for the LEGO Inventory API.
/// All data operations go through here; nothing in this class touches the database.
/// </summary>
public class ApiClient(IHttpClientFactory factory, AuthService auth, IConfiguration config)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private HttpClient Http()
    {
        var client = factory.CreateClient("api");
        if (auth.Token is not null)
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    private async Task<T?> GetAsync<T>(string url)
    {
        try
        {
            var resp = await Http().GetAsync(url);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<T>(JsonOpts) : default;
        }
        catch { return default; }
    }

    private async Task<(bool Ok, HttpStatusCode Status)> SendAsync(HttpMethod method, string url, object? body = null)
    {
        try
        {
            var req = new HttpRequestMessage(method, url);
            if (auth.Token is not null)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
            if (body is not null)
                req.Content = JsonContent.Create(body, options: JsonOpts);
            var resp = await factory.CreateClient("api").SendAsync(req);
            return (resp.IsSuccessStatusCode, resp.StatusCode);
        }
        catch { return (false, HttpStatusCode.ServiceUnavailable); }
    }

    private Task<(bool Ok, HttpStatusCode Status)> PostAsync(string url, object? body = null)  => SendAsync(HttpMethod.Post,   url, body);
    private Task<(bool Ok, HttpStatusCode Status)> PatchAsync(string url, object? body = null) => SendAsync(HttpMethod.Patch,  url, body);
    private Task<(bool Ok, HttpStatusCode Status)> PutAsync(string url, object? body = null)   => SendAsync(HttpMethod.Put,    url, body);
    private Task<(bool Ok, HttpStatusCode Status)> DeleteAsync(string url)                     => SendAsync(HttpMethod.Delete, url);

    // ── Auth / OAuth ──────────────────────────────────────────────────────────

    // Used for browser-navigated URLs — must be the publicly reachable API address.
    private string ApiPublicBase => (config["API_PUBLIC_URL"] ?? config["API_BASE_URL"] ?? "http://localhost:8090").TrimEnd('/');

    /// <summary>Returns the URL to navigate the browser to for an OAuth challenge.</summary>
    public string ChallengeUrl(string provider, string? linkToken = null)
    {
        var url = $"{ApiPublicBase}/auth/challenge?provider={Uri.EscapeDataString(provider)}";
        return linkToken is not null ? $"{url}&link_token={Uri.EscapeDataString(linkToken)}" : url;
    }

    /// <summary>
    /// Registers a link intent on the server (requires authentication) and returns a short-lived opaque token
    /// to be passed to ChallengeUrl. The server binds the token to the current user's ID so the OAuth callback
    /// can resolve the link target without trusting user-supplied parameters.
    /// </summary>
    public async Task<string?> CreateLinkIntentAsync()
    {
        try
        {
            var resp = await Http().PostAsJsonAsync("/api/auth/link-intent", new { }, JsonOpts);
            if (!resp.IsSuccessStatusCode) return null;
            return (await resp.Content.ReadFromJsonAsync<LinkIntentResponse>(JsonOpts))?.Token;
        }
        catch { return null; }
    }

    public Task<string[]?> GetProvidersAsync() => GetAsync<string[]>("/api/auth/providers");

    public async Task<string?> ExchangePendingTokenAsync(string token)
    {
        var resp = await GetAsync<TokenResponse>($"/api/auth/exchange/{token}");
        return resp?.Token;
    }

    public async Task<string?> LoginAsync(string username, string password)
    {
        var resp = await GetTokenResponse("/api/auth/login", new { Username = username, Password = password });
        return resp?.Token;
    }

    public async Task<string?> RegisterAsync(string username, string password)
    {
        var resp = await GetTokenResponse("/api/auth/register", new { Username = username, Password = password });
        return resp?.Token;
    }

    private async Task<TokenResponse?> GetTokenResponse(string url, object body)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(body, options: JsonOpts)
            };
            var resp = await factory.CreateClient("api").SendAsync(req);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts) : null;
        }
        catch { return null; }
    }

    public Task<UserProfileDto?> GetMyProfileAsync() => GetAsync<UserProfileDto>("/api/auth/me");

    public async Task<(bool Success, bool UsernameTaken)> ChangeUsernameAsync(string newUsername)
    {
        var (ok, status) = await PatchAsync("/api/auth/me/username", new { NewUsername = newUsername });
        return (ok, status == HttpStatusCode.Conflict);
    }

    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        var (ok, _) = await PatchAsync("/api/auth/me/password", new { CurrentPassword = currentPassword, NewPassword = newPassword });
        return ok;
    }

    public async Task ChangeThemeAsync(string? color)      => await PatchAsync("/api/auth/me/theme",   new { Color = color });
    public async Task ChangePictureAsync(string? url)      => await PatchAsync("/api/auth/me/picture", new { Url   = url });
    public Task<LinkedLoginDto[]?> GetLinkedLoginsAsync()  => GetAsync<LinkedLoginDto[]>("/api/auth/me/logins");
    public async Task<bool> UnlinkLoginAsync(string provider)
    {
        var (ok, _) = await DeleteAsync($"/api/auth/me/logins/{Uri.EscapeDataString(provider)}");
        return ok;
    }

    // ── Home ─────────────────────────────────────────────────────────────────

    public Task<HomePreviewDto?> GetHomePreviewAsync() => GetAsync<HomePreviewDto>("/api/home/preview");

    // ── Sets ─────────────────────────────────────────────────────────────────

    public Task<SetCatalogViewResponse?> GetSetsCatalogViewAsync()   => GetAsync<SetCatalogViewResponse>("/api/sets/catalog-view");
    public Task<MyOwnedSetDto[]?>        GetMyOwnedSetsAsync()       => GetAsync<MyOwnedSetDto[]>("/api/sets/my-owned");
    public async Task<bool> ImportSetAsync(string setId)             => (await PostAsync("/api/sets/import", new { SetId = setId })).Ok;
    public async Task<bool> AddOwnedSetAsync(string setId, bool applyBricks = false)
        => (await PostAsync("/api/sets/owned", new { SetId = setId, ApplyBricks = applyBricks })).Ok;
    public async Task<bool> DeleteOwnedSetAsync(string setId, int setIndex)
        => (await DeleteAsync($"/api/sets/owned/{Uri.EscapeDataString(setId)}/{setIndex}")).Ok;
    public async Task<bool> DeleteLastOwnedSetAsync(string setId)
        => (await DeleteAsync($"/api/sets/owned/{Uri.EscapeDataString(setId)}/last")).Ok;
    public async Task<bool> DeleteSetFromCatalogAsync(string setId)
        => (await DeleteAsync($"/api/sets/{Uri.EscapeDataString(setId)}")).Ok;

    // ── Bricks ───────────────────────────────────────────────────────────────

    public Task<BrickCatalogViewDto[]?>  GetBricksCatalogViewAsync()          => GetAsync<BrickCatalogViewDto[]>("/api/bricks/catalog-view");
    public Task<SetBrickDto[]?>          GetSetsForBrickAsync(string p, string c) => GetAsync<SetBrickDto[]>($"/api/bricks/{Uri.EscapeDataString(p)}/{Uri.EscapeDataString(c)}/sets");
    public async Task<ResolveBrickResponse?> ResolvePartColorsPostAsync(string partNum)
    {
        try
        {
            var resp = await Http().PostAsJsonAsync("/api/bricks/resolve", new { PartNum = partNum }, JsonOpts);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<ResolveBrickResponse>(JsonOpts) : null;
        }
        catch { return null; }
    }
    public async Task AddLooseBrickAsync(string partNum, string partName, PartColorInfoDto colorInfo, int quantity)
        => await PostAsync("/api/bricks/owned", new { PartNum = partNum, PartName = partName, ColorId = colorInfo.ColorId, ColorName = colorInfo.ColorName, PartImgUrl = colorInfo.PartImgUrl, Quantity = quantity });
    public async Task<bool> UpdateBrickStockAsync(string partNum, string colorId, int stock)
        => (await PatchAsync($"/api/bricks/owned/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}", new { Stock = stock })).Ok;

    // ── My Bricks ────────────────────────────────────────────────────────────

    public Task<MyBrickDto[]?>           GetMyBricksAsync()                                       => GetAsync<MyBrickDto[]>("/api/mybricks");
    public Task<MyBrickSetDetailDto[]?>  GetUserSetsForBrickAsync(string p, string c)             => GetAsync<MyBrickSetDetailDto[]>($"/api/mybricks/{Uri.EscapeDataString(p)}/{Uri.EscapeDataString(c)}/sets");
    public async Task<bool>              UpsertBrickStockAsync(string p, string c, int stock)     => (await PutAsync($"/api/mybricks/{Uri.EscapeDataString(p)}/{Uri.EscapeDataString(c)}/stock", new { Stock = stock })).Ok;

    // ── Minifigs ─────────────────────────────────────────────────────────────

    public Task<MinifigCatalogViewDto[]?> GetMinifigsCatalogViewAsync()           => GetAsync<MinifigCatalogViewDto[]>("/api/minifigs/catalog-view");
    public Task<MinifigDto[]?>            GetMinifigsAsync()                      => GetAsync<MinifigDto[]>("/api/minifigs");
    public Task<MinifigBrickDto[]?>       GetMinifigBricksAsync(string id)        => GetAsync<MinifigBrickDto[]>($"/api/minifigs/{Uri.EscapeDataString(id)}/bricks");
    public async Task<ResolveMinifigResponse?> ResolveMinifigIdPostAsync(string query, int page = 1)
    {
        try
        {
            var resp = await Http().PostAsJsonAsync("/api/minifigs/import", new { Query = query, Page = page }, JsonOpts);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<ResolveMinifigResponse>(JsonOpts) : null;
        }
        catch { return null; }
    }
    public async Task<bool> AddOwnedMinifigAsync(string minifigId, int count)
        => (await PostAsync("/api/minifigs/owned", new { MinifigId = minifigId, Count = count })).Ok;
    public async Task<bool> UpdateMinifigStockAsync(string minifigId, int stock)
        => (await PatchAsync($"/api/minifigs/owned/{Uri.EscapeDataString(minifigId)}", new { Stock = stock })).Ok;

    // ── My Minifigs ──────────────────────────────────────────────────────────

    public Task<MyMinifigDto[]?>  GetMyMinifigsAsync()                                     => GetAsync<MyMinifigDto[]>("/api/myminifigs");
    public Task<MinifigBrickDto[]?> GetMyMinifigBricksAsync(string id)                     => GetAsync<MinifigBrickDto[]>($"/api/myminifigs/{Uri.EscapeDataString(id)}/bricks");
    public async Task<bool>       UpsertMinifigStockAsync(string minifigId, int stock)     => (await PutAsync($"/api/myminifigs/{Uri.EscapeDataString(minifigId)}/stock", new { Stock = stock })).Ok;

    // ── BOM ──────────────────────────────────────────────────────────────────

    public Task<BomResponseDto?> GetBomAsync(string setId, int setIndex)
        => GetAsync<BomResponseDto>($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}");
    public async Task<bool> UpdateSetBrickStockAsync(string setId, int setIndex, string partNum, string colorId, int stock)
        => (await PatchAsync($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/bricks/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}", new { Stock = stock })).Ok;
    public async Task<bool> UpdateLooseBrickStockAsync(string setId, int setIndex, string partNum, string colorId, int stock)
        => (await PatchAsync($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/loose-bricks/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}", new { Stock = stock })).Ok;
    public async Task<bool> UpdateBomMinifigStockAsync(string setId, int setIndex, string minifigId, int stock)
        => (await PatchAsync($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/minifigs/{Uri.EscapeDataString(minifigId)}", new { Stock = stock })).Ok;
    public Task<BomBrickDto[]?> GetMinifigBricksInBomAsync(string setId, int setIndex, string minifigId)
        => GetAsync<BomBrickDto[]>($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/minifigs/{Uri.EscapeDataString(minifigId)}/bricks");

    // ── Admin ────────────────────────────────────────────────────────────────

    public Task<AdminUserDto[]?> GetAdminUsersAsync()     => GetAsync<AdminUserDto[]>("/api/admin/users");
    public Task<PendingCountDto?> GetPendingCountAsync()  => GetAsync<PendingCountDto>("/api/admin/pending-count");
    public async Task<bool> ImportColorsAsync()           => (await PostAsync("/api/admin/import-colors")).Ok;
    public async Task BackfillImagesAsync(CancellationToken ct = default)
    {
        try { await Http().PostAsync("/api/admin/backfill-images", null, ct); } catch { }
    }
    public async Task<bool> SetUserRoleAsync(int userId, string role)
        => (await PatchAsync($"/api/admin/users/{userId}/role", new { Role = role })).Ok;

    // ── Users ────────────────────────────────────────────────────────────────

    public Task<UserStatsDto[]?> GetUsersAsync() => GetAsync<UserStatsDto[]>("/api/users");

    // ── Sets resolve/import (called from dialogs) ─────────────────────────────

    public async Task<ResolveSetResponse?> ResolveSetIdPostAsync(string query, int page = 1)
    {
        try
        {
            var resp = await Http().PostAsJsonAsync("/api/sets/resolve", new { Query = query, Page = page }, JsonOpts);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<ResolveSetResponse>(JsonOpts) : null;
        }
        catch { return null; }
    }

    // ── Response models ───────────────────────────────────────────────────────

    public record TokenResponse(string Token);
    public record LinkIntentResponse(string Token);
    public record UserProfileDto(int UserId, string UserName, string Role, string? ProfilePictureUrl, string? PrimaryColor, bool HasPassword);
    public record LinkedLoginDto(string Provider);
    public record HomePreviewDto(List<PreviewItemDto> Sets, List<PreviewItemDto> Bricks, List<PreviewItemDto> Minifigs);
    public record PreviewItemDto(string Id, string Name, string? ImgUrl);

    public record SetDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl);
    public record SetCatalogViewDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl, int UserOwnedCount);
    public record SetCatalogViewResponse(List<SetCatalogViewDto> Sets, int TotalOwnedInstances, int TotalOwners, int TotalPieces);
    public record OwnedInstanceDto(int SetIndex, int MissingPieceCount, int StockCount);
    public record MyOwnedSetDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl, List<OwnedInstanceDto> Instances);
    public record SetCandidateDto(string SetNum, string Name, int Year, string? ImageUrl);
    public record ResolveSetResponse(List<SetCandidateDto> Results, bool Resolved, bool HasMore);

    public record BrickDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId);
    public record BrickCatalogViewDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId, int TotalStock, int TotalNeeded, int SetCount);
    public record SetBrickDto(string SetId, string PartNum, string ColorId, int Count, int SpareCount);
    public record PartColorInfoDto(string ColorId, string ColorName, string? PartImgUrl);
    public record ResolveBrickResponse(string? PartName, List<PartColorInfoDto> Colors);
    public record MyBrickDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId, int Stock, int UserNeeded, int UserSetCount);
    public record MyBrickSetDetailDto(string SetId, string SetName, string? SetImg, int BrickCount, int CopiesOwned);

    public record MinifigDto(string MinifigId, string MinifigName, string? ImgUrl, string MinifigUrl);
    public record MinifigCatalogViewDto(string MinifigId, string MinifigName, string? ImgUrl, string MinifigUrl, int PartCount);
    public record MinifigBrickDto(string BrickId, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Quantity);
    public record MinifigCandidateDto(string MinifigId, string Name, int NumParts, string? ImageUrl);
    public record ResolveMinifigResponse(List<MinifigCandidateDto> Results, bool Resolved, bool HasMore);
    public record MyMinifigDto(string MinifigId, string MinifigName, string? ImgUrl, int Stock, int UserNeeded, int UserSetCount, int PartCount);

    public record BomBrickDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Count, int SpareCount, int SetStock, int LooseStock, string? BricklinkId);
    public record BomMinifigDto(string MinifigId, string Name, string? ImgUrl, int Count, int OwnedStock);
    public record BomResponseDto(string SetId, int SetIndex, string SetName, string ManualUrl, List<int> OwnedInstances, List<string> OwnedSetIds, List<BomBrickDto> Bricks, List<BomMinifigDto> Minifigs);

    public record AdminUserDto(int UserId, string UserName, string Role, string? ProfilePictureUrl);
    public record PendingCountDto(int Count);
    public record UserStatsDto(int UserId, string UserName, string Role, string? ProfilePictureUrl, int OwnedSets, int OwnedBricks, int OwnedMinifigs);
}
