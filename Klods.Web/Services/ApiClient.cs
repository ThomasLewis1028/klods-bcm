using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Forms;

namespace Klods.Services;

/// <summary>
/// Scoped per-circuit HTTP client for the Klods API.
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

    public Task<UserProfileDto?> GetMyProfileAsync(string? bearerToken = null)
    {
        if (bearerToken is null) return GetAsync<UserProfileDto>("/api/auth/me");
        return GetWithTokenAsync<UserProfileDto>("/api/auth/me", bearerToken);
    }

    private async Task<T?> GetWithTokenAsync<T>(string url, string bearerToken)
    {
        try
        {
            var client = factory.CreateClient("api");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            var resp = await client.GetAsync(url);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<T>(JsonOpts) : default;
        }
        catch { return default; }
    }

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

    public Task<SetCatalogStatsDto?>     GetSetCatalogStatsAsync()   => GetAsync<SetCatalogStatsDto>("/api/sets/catalog-stats");
    public Task<ThemeDto[]?>             GetSetThemesAsync()         => GetAsync<ThemeDto[]>("/api/sets/themes");
    public Task<SetCatalogPage?>         GetSetsCatalogPageAsync(string q, int? theme, string sort, string dir, int page, int pageSize)
    {
        var url = $"/api/sets/catalog?q={Uri.EscapeDataString(q)}&sort={sort}&dir={dir}&page={page}&pageSize={pageSize}";
        if (theme is int t) url += $"&theme={t}";
        return GetAsync<SetCatalogPage>(url);
    }
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

    public Task<BrickCatalogStatsDto?>   GetBrickCatalogStatsAsync()          => GetAsync<BrickCatalogStatsDto>("/api/bricks/catalog-stats");
    public Task<BrickCatalogPage?>       GetBricksCatalogPageAsync(string q, int page, int pageSize)
        => GetAsync<BrickCatalogPage>($"/api/bricks/catalog?q={Uri.EscapeDataString(q)}&page={page}&pageSize={pageSize}");
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

    public Task<MinifigSearchDto[]?>      SearchMinifigsCatalogAsync(string q)    => GetAsync<MinifigSearchDto[]>($"/api/minifigs/catalog-search?q={Uri.EscapeDataString(q)}");
    public Task<MinifigCatalogStatsDto?>  GetMinifigCatalogStatsAsync()           => GetAsync<MinifigCatalogStatsDto>("/api/minifigs/catalog-stats");
    public Task<MinifigCatalogPage?>      GetMinifigsCatalogPageAsync(string q, int page, int pageSize)
        => GetAsync<MinifigCatalogPage>($"/api/minifigs/catalog?q={Uri.EscapeDataString(q)}&page={page}&pageSize={pageSize}");
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
    public Task<MyMinifigBrickDto[]?> GetMyMinifigLooseBricksAsync(string id)              => GetAsync<MyMinifigBrickDto[]>($"/api/myminifigs/{Uri.EscapeDataString(id)}/loose-bricks");
    public async Task<bool>       UpsertMinifigStockAsync(string minifigId, int stock)     => (await PutAsync($"/api/myminifigs/{Uri.EscapeDataString(minifigId)}/stock", new { Stock = stock })).Ok;
    public async Task<bool>       UpdateMyMinifigLooseBrickStockAsync(string minifigId, string partNum, string colorId, int stock)
        => (await PatchAsync($"/api/myminifigs/{Uri.EscapeDataString(minifigId)}/loose-bricks/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}", new { Stock = stock })).Ok;

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
    public async Task<bool> UpdateBomMinifigBrickStockAsync(string setId, int setIndex, string minifigId, string partNum, string colorId, int stock)
        => (await PatchAsync($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/minifigs/{Uri.EscapeDataString(minifigId)}/bricks/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}", new { Stock = stock })).Ok;

    // ── Admin ────────────────────────────────────────────────────────────────

    public Task<AdminUserDto[]?> GetAdminUsersAsync()     => GetAsync<AdminUserDto[]>("/api/admin/users");
    public async Task<bool> ImportColorsAsync()           => (await PostAsync("/api/admin/import-colors")).Ok;
    public async Task<bool> SetUserRoleAsync(int userId, string role)
        => (await PatchAsync($"/api/admin/users/{userId}/role", new { Role = role })).Ok;

    public Task<CatalogImportDto[]?> GetCatalogImportsAsync() => GetAsync<CatalogImportDto[]>("/api/admin/catalog-imports");

    public Task<RssSettingsDto?> GetRssSettingsAsync()        => GetAsync<RssSettingsDto>("/api/admin/rss-settings");
    public Task<TimezoneDto[]?> GetTimezonesAsync()           => GetAsync<TimezoneDto[]>("/api/admin/timezones");
    public Task<CronPreviewDto?> PreviewCronAsync(string cron, string tz)
        => GetAsync<CronPreviewDto>($"/api/admin/cron-preview?cron={Uri.EscapeDataString(cron)}&tz={Uri.EscapeDataString(tz)}");
    public async Task<(bool Ok, string? Error)> SaveRssSettingsAsync(bool enabled, string cron, string timezone, int maxImports)
    {
        try
        {
            var resp = await Http().PutAsJsonAsync("/api/admin/rss-settings",
                new { Enabled = enabled, Cron = cron, Timezone = timezone, MaxImports = maxImports }, JsonOpts);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync();
            return (false, string.IsNullOrWhiteSpace(body) ? "Failed to save settings." : body);
        }
        catch (Exception e) { return (false, e.Message); }
    }
    public async Task<CatalogImportDto?> RssPollNowAsync()
    {
        try
        {
            var resp = await Http().PostAsync("/api/admin/rss-poll", null);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<CatalogImportDto>(JsonOpts) : null;
        }
        catch { return null; }
    }

    public async Task<(bool Ok, string? Message)> BulkImportCatalogAsync(
        IReadOnlyList<IBrowserFile> files, DateTime? snapshot, CancellationToken ct = default)
    {
        const long maxFile = 600L * 1024 * 1024;
        var temps = new List<(string Path, string Name)>();
        try
        {
            // Buffer each browser file to a local temp file ONE AT A TIME. Holding several IBrowserFile
            // streams open while HttpClient sends them sequentially makes the idle ones time out.
            foreach (var f in files)
            {
                var path = Path.GetTempFileName();
                await using (var src = f.OpenReadStream(maxFile, ct))
                await using (var dest = File.Create(path))
                    await src.CopyToAsync(dest, ct);
                temps.Add((path, f.Name));
            }

            using var content = new MultipartFormDataContent();
            if (snapshot is { } s) content.Add(new StringContent(s.ToString("o")), "snapshotDate");
            foreach (var (path, name) in temps)
                content.Add(new StreamContent(File.OpenRead(path)), "files", name);

            // Dedicated client with a long timeout — a full COPY + upsert can run for minutes.
            var client = factory.CreateClient("api");
            client.Timeout = TimeSpan.FromMinutes(30);
            if (auth.Token is not null)
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

            var resp = await client.PostAsync("/api/admin/bulk-import", content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            return (resp.IsSuccessStatusCode, body);
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
        finally
        {
            foreach (var (path, _) in temps)
                try { File.Delete(path); } catch { /* best effort */ }
        }
    }

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
    public record SetCatalogStatsDto(int TotalSets, int TotalOwnedInstances, int TotalOwners, long TotalPieces);
    public record SetCatalogSearchDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl, int UserOwnedCount);
    public record SetCatalogPage(List<SetCatalogSearchDto> Items, int Total);
    public record ThemeDto(int Id, string Name);
    public record OwnedInstanceDto(int SetIndex, int MissingPieceCount, int StockCount);
    public record MyOwnedSetDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl, List<OwnedInstanceDto> Instances);
    public record SetCandidateDto(string SetNum, string Name, int Year, string? ImageUrl);
    public record ResolveSetResponse(List<SetCandidateDto> Results, bool Resolved, bool HasMore);

    public record BrickDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId);
    public record BrickCatalogViewDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId, int TotalStock, int TotalNeeded, int SetCount);
    public record BrickCatalogStatsDto(int TotalBricks, long TotalOwnedStock);
    public record BrickCatalogPage(List<BrickCatalogViewDto> Items, int Total);
    public record SetBrickDto(string SetId, string PartNum, string ColorId, int Count, int SpareCount);
    public record PartColorInfoDto(string ColorId, string ColorName, string? PartImgUrl);
    public record ResolveBrickResponse(string? PartName, List<PartColorInfoDto> Colors);
    public record MyBrickDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId, int Stock, int UserNeeded, int UserSetCount);
    public record MyBrickSetDetailDto(string SetId, string SetName, string? SetImg, int BrickCount, int CopiesOwned);

    public record MinifigCatalogViewDto(string MinifigId, string MinifigName, string? ImgUrl, string MinifigUrl, int PartCount);
    public record MinifigSearchDto(string MinifigId, string Name, string? ImgUrl);
    public record MinifigCatalogStatsDto(int TotalMinifigs, long TotalParts);
    public record MinifigCatalogPage(List<MinifigCatalogViewDto> Items, int Total);
    public record MinifigBrickDto(string BrickId, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Quantity);
    public record MinifigCandidateDto(string MinifigId, string Name, int NumParts, string? ImageUrl);
    public record ResolveMinifigResponse(List<MinifigCandidateDto> Results, bool Resolved, bool HasMore);
    public record MyMinifigDto(string MinifigId, string MinifigName, string? ImgUrl, int Stock, int InUseStock, int UserNeeded, int UserSetCount, int PartCount);
    public record MyMinifigBrickDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Need, int Owned);

    public record BomBrickDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Count, int SpareCount, int SetStock, int LooseStock, string? BricklinkId);
    public record BomMinifigDto(string MinifigId, string Name, string? ImgUrl, int Count, int OwnedStock);
    public record BomResponseDto(string SetId, int SetIndex, string SetName, string ManualUrl, List<int> OwnedInstances, List<string> OwnedSetIds, List<BomBrickDto> Bricks, List<BomMinifigDto> Minifigs);

    public record AdminUserDto(int UserId, string UserName, string Role, string? ProfilePictureUrl);
    public record CatalogImportDto(DateTime ImportedAt, DateTime? SnapshotDate, string Source, string Status, string? Notes);
    public record RssSettingsDto(bool Enabled, string Cron, string Timezone, int MaxImports);
    public record TimezoneDto(string Id, string DisplayName);
    public record CronPreviewDto(bool Valid, List<string> Next);
    public record UserStatsDto(int UserId, string UserName, string Role, string? ProfilePictureUrl, int OwnedSets, int OwnedBricks, int OwnedMinifigs);
}
