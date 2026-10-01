using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Klods.Database;
using Klods.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Klods.Api.Auth;

/// <summary>
/// Per-user MCP keys. Keys are deny-by-default: the default authorization policy only runs the JWT
/// scheme, so a key authenticates nowhere except endpoints opted in with <see cref="AllowApiKey{TBuilder}"/>,
/// which also attaches the per-user key rate limit.
/// </summary>
public static class ApiKeyAuth
{
    public const string Scheme = "ApiKey";
    public const string Policy = "ApiKeyAllowed";
    public const string RateLimitPolicy = "apikey";
    public const string EnabledSettingKey = "mcp.enabled";
    public const string KeyPrefix = "klods_";

    public const string AuthMethodClaim = "auth_method";
    public const string AuthMethodValue = "apikey";
    public const string KeyNameClaim = "key_name";

    private const int DisplayPrefixLength = 12;
    private static readonly TimeSpan LastUsedResolution = TimeSpan.FromMinutes(1);

    public static TBuilder AllowApiKey<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization(Policy).RequireRateLimiting(RateLimitPolicy);

    public static bool IsApiKeyPrincipal(ClaimsPrincipal user) =>
        user.FindFirstValue(AuthMethodClaim) == AuthMethodValue;

    public static (string Key, string Prefix, string Hash) Generate()
    {
        var key = KeyPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        return (key, key[..DisplayPrefixLength], Hash(key));
    }

    // Keys are 256 bits of CSPRNG output, so a fast hash is enough — there's nothing to brute-force.
    public static string Hash(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static bool TryGetKey(HttpRequest request, out string key)
    {
        key = string.Empty;
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

        var token = header["Bearer ".Length..].Trim();
        if (!token.StartsWith(KeyPrefix, StringComparison.Ordinal)) return false;

        key = token;
        return true;
    }

    public class Handler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        IDbContextFactory<InventoryContext> dbFactory, SettingsService settings)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!TryGetKey(Request, out var key)) return AuthenticateResult.NoResult();

            var ct = Context.RequestAborted;
            if (!await settings.GetBoolAsync(EnabledSettingKey, ct: ct))
                return AuthenticateResult.Fail("MCP access is disabled.");

            var hash = Hash(key);
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var match = await db.ApiKeys.AsNoTracking()
                .Where(k => k.Hash == hash)
                .Join(db.Users, k => k.UserId, u => u.UserId,
                    (k, u) => new { k.Id, k.Name, k.LastUsedAt, u.UserId, u.UserName, u.Status })
                .FirstOrDefaultAsync(ct);

            if (match is null) return AuthenticateResult.Fail("Invalid API key.");
            if (match.Status != "Active") return AuthenticateResult.Fail("Account is no longer active.");

            var now = DateTime.UtcNow;
            if (match.LastUsedAt is null || now - match.LastUsedAt > LastUsedResolution)
                await db.ApiKeys.Where(k => k.Id == match.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, now), ct);

            // Role is pinned to User regardless of the account's real role: keys never carry admin rights.
            Claim[] claims =
            [
                new("sub", match.UserId.ToString()),
                new("name", match.UserName),
                new("role", "User"),
                new(AuthMethodClaim, AuthMethodValue),
                new(KeyNameClaim, match.Name),
            ];
            var identity = new ClaimsIdentity(claims, Scheme.Name, "name", "role");
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
        }
    }
}
