using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
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
        var username = $"testuser_{Guid.NewGuid():N}";

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
    public async Task GetProviders_ReturnsOk()
    {
        var resp = await _client.GetAsync("/api/auth/providers");
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<string> GetTokenAsync()
    {
        var username = $"testuser_{Guid.NewGuid():N}";
        var resp = await _client.PostAsJsonAsync("/api/auth/register",
            new { Username = username, Password = "testpass123" });
        var body = await resp.Content.ReadFromJsonAsync<TokenResponse>();
        return body!.Token;
    }

    private record TokenResponse(string Token);
}
