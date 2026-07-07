using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Klods;
using Klods.Database;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Klods.Tests;

[TestClass]
public class ApiTests
{
    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        // Program.cs reads JWT_SECRET from configuration at startup — before WebApplicationFactory's
        // in-memory config would apply. Injecting it via ConfigureAppConfiguration would only reach
        // request-time reads (the token signer), not the startup validation key, so the two would use
        // different secrets and authenticated requests would 401. Set it as a real env var, which
        // both the startup read and request-time reads see, before the host builds.
        Environment.SetEnvironmentVariable("JWT_SECRET", "test-secret-key-for-unit-tests-must-be-long-enough");

        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Login_InvalidCredentials_ReturnsUnauthorized()
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Username = "nobody", Password = "wrong" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task Register_ThenLogin_ReturnsToken()
    {
        var username = $"testuser_{Guid.NewGuid():N}"[..32];

        var registerResp = await _client.PostAsJsonAsync("/api/auth/register",
            new { Username = username, Password = "testpass123" });
        Assert.AreEqual(HttpStatusCode.OK, registerResp.StatusCode);

        var token = (await registerResp.Content.ReadFromJsonAsync<TokenResponse>())?.Token;
        Assert.IsNotNull(token);

        var loginResp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Username = username, Password = "testpass123" });
        Assert.AreEqual(HttpStatusCode.OK, loginResp.StatusCode);
    }

    [TestMethod]
    public async Task Register_UsernameTooLong_ReturnsBadRequest()
    {
        var username = new string('a', 41);
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { Username = username, Password = "testpass123" });
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GetProviders_ReturnsOk()
    {
        var resp = await _client.GetAsync("/api/auth/providers");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task SuspendedUser_ExistingToken_ReturnsUnauthorized()
    {
        var username = $"testuser_{Guid.NewGuid():N}"[..32];
        var registerResp = await _client.PostAsJsonAsync("/api/auth/register",
            new { Username = username, Password = "testpass123" });
        var token = (await registerResp.Content.ReadFromJsonAsync<TokenResponse>())?.Token;
        Assert.IsNotNull(token);

        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/sets/"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await _client.SendAsync(req);
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        }

        // Simulates an admin suspending the user mid-session — their already-issued token
        // must stop working immediately rather than staying valid until it expires.
        using (var scope = _factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InventoryContext>>();
            await using var db = dbFactory.CreateDbContext();
            await db.Users.Where(u => u.UserName == username)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "Pending"));
        }

        using (var req = new HttpRequestMessage(HttpMethod.Get, "/api/sets/"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var resp = await _client.SendAsync(req);
            Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
    }

    [TestMethod]
    public async Task ChangePicture_ArbitraryUrl_ReturnsBadRequest()
    {
        var token = await GetTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Patch, "/api/auth/me/picture")
        {
            Content = JsonContent.Create(new { Url = "https://evil.example.com/tracker.png" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task ChangePicture_CatalogUrl_ReturnsOk()
    {
        var token = await GetTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Patch, "/api/auth/me/picture")
        {
            Content = JsonContent.Create(new { Url = "https://cdn.rebrickable.com/media/fig.jpg" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task ChangePicture_Null_RemovesPicture_ReturnsOk()
    {
        var token = await GetTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Patch, "/api/auth/me/picture")
        {
            Content = JsonContent.Create(new { Url = (string?)null })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Auth-required endpoints ───────────────────────────────────────────────

    [TestMethod]
    public async Task GetSets_Unauthenticated_ReturnsUnauthorized()
    {
        var resp = await _client.GetAsync("/api/sets/");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task GetSets_Authenticated_ReturnsOk()
    {
        var token = await GetTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/sets/");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task BrickNotes_LocationTooLong_ReturnsBadRequest()
    {
        var token = await GetTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/bricks/owned/3001/1/notes")
        {
            Content = JsonContent.Create(new { Location = new string('a', 101), Notes = (string?)null })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task BrickNotes_WithinLimits_ReturnsOk()
    {
        // BrickOwned has a required FK to Brick(PartNum, ColorId) — seed one so the upsert succeeds.
        var partNum = $"testpart_{Guid.NewGuid():N}"[..20];
        using (var scope = _factory.Services.CreateScope())
        {
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InventoryContext>>();
            await using var db = dbFactory.CreateDbContext();
            db.Bricks.Add(new Brick { PartNum = partNum, ColorId = "1", Name = "Test Part", ColorName = "Black", HexColor = "000000" });
            await db.SaveChangesAsync();
        }

        var token = await GetTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Put, $"/api/bricks/owned/{partNum}/1/notes")
        {
            Content = JsonContent.Create(new { Location = "Bin 4", Notes = "Some notes" })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task MyBrickStock_Negative_ReturnsBadRequest()
    {
        var token = await GetTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Put, "/api/mybricks/3001/1/stock")
        {
            Content = JsonContent.Create(new { Stock = -5 })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task AddOwnedMinifig_ExcessiveCount_ReturnsBadRequest()
    {
        var token = await GetTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/minifigs/owned")
        {
            Content = JsonContent.Create(new { MinifigId = "fig-000001", Count = 50_000 })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Cached so tests that just need "a" logged-in user share one registration instead of each
    // burning a /register call — they all hit the same rate-limit bucket (one shared HttpClient).
    private static string? _cachedToken;

    private static async Task<string> GetTokenAsync()
    {
        if (_cachedToken is not null) return _cachedToken;

        var username = $"testuser_{Guid.NewGuid():N}"[..32];
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { Username = username, Password = "testpass123" });
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        return _cachedToken = body!.Token;
    }

    private record TokenResponse(string Token);
}

// Own WebApplicationFactory (own DI container) so this class's requests don't share a rate-limiter
// bucket with ApiTests — tripping the limit here shouldn't 429 an unrelated test running elsewhere.
[TestClass]
public class RateLimitTests
{
    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static void Init(TestContext _)
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", "test-secret-key-for-unit-tests-must-be-long-enough");
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Login_ExceedsRateLimit_Returns429()
    {
        HttpResponseMessage? last = null;
        for (var i = 0; i < 15; i++)
            last = await _client.PostAsJsonAsync("/api/auth/login", new { Username = "nobody", Password = "wrong" });

        Assert.AreEqual(HttpStatusCode.TooManyRequests, last!.StatusCode);
    }
}
