using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
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

[TestClass]
public class ApiKeyTests
{
    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", "test-secret-key-for-unit-tests-must-be-long-enough");
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
        await ApiKeyTestHelper.SetMcpEnabledAsync(_factory, true);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Key_AuthenticatesOnAllowlistedEndpoint_ButNotElsewhere()
    {
        var (username, jwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (_, key) = await ApiKeyTestHelper.CreateKeyAsync(_client, jwt);

        var whoami = await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/auth/whoami", key);
        Assert.AreEqual(HttpStatusCode.OK, whoami.StatusCode);
        Assert.AreEqual(username, (await whoami.Content.ReadFromJsonAsync<WhoAmI>())!.UserName);

        // Not opted in: profile, and key management itself (a key must not mint more keys).
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/auth/me/", key)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Post, "/api/auth/me/keys", key, new { Name = "x" })).StatusCode);
    }

    [TestMethod]
    public async Task AdminsKey_CannotReachAdminEndpoints()
    {
        var (username, jwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        await ApiKeyTestHelper.UpdateUserAsync(_factory, username, q => q.ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, "Admin")));
        var (_, key) = await ApiKeyTestHelper.CreateKeyAsync(_client, jwt);

        Assert.AreEqual(HttpStatusCode.OK,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/admin/users", jwt)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/admin/users", key)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Put, "/api/admin/mcp-settings", key, new { Enabled = false })).StatusCode);
    }

    [TestMethod]
    public async Task Admin_SeesAndRevokesAnyUsersKey()
    {
        var (adminName, adminJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        await ApiKeyTestHelper.UpdateUserAsync(_factory, adminName, q => q.ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, "Admin")));
        var (ownerName, ownerJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (id, key) = await ApiKeyTestHelper.CreateKeyAsync(_client, ownerJwt);

        var list = await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/admin/api-keys", adminJwt);
        Assert.AreEqual(HttpStatusCode.OK, list.StatusCode);
        var keys = (await list.Content.ReadFromJsonAsync<List<AdminKey>>())!;
        Assert.AreEqual(ownerName, keys.Single(k => k.Id == id).UserName);

        Assert.AreEqual(HttpStatusCode.OK,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Delete, $"/api/admin/api-keys/{id}", adminJwt)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/auth/whoami", key)).StatusCode);
    }

    [TestMethod]
    public async Task RevokedKey_IsRejected_AndOnlyTheOwnerCanRevoke()
    {
        var (_, ownerJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (_, otherJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (id, key) = await ApiKeyTestHelper.CreateKeyAsync(_client, ownerJwt);

        Assert.AreEqual(HttpStatusCode.NotFound,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Delete, $"/api/auth/me/keys/{id}", otherJwt)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/auth/whoami", key)).StatusCode);

        Assert.AreEqual(HttpStatusCode.OK,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Delete, $"/api/auth/me/keys/{id}", ownerJwt)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/auth/whoami", key)).StatusCode);
    }

    [TestMethod]
    public async Task SiteToggleOff_RejectsExistingKeysAndCreation_UntilReEnabled()
    {
        var (_, jwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (_, key) = await ApiKeyTestHelper.CreateKeyAsync(_client, jwt);

        await ApiKeyTestHelper.SetMcpEnabledAsync(_factory, false);
        try
        {
            Assert.AreEqual(HttpStatusCode.Unauthorized,
                (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/auth/whoami", key)).StatusCode);
            Assert.AreEqual(HttpStatusCode.Forbidden,
                (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Post, "/api/auth/me/keys", jwt, new { Name = "x" })).StatusCode);
        }
        finally
        {
            await ApiKeyTestHelper.SetMcpEnabledAsync(_factory, true);
        }

        Assert.AreEqual(HttpStatusCode.OK,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/auth/whoami", key)).StatusCode);
    }

    [TestMethod]
    public async Task SuspendedUsersKey_IsRejected()
    {
        var (username, jwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (_, key) = await ApiKeyTestHelper.CreateKeyAsync(_client, jwt);

        await ApiKeyTestHelper.UpdateUserAsync(_factory, username, q => q.ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "Pending")));

        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/auth/whoami", key)).StatusCode);
    }

    [TestMethod]
    public async Task CreatingPastTheCap_ReturnsBadRequest()
    {
        var (_, jwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        for (var i = 0; i < 5; i++)
            await ApiKeyTestHelper.CreateKeyAsync(_client, jwt);

        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Post, "/api/auth/me/keys", jwt, new { Name = "one too many" })).StatusCode);
    }

    [TestMethod]
    public async Task Writes_ReferencingUnknownCatalogItems_ReturnNotFound()
    {
        var (_, jwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (_, key) = await ApiKeyTestHelper.CreateKeyAsync(_client, jwt);

        Assert.AreEqual(HttpStatusCode.NotFound, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Post,
            "/api/sets/owned", key, new { SetId = "no-such-set", ApplyBricks = false })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Put,
            "/api/mybricks/no-such-part/999999/stock", key, new { Stock = 3 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Put,
            "/api/bricks/owned/no-such-part/999999/notes", key, new { Location = "bin", Notes = (string?)null })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Post,
            "/api/myminifigs/no-such-fig/instances", key)).StatusCode);
    }

    private record WhoAmI(string UserName, string? KeyName);
    private record AdminKey(int Id, string UserName);
}

