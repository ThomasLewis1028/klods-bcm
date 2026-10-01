using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Klods.Mcp;

/// <summary>
/// Rejects MCP requests whose key the API doesn't accept, so clients get a real 401 instead of a server
/// that lists tools and then fails every call. A successful check is cached briefly to avoid an extra API
/// round-trip per request; tool calls still go through the API with the key, so revocation applies to them
/// immediately regardless of this cache.
/// </summary>
public class KeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, IHttpClientFactory httpClientFactory, IMemoryCache cache)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "KlodsKey";
    private const string KeyPrefix = "klods_";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var key = header["Bearer ".Length..].Trim();
        if (!key.StartsWith(KeyPrefix, StringComparison.Ordinal)) return AuthenticateResult.Fail("Not a Klods API key.");

        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        if (!cache.TryGetValue(cacheKey, out string? userName))
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/whoami");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var resp = await httpClientFactory.CreateClient(nameof(KlodsApiClient)).SendAsync(req, Context.RequestAborted);

            if (resp.StatusCode == HttpStatusCode.Unauthorized) return AuthenticateResult.Fail("API key rejected.");
            resp.EnsureSuccessStatusCode();

            userName = (await resp.Content.ReadFromJsonAsync<WhoAmI>(Context.RequestAborted))?.UserName ?? "";
            cache.Set(cacheKey, userName, CacheFor);
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, userName ?? "")], SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Bearer";
        return Task.CompletedTask;
    }

    private record WhoAmI(string UserName);
}
