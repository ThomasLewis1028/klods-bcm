using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class ApiKeyEndpoints
{
    public static void MapApiKeys(this IEndpointRouteBuilder app)
    {
        // Key management needs a real sign-in: the group is JWT-only, so a key can't mint or list keys.
        var mine = app.MapGroup("/api/auth/me/keys").RequireAuthorization();

        mine.MapGet("/", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory,
            SettingsService settings, IConfiguration config) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var keys = await db.ApiKeys.AsNoTracking()
                .Where(k => k.UserId == userId)
                .OrderBy(k => k.CreatedAt)
                .Select(k => new ApiKeyDto(k.Id, k.Name, k.Prefix, k.CreatedAt, k.LastUsedAt))
                .ToListAsync();
            return Results.Ok(new MyApiKeysResponse(
                await settings.GetBoolAsync(ApiKeyAuth.EnabledSettingKey), MaxKeysPerUser(config), keys));
        });

        mine.MapPost("/", async (CreateApiKeyRequest req, HttpContext http,
            IDbContextFactory<InventoryContext> dbFactory, SettingsService settings, IConfiguration config) =>
        {
            if (!await settings.GetBoolAsync(ApiKeyAuth.EnabledSettingKey))
                return Results.Json("MCP access is disabled.", statusCode: StatusCodes.Status403Forbidden);

            var name = (req.Name ?? "").Trim();
            if (name.Length is 0 or > ApiKey.MaxNameLength)
                return Results.BadRequest($"Name must be 1-{ApiKey.MaxNameLength} characters.");

            var userId = http.UserId();
            var max = MaxKeysPerUser(config);
            await using var db = dbFactory.CreateDbContext();
            if (await db.ApiKeys.CountAsync(k => k.UserId == userId) >= max)
                return Results.BadRequest($"You already have {max} keys. Revoke one first.");

            var (key, prefix, hash) = ApiKeyAuth.Generate();
            var entity = new ApiKey { UserId = userId, Name = name, Prefix = prefix, Hash = hash, CreatedAt = DateTime.UtcNow };
            db.ApiKeys.Add(entity);
            await db.SaveChangesAsync();

            return Results.Ok(new CreatedApiKeyResponse(entity.Id, entity.Name, entity.Prefix, entity.CreatedAt, key));
        });

        mine.MapDelete("/{id:int}", async (int id, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var rows = await db.ApiKeys.Where(k => k.Id == id && k.UserId == userId).ExecuteDeleteAsync();
            return rows > 0 ? Results.Ok() : Results.NotFound();
        });

        // Lets the MCP server confirm a key before serving a request. Deliberately outside the key rate limit:
        // an exhausted budget must surface as 429 on the tool call, not as a rejected key.
        app.MapGet("/api/auth/whoami", (HttpContext http) => Results.Ok(new WhoAmIResponse(
                http.User.FindFirstValue("name") ?? "",
                http.User.FindFirstValue(ApiKeyAuth.KeyNameClaim))))
            .RequireAuthorization(ApiKeyAuth.Policy);

        var admin = app.MapGroup("/api/admin").RequireAuthorization("Admin");

        admin.MapGet("/mcp-settings", async (SettingsService settings) =>
            Results.Ok(new McpSettingsDto(await settings.GetBoolAsync(ApiKeyAuth.EnabledSettingKey))));

        admin.MapPut("/mcp-settings", async (McpSettingsDto req, SettingsService settings) =>
        {
            await settings.SetAsync(ApiKeyAuth.EnabledSettingKey, req.Enabled ? "true" : "false");
            return Results.Ok();
        });

        admin.MapGet("/api-keys", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var keys = await db.ApiKeys.AsNoTracking()
                .Join(db.Users, k => k.UserId, u => u.UserId, (k, u) => new { Key = k, u.UserName })
                .OrderBy(x => x.UserName).ThenBy(x => x.Key.CreatedAt)
                .Select(x => new AdminApiKeyDto(x.Key.Id, x.Key.UserId, x.UserName, x.Key.Name, x.Key.Prefix,
                    x.Key.CreatedAt, x.Key.LastUsedAt))
                .ToListAsync();
            return Results.Ok(keys);
        });

        admin.MapDelete("/api-keys/{id:int}", async (int id, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var rows = await db.ApiKeys.Where(k => k.Id == id).ExecuteDeleteAsync();
            return rows > 0 ? Results.Ok() : Results.NotFound();
        });
    }

    private static int MaxKeysPerUser(IConfiguration config) => config.GetValue("MCP_MAX_KEYS_PER_USER", 5);

    public record ApiKeyDto(int Id, string Name, string Prefix, DateTime CreatedAt, DateTime? LastUsedAt);
    public record MyApiKeysResponse(bool Enabled, int MaxKeys, List<ApiKeyDto> Keys);
    public record CreateApiKeyRequest(string? Name);
    public record CreatedApiKeyResponse(int Id, string Name, string Prefix, DateTime CreatedAt, string Key);
    public record WhoAmIResponse(string UserName, string? KeyName);
    public record McpSettingsDto(bool Enabled);
    public record AdminApiKeyDto(int Id, int UserId, string UserName, string Name, string Prefix, DateTime CreatedAt, DateTime? LastUsedAt);
}