// Writes aimed at another user's set or minifig copy must report "not found" and leave their data alone.
[TestClass]
public class OwnershipTests
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
    public async Task RemovingAnotherUsersSetCopy_ReturnsNotFound_AndKeepsIt()
    {
        var (ownerName, ownerJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (_, otherJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var setId = $"t{Guid.NewGuid():N}"[..12];
        await SeedAsync(async db =>
        {
            var ownerId = await db.Users.Where(u => u.UserName == ownerName).Select(u => u.UserId).SingleAsync();
            db.Sets.Add(new Set { SetId = setId, Name = "Test", ManualUrl = "", DateModified = DateTime.UtcNow });
            db.SetsOwned.Add(new SetOwned { UserId = ownerId, SetId = setId, SetIndex = 0 });
        });

        Assert.AreEqual(HttpStatusCode.NotFound, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Delete,
            $"/api/sets/owned/{setId}/0?moveStock=true", otherJwt)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Delete,
            $"/api/sets/owned/{setId}/0?moveStock=false", ownerJwt)).StatusCode);
    }

    [TestMethod]
    public async Task SettingPartStockOnAnotherUsersMinifigCopy_ReturnsNotFound()
    {
        var (ownerName, ownerJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (_, otherJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var figId = $"fig-t{Guid.NewGuid():N}"[..16];
        var partNum = $"p{Guid.NewGuid():N}"[..12];
        await SeedAsync(async db =>
        {
            var ownerId = await db.Users.Where(u => u.UserName == ownerName).Select(u => u.UserId).SingleAsync();
            db.Bricks.Add(new Brick { PartNum = partNum, ColorId = "0", Name = "Test part" });
            db.Minifigs.Add(new Minifig { MinifigId = figId, Name = "Test fig", NumParts = 1, DateModified = DateTime.UtcNow });
            db.MinifigBricks.Add(new MinifigBrick { MinifigId = figId, PartNum = partNum, ColorId = "0", Count = 1 });
            db.MinifigOwneds.Add(new MinifigOwned { UserId = ownerId, MinifigId = figId, MinifigIndex = 0 });
        });
        var path = $"/api/myminifigs/{figId}/instances/0/parts/{partNum}/0";

        Assert.AreEqual(HttpStatusCode.NotFound,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Patch, path, otherJwt, new { Stock = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Patch,
            $"/api/myminifigs/{figId}/instances/0/parts/not-in-fig/0", ownerJwt, new { Stock = 1 })).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK,
            (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Patch, path, ownerJwt, new { Stock = 1 })).StatusCode);
    }

    private static async Task SeedAsync(Func<InventoryContext, Task> seed)
    {
        using var scope = _factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InventoryContext>>().CreateDbContext();
        await seed(db);
        await db.SaveChangesAsync();
    }
}

