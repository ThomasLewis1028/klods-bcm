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

    public async Task<(string? Token, bool IsPending)> LoginAsync(string username, string password)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new { Username = username, Password = password }, options: JsonOpts)
            };
            var resp = await factory.CreateClient("api").SendAsync(req);
            if (resp.StatusCode == HttpStatusCode.Forbidden) return (null, true);
            if (!resp.IsSuccessStatusCode) return (null, false);
            var token = (await resp.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts))?.Token;
            return (token, false);
        }
        catch { return (null, false); }
    }

    public async Task<(string? Token, bool IsPending)> RegisterAsync(string username, string password)
    {
        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
            {
                Content = JsonContent.Create(new { Username = username, Password = password }, options: JsonOpts)
            };
            var resp = await factory.CreateClient("api").SendAsync(req);
            if (resp.StatusCode == HttpStatusCode.Accepted) return (null, true);
            if (!resp.IsSuccessStatusCode) return (null, false);
            var token = (await resp.Content.ReadFromJsonAsync<TokenResponse>(JsonOpts))?.Token;
            return (token, false);
        }
        catch { return (null, false); }
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
    public async Task ChangeFontScaleAsync(double scale)   => await PatchAsync("/api/auth/me/fontscale", new { Scale = scale });
    public async Task MarkTourSeenAsync()                  => await PatchAsync("/api/auth/me/tour-seen");
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
    public async Task<bool> DeleteOwnedSetAsync(string setId, int setIndex, bool moveStock = false)
        => (await DeleteAsync($"/api/sets/owned/{Uri.EscapeDataString(setId)}/{setIndex}?moveStock={moveStock}")).Ok;
    public async Task<bool> DeleteLastOwnedSetAsync(string setId)
        => (await DeleteAsync($"/api/sets/owned/{Uri.EscapeDataString(setId)}/last")).Ok;
    public async Task<bool> DeleteSetFromCatalogAsync(string setId)
        => (await DeleteAsync($"/api/sets/{Uri.EscapeDataString(setId)}")).Ok;
    public async Task<bool> UpdateOwnedSetNotesAsync(string setId, int setIndex, string? location, string? notes)
        => (await PutAsync($"/api/sets/owned/{Uri.EscapeDataString(setId)}/{setIndex}/notes", new { Location = location, Notes = notes })).Ok;

    // ── Bricks ───────────────────────────────────────────────────────────────

    public Task<BrickCatalogStatsDto?>   GetBrickCatalogStatsAsync()          => GetAsync<BrickCatalogStatsDto>("/api/bricks/catalog-stats");
    public Task<BrickCatalogPage?>       GetBricksCatalogPageAsync(string q, string sort, string dir, int page, int pageSize)
        => GetAsync<BrickCatalogPage>($"/api/bricks/catalog?q={Uri.EscapeDataString(q)}&sort={sort}&dir={dir}&page={page}&pageSize={pageSize}");
    public Task<SetForBrickPage?>        GetSetsForBrickPageAsync(string p, string c, string q, int page, int pageSize)
        => GetAsync<SetForBrickPage>($"/api/bricks/{Uri.EscapeDataString(p)}/{Uri.EscapeDataString(c)}/sets/paged?q={Uri.EscapeDataString(q)}&page={page}&pageSize={pageSize}");
    public Task<OwnedStockDto?>          GetMyBrickStockAsync(string p, string c) => GetAsync<OwnedStockDto>($"/api/bricks/{Uri.EscapeDataString(p)}/{Uri.EscapeDataString(c)}/owned");
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
    public async Task<bool> UpdateBrickNotesAsync(string partNum, string colorId, string? location, string? notes)
        => (await PutAsync($"/api/bricks/owned/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}/notes", new { Location = location, Notes = notes })).Ok;

    // ── My Bricks ────────────────────────────────────────────────────────────

    public Task<MyBrickDto[]?>           GetMyBricksAsync()                                       => GetAsync<MyBrickDto[]>("/api/mybricks");
    public Task<MyBrickSetDetailDto[]?>  GetUserSetsForBrickAsync(string p, string c)             => GetAsync<MyBrickSetDetailDto[]>($"/api/mybricks/{Uri.EscapeDataString(p)}/{Uri.EscapeDataString(c)}/sets");
    public async Task<bool>              UpsertBrickStockAsync(string p, string c, int stock)     => (await PutAsync($"/api/mybricks/{Uri.EscapeDataString(p)}/{Uri.EscapeDataString(c)}/stock", new { Stock = stock })).Ok;

    // ── Minifigs ─────────────────────────────────────────────────────────────

    public Task<MinifigSearchPage?>        SearchMinifigsCatalogAsync(string q, int page = 0, int pageSize = 10)
        => GetAsync<MinifigSearchPage>($"/api/minifigs/catalog-search?q={Uri.EscapeDataString(q)}&page={page}&pageSize={pageSize}");
    public Task<MinifigCatalogStatsDto?>  GetMinifigCatalogStatsAsync()           => GetAsync<MinifigCatalogStatsDto>("/api/minifigs/catalog-stats");
    public Task<MinifigCatalogPage?>      GetMinifigsCatalogPageAsync(string q, string sort, string dir, int page, int pageSize)
        => GetAsync<MinifigCatalogPage>($"/api/minifigs/catalog?q={Uri.EscapeDataString(q)}&sort={sort}&dir={dir}&page={page}&pageSize={pageSize}");
    public Task<MinifigBrickDto[]?>       GetMinifigBricksAsync(string id)        => GetAsync<MinifigBrickDto[]>($"/api/minifigs/{Uri.EscapeDataString(id)}/bricks");
    public Task<LooseCountDto?>           GetMyMinifigLooseCountAsync(string id)  => GetAsync<LooseCountDto>($"/api/minifigs/{Uri.EscapeDataString(id)}/loose-count");
    public async Task<ResolveMinifigResponse?> ResolveMinifigIdPostAsync(string query, int page = 1)
    {
        try
        {
            var resp = await Http().PostAsJsonAsync("/api/minifigs/import", new { Query = query, Page = page }, JsonOpts);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<ResolveMinifigResponse>(JsonOpts) : null;
        }
        catch { return null; }
    }
    public async Task<bool> AddOwnedMinifigAsync(string minifigId, int count, bool applyParts = false)
        => (await PostAsync("/api/minifigs/owned", new { MinifigId = minifigId, Count = count, ApplyParts = applyParts })).Ok;
    public async Task<bool> UpdateMinifigStockAsync(string minifigId, int stock)
        => (await PatchAsync($"/api/minifigs/owned/{Uri.EscapeDataString(minifigId)}", new { Stock = stock })).Ok;

    // ── My Minifigs ──────────────────────────────────────────────────────────

    public Task<MyMinifigDto[]?>  GetMyMinifigsAsync()                                     => GetAsync<MyMinifigDto[]>("/api/myminifigs");
    public Task<MyMinifigBrickDto[]?> GetMyMinifigLooseBricksAsync(string id)              => GetAsync<MyMinifigBrickDto[]>($"/api/myminifigs/{Uri.EscapeDataString(id)}/loose-bricks");
    public async Task<bool>       UpsertMinifigStockAsync(string minifigId, int stock)     => (await PutAsync($"/api/myminifigs/{Uri.EscapeDataString(minifigId)}/stock", new { Stock = stock })).Ok;
    public async Task<bool>       UpdateMyMinifigLooseBrickStockAsync(string minifigId, string partNum, string colorId, int stock)
        => (await PatchAsync($"/api/myminifigs/{Uri.EscapeDataString(minifigId)}/loose-bricks/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}", new { Stock = stock })).Ok;

    public Task<MinifigInstanceDto[]?> GetMinifigInstancesAsync(string id)   => GetAsync<MinifigInstanceDto[]>($"/api/myminifigs/{Uri.EscapeDataString(id)}/instances");
    public Task<AssignableCopyDto[]?>  GetAssignableCopiesAsync(string id)   => GetAsync<AssignableCopyDto[]>($"/api/myminifigs/{Uri.EscapeDataString(id)}/assignable-copies");
    public async Task<bool> SetMinifigInstancePartStockAsync(string id, int index, string partNum, string colorId, int stock)
        => (await PatchAsync($"/api/myminifigs/{Uri.EscapeDataString(id)}/instances/{index}/parts/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}", new { Stock = stock })).Ok;
    public async Task<bool> AssignMinifigInstanceAsync(string id, int index, string? setId, int? setIndex)
        => (await PatchAsync($"/api/myminifigs/{Uri.EscapeDataString(id)}/instances/{index}/assign", new { SetId = setId, SetIndex = setIndex })).Ok;
    public async Task<bool> AddLooseMinifigInstanceAsync(string id)
        => (await PostAsync($"/api/myminifigs/{Uri.EscapeDataString(id)}/instances")).Ok;
    public async Task<bool> RemoveMinifigInstanceAsync(string id, int index)
        => (await DeleteAsync($"/api/myminifigs/{Uri.EscapeDataString(id)}/instances/{index}")).Ok;

    // ── BOM ──────────────────────────────────────────────────────────────────

    public Task<BomResponseDto?> GetBomAsync(string setId, int setIndex)
        => GetAsync<BomResponseDto>($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}");
    public Task<CompletenessDto?> GetBomCompletenessAsync(string setId, int setIndex)
        => GetAsync<CompletenessDto>($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/completeness");
    public async Task<bool> UpdateSetBrickStockAsync(string setId, int setIndex, string partNum, string colorId, int stock)
        => (await PatchAsync($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/bricks/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}", new { Stock = stock })).Ok;
    public async Task<bool> UpdateLooseBrickStockAsync(string setId, int setIndex, string partNum, string colorId, int stock)
        => (await PatchAsync($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/loose-bricks/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}", new { Stock = stock })).Ok;
    public Task<BomMinifigInstanceDto[]?> GetBomMinifigInstancesAsync(string setId, int setIndex, string minifigId)
        => GetAsync<BomMinifigInstanceDto[]>($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/minifigs/{Uri.EscapeDataString(minifigId)}/instances");
    public async Task<bool> AddBomMinifigInstanceAsync(string setId, int setIndex, string minifigId)
        => (await PostAsync($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/minifigs/{Uri.EscapeDataString(minifigId)}/instances")).Ok;
    public async Task<bool> RemoveBomMinifigInstanceAsync(string setId, int setIndex, string minifigId, int index)
        => (await DeleteAsync($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/minifigs/{Uri.EscapeDataString(minifigId)}/instances/{index}")).Ok;
    public async Task<bool> UpdateBomMinifigInstancePartStockAsync(string setId, int setIndex, string minifigId, int index, string partNum, string colorId, int stock)
        => (await PatchAsync($"/api/bom/{Uri.EscapeDataString(setId)}/{setIndex}/minifigs/{Uri.EscapeDataString(minifigId)}/instances/{index}/parts/{Uri.EscapeDataString(partNum)}/{Uri.EscapeDataString(colorId)}", new { Stock = stock })).Ok;

    // ── Admin ────────────────────────────────────────────────────────────────

    public Task<AdminUserDto[]?> GetAdminUsersAsync()     => GetAsync<AdminUserDto[]>("/api/admin/users");
    public async Task<bool> ImportColorsAsync()           => (await PostAsync("/api/admin/import-colors")).Ok;
    public async Task<bool> SetUserRoleAsync(int userId, string role)
        => (await PatchAsync($"/api/admin/users/{userId}/role", new { Role = role })).Ok;
    public async Task<bool> SetUserStatusAsync(int userId, string status)
        => (await PatchAsync($"/api/admin/users/{userId}/status", new { Status = status })).Ok;
    public Task<RegistrationSettingsDto?> GetRegistrationSettingsAsync()
        => GetAsync<RegistrationSettingsDto>("/api/admin/registration-settings");
    public async Task<bool> SaveRegistrationSettingsAsync(bool autoApprove)
        => (await PutAsync("/api/admin/registration-settings", new { AutoApprove = autoApprove })).Ok;

    public Task<CatalogImportDto[]?> GetCatalogImportsAsync() => GetAsync<CatalogImportDto[]>("/api/admin/catalog-imports");

    public Task<ThemeVisibilityDto[]?> GetThemeVisibilityAsync() => GetAsync<ThemeVisibilityDto[]>("/api/admin/theme-visibility");
    public async Task<bool> SaveThemeVisibilityAsync(IEnumerable<int> hiddenThemeIds)
        => (await PutAsync("/api/admin/theme-visibility", new { HiddenThemeIds = hiddenThemeIds.ToArray() })).Ok;

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

    public Task<SetUpdateSettingsDto?> GetSetUpdateSettingsAsync() => GetAsync<SetUpdateSettingsDto>("/api/admin/set-update-settings");
    public async Task<(bool Ok, string? Error)> SaveSetUpdateSettingsAsync(bool enabled, string cron, string timezone, int maxReimports)
    {
        try
        {
            var resp = await Http().PutAsJsonAsync("/api/admin/set-update-settings",
                new { Enabled = enabled, Cron = cron, Timezone = timezone, MaxReimports = maxReimports }, JsonOpts);
            if (resp.IsSuccessStatusCode) return (true, null);
            var body = await resp.Content.ReadAsStringAsync();
            return (false, string.IsNullOrWhiteSpace(body) ? "Failed to save settings." : body);
        }
        catch (Exception e) { return (false, e.Message); }
    }
    public async Task<CatalogImportDto?> SetUpdatePollNowAsync()
    {
        try
        {
            var resp = await Http().PostAsync("/api/admin/set-update-poll", null);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadFromJsonAsync<CatalogImportDto>(JsonOpts) : null;
        }
        catch { return null; }
    }

    public Task<NotificationDto[]?> GetNotificationsAsync() => GetAsync<NotificationDto[]>("/api/notifications/");
    public async Task<int> GetUnreadNotificationCountAsync() => await GetAsync<int?>("/api/notifications/unread-count") ?? 0;
    public Task MarkAllNotificationsReadAsync() => PostAsync("/api/notifications/read-all");
    public Task MarkNotificationReadAsync(int id) => PostAsync($"/api/notifications/{id}/read");

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
    public record UserProfileDto(int UserId, string UserName, string Role, string? ProfilePictureUrl, string? PrimaryColor, bool HasPassword, double FontScale, bool HasSeenTour);
    public record LinkedLoginDto(string Provider);
    public record HomePreviewDto(
        PreviewItemDto? CatalogSet, PreviewItemDto? CatalogBrick, PreviewItemDto? CatalogMinifig,
        PreviewItemDto? MySet, PreviewItemDto? MyBrick, PreviewItemDto? MyMinifig);
    public record PreviewItemDto(string Id, string Name, string? ImgUrl);

    public record SetDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl);
    public record SetCatalogStatsDto(int TotalSets, int TotalOwnedInstances, int TotalOwners, long TotalPieces);
    public record SetCatalogSearchDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl, int UserOwnedCount);
    public record SetCatalogPage(List<SetCatalogSearchDto> Items, int Total);
    public record ThemeDto(int Id, string Name);
    public record OwnedInstanceDto(int SetIndex, int MissingPieceCount, int StockCount, int Percent, string Status, string? Location, string? Notes);
    public record MyOwnedSetDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl, List<OwnedInstanceDto> Instances);
    public record SetCandidateDto(string SetNum, string Name, int Year, string? ImageUrl);
    public record ResolveSetResponse(List<SetCandidateDto> Results, bool Resolved, bool HasMore);

    public record BrickDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId);
    public record BrickCatalogViewDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId, int TotalStock, int TotalUsed, int SetCount);
    public record BrickCatalogStatsDto(int TotalBricks, long TotalUsed);
    public record BrickCatalogPage(List<BrickCatalogViewDto> Items, int Total);
    public record SetForBrickDto(string SetId, string Name, string? SetImg, int Count);
    public record SetForBrickPage(List<SetForBrickDto> Items, int Total);
    public record OwnedStockDto(int Stock, string? Location, string? Notes);
    public record LooseCountDto(int Count);
    public record PartColorInfoDto(string ColorId, string ColorName, string? PartImgUrl);
    public record ResolveBrickResponse(string? PartName, List<PartColorInfoDto> Colors);
    public record MyBrickDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId, int Stock, int UserNeeded, int UserSetCount);
    public record MyBrickSetDetailDto(string SetId, string SetName, string? SetImg, int BrickCount, int CopiesOwned);

    public record MinifigCatalogViewDto(string MinifigId, string MinifigName, string? ImgUrl, string MinifigUrl, int PartCount);
    public record MinifigSearchDto(string MinifigId, string Name, string? ImgUrl);
    public record MinifigSearchPage(List<MinifigSearchDto> Items, int Total);
    public record MinifigCatalogStatsDto(int TotalMinifigs, long TotalParts);
    public record MinifigCatalogPage(List<MinifigCatalogViewDto> Items, int Total);
    public record MinifigBrickDto(string BrickId, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Quantity);
    public record MinifigCandidateDto(string MinifigId, string Name, int NumParts, string? ImageUrl);
    public record ResolveMinifigResponse(List<MinifigCandidateDto> Results, bool Resolved, bool HasMore);
    public record MyMinifigDto(string MinifigId, string MinifigName, string? ImgUrl, int Stock, int InUseStock, int UserNeeded, int UserSetCount, int PartCount);
    public record MinifigInstanceDto(int Index, string? SetId, int? SetIndex, string? SetName, string? SetImg, List<MinifigInstancePartDto> Parts);
    public record MinifigInstancePartDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Need, int Owned);
    public record AssignableCopyDto(string SetId, string SetName, string? SetImg, int SetIndex);
    public record MyMinifigBrickDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Need, int Owned);

    public record BomBrickDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Count, int SpareCount, int SetStock, int LooseStock, string? BricklinkId);
    public record BomMinifigDto(string MinifigId, string Name, string? ImgUrl, int Count, int OwnedStock);
    public record BomMinifigInstanceDto(int Index, List<BomMinifigInstancePartDto> Parts);
    public record BomMinifigInstancePartDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Need, int Owned);
    public record BomResponseDto(string SetId, int SetIndex, string SetName, string ManualUrl, List<int> OwnedInstances, List<string> OwnedSetIds, List<BomBrickDto> Bricks, List<BomMinifigDto> Minifigs, int Percent, string Status, string? Location, string? Notes);
    public record CompletenessDto(int Percent, string Status);

    public record AdminUserDto(int UserId, string UserName, string Role, string? ProfilePictureUrl, string Status);
    public record RegistrationSettingsDto(bool AutoApprove);
    public record CatalogImportDto(DateTime ImportedAt, DateTime? SnapshotDate, string Source, string Status, string? Notes);
    public record RssSettingsDto(bool Enabled, string Cron, string Timezone, int MaxImports);
    public record SetUpdateSettingsDto(bool Enabled, string Cron, string Timezone, int MaxReimports);
    public record NotificationDto(int Id, string SetId, string SetName, string? SetImg, DateTime DetectedAt, bool Read, List<NotificationItemDto> Items);
    public record NotificationItemDto(string PartNum, string? PartName, string? PartImg, string? ColorName, string? HexColor, string ChangeKind, int OldCount, int NewCount);
    public record ThemeVisibilityDto(int Id, string Name, int? ParentId, int SetCount, bool Hidden);
    public record TimezoneDto(string Id, string DisplayName);
    public record CronPreviewDto(bool Valid, List<string> Next);
    public record UserStatsDto(int UserId, string UserName, string Role, string? ProfilePictureUrl, int OwnedSets, int OwnedBricks, int OwnedMinifigs);
}