// Any user may read another active user's collection through /api/users/{id}/…; personal location/notes stay private.
[TestClass]
public class UserCollectionTests
{
    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", "test-secret-key-for-unit-tests-must-be-long-enough");
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
        await ApiKeyTestHelper.SetMcpEnabledAsync(_factory, true);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Viewer_SeesOwnersStock_ButNotTheirLocationOrNotes()
    {
        var (ownerName, ownerJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (viewerName, viewerJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var setId = $"t{Guid.NewGuid():N}"[..12];
        var partNum = $"p{Guid.NewGuid():N}"[..12];
        var ownerId = 0;
        await SeedAsync(async db =>
        {
            ownerId = await UserIdAsync(db, ownerName);
            var viewerId = await UserIdAsync(db, viewerName);
            db.Sets.Add(new Set { SetId = setId, Name = "Test", ManualUrl = "", DateModified = DateTime.UtcNow });
            db.Bricks.Add(new Brick { PartNum = partNum, ColorId = "0", Name = "Test part" });
            db.SetBricks.Add(new SetBrick { SetId = setId, PartNum = partNum, ColorId = "0", Count = 4 });
            db.SetsOwned.Add(new SetOwned { UserId = ownerId, SetId = setId, SetIndex = 0, Location = "attic", Notes = "private" });
            db.BrickOwneds.Add(new BrickOwned { UserId = ownerId, PartNum = partNum, ColorId = "0", Stock = 7, Location = "bin 3", Notes = "private" });
            db.BrickOwneds.Add(new BrickOwned { UserId = viewerId, PartNum = partNum, ColorId = "0", Stock = 2 });
        });

        var bricks = await GetJsonAsync($"/api/users/{ownerId}/bricks", viewerJwt);
        Assert.AreEqual(7, (int)bricks.AsArray().Single(b => (string?)b!["partNum"] == partNum)!["stock"]!);

        var stock = await GetJsonAsync($"/api/users/{ownerId}/bricks/{partNum}/0", viewerJwt);
        Assert.AreEqual(7, (int)stock["stock"]!);
        Assert.IsNull(stock["location"]);
        Assert.IsNull(stock["notes"]);

        var sets = await GetJsonAsync($"/api/users/{ownerId}/sets", viewerJwt);
        var instance = sets.AsArray().Single()!["instances"]!.AsArray().Single()!;
        Assert.IsNull(instance["location"]);
        Assert.IsNull(instance["notes"]);

        var bom = await GetJsonAsync($"/api/users/{ownerId}/bom/{setId}/0", viewerJwt);
        Assert.AreEqual(7, (int)bom["bricks"]!.AsArray().Single()!["looseStock"]!);
        Assert.IsNull(bom["location"]);
        Assert.IsNull(bom["notes"]);

        var own = await GetJsonAsync("/api/sets/my-owned", ownerJwt);
        Assert.AreEqual("attic", (string?)own.AsArray().Single()!["instances"]!.AsArray().Single()!["location"]);
    }

    [TestMethod]
    public async Task PendingAndUnknownUsers_ReturnNotFound()
    {
        var (pendingName, _) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (_, viewerJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        await ApiKeyTestHelper.UpdateUserAsync(_factory, pendingName, q => q.ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, "Pending")));
        var pendingId = 0;
        await SeedAsync(async db => pendingId = await UserIdAsync(db, pendingName));

        foreach (var path in new[] { $"/api/users/{pendingId}", $"/api/users/{pendingId}/sets", $"/api/users/{int.MaxValue}/bricks" })
            Assert.AreEqual(HttpStatusCode.NotFound, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, path, viewerJwt)).StatusCode, path);
    }

    [TestMethod]
    public async Task ApiKey_CanReadAnotherUsersCollection()
    {
        var (ownerName, _) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (_, viewerJwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (_, key) = await ApiKeyTestHelper.CreateKeyAsync(_client, viewerJwt);
        var ownerId = 0;
        await SeedAsync(async db => ownerId = await UserIdAsync(db, ownerName));

        foreach (var path in new[] { "/api/users/", $"/api/users/{ownerId}/sets", $"/api/users/{ownerId}/bricks", $"/api/users/{ownerId}/minifigs" })
            Assert.AreEqual(HttpStatusCode.OK, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, path, key)).StatusCode, path);
    }

    private static async Task<JsonNode> GetJsonAsync(string path, string bearer)
    {
        var resp = await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, path, bearer);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, path);
        return (await resp.Content.ReadFromJsonAsync<JsonNode>())!;
    }

    private static Task<int> UserIdAsync(InventoryContext db, string userName) =>
        db.Users.Where(u => u.UserName == userName).Select(u => u.UserId).SingleAsync();

    private static async Task SeedAsync(Func<InventoryContext, Task> seed)
    {
        using var scope = _factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InventoryContext>>().CreateDbContext();
        await seed(db);
        await db.SaveChangesAsync();
    }
}

// Own factory with a tiny read budget so the limit is reachable without hammering the API.
[TestClass]
public class ApiKeyRateLimitTests
{
    private static WebApplicationFactory<Program> _factory = null!;
    private static HttpClient _client = null!;

    [ClassInitialize]
    public static async Task Init(TestContext _)
    {
        Environment.SetEnvironmentVariable("JWT_SECRET", "test-secret-key-for-unit-tests-must-be-long-enough");
        Environment.SetEnvironmentVariable("MCP_RATE_READS_PER_MIN", "2");
        try
        {
            _factory = new WebApplicationFactory<Program>();
            _client = _factory.CreateClient();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_RATE_READS_PER_MIN", null);
        }
        await ApiKeyTestHelper.SetMcpEnabledAsync(_factory, true);
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task KeysOfOneUser_ShareABudget_AndBrowserTrafficIsUnaffected()
    {
        var (_, jwt) = await ApiKeyTestHelper.RegisterAsync(_client);
        var (_, keyA) = await ApiKeyTestHelper.CreateKeyAsync(_client, jwt);
        var (_, keyB) = await ApiKeyTestHelper.CreateKeyAsync(_client, jwt);

        Assert.AreEqual(HttpStatusCode.OK, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/sets/my-owned", keyA)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/sets/my-owned", keyA)).StatusCode);

        var limited = await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/sets/my-owned", keyB);
        Assert.AreEqual(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.IsNotNull(limited.Headers.RetryAfter);

        Assert.AreEqual(HttpStatusCode.OK, (await ApiKeyTestHelper.SendAsync(_client, HttpMethod.Get, "/api/sets/my-owned", jwt)).StatusCode);
    }
}

internal static class ApiKeyTestHelper
{
    public static async Task<(string Username, string Jwt)> RegisterAsync(HttpClient client)
    {
        var username = $"keyuser_{Guid.NewGuid():N}"[..32];
        var resp = await client.PostAsJsonAsync("/api/auth/register", new { Username = username, Password = "testpass123" });
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        return (username, (await resp.Content.ReadFromJsonAsync<TokenBody>())!.Token);
    }

    public static async Task<(int Id, string Key)> CreateKeyAsync(HttpClient client, string jwt)
    {
        var resp = await SendAsync(client, HttpMethod.Post, "/api/auth/me/keys", jwt, new { Name = "test" });
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<CreatedKey>())!;
        return (body.Id, body.Key);
    }

    public static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string bearer, object? body = null)
    {
        using var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        if (body is not null) req.Content = JsonContent.Create(body);
        return await client.SendAsync(req);
    }

    public static async Task SetMcpEnabledAsync(WebApplicationFactory<Program> factory, bool enabled)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<Klods.Services.SettingsService>()
            .SetAsync("mcp.enabled", enabled ? "true" : "false");
    }

    public static async Task UpdateUserAsync(WebApplicationFactory<Program> factory, string username, Func<IQueryable<User>, Task> update)
    {
        using var scope = factory.Services.CreateScope();
        await using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InventoryContext>>().CreateDbContext();
        await update(db.Users.Where(u => u.UserName == username));
    }

    private record TokenBody(string Token);
    private record CreatedKey(int Id, string Key);
}
