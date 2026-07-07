using Klods;
using Klods.Components;
using Klods.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using MudBlazor.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Persist DataProtection keys so antiforgery tokens survive container restarts. Only when a
// path is configured (production/compose); locally it falls back to the default per-user
// store, which is fine for dev. If the configured path isn't writable (e.g. a root-owned
// volume), fall back to ephemeral keys rather than failing every request — the site stays up,
// keys just won't survive a restart.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Klods.Web");
var dpKeysPath = builder.Configuration["DATAPROTECTION_KEYS_PATH"];
if (!string.IsNullOrWhiteSpace(dpKeysPath))
{
    try
    {
        Directory.CreateDirectory(dpKeysPath);
        var probe = Path.Combine(dpKeysPath, ".write-probe");
        File.WriteAllText(probe, string.Empty);
        File.Delete(probe);
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(
            $"DataProtection: keys path '{dpKeysPath}' is not writable ({ex.Message}); " +
            "falling back to ephemeral keys (they will not survive a restart).");
    }
}

builder.Services.AddSingleton<ImageStorageService>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();

var apiBase = builder.Configuration["API_BASE_URL"] ?? "http://klods_api:8080";
builder.Services.AddHttpClient("api", c => c.BaseAddress = new Uri(apiBase));
builder.Services.AddScoped<ApiClient>();

// A direct client can spoof X-Forwarded-* freely unless we only trust a configured proxy — that
// would defeat IP-based rate limiting. When TRUSTED_PROXY_NETWORKS (comma-separated CIDRs) isn't
// set, leave ASP.NET Core's default (loopback only) in place rather than trusting every client.
var trustedProxyNetworks = builder.Configuration["TRUSTED_PROXY_NETWORKS"];
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    if (string.IsNullOrWhiteSpace(trustedProxyNetworks)) return;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    foreach (var cidr in trustedProxyNetworks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
});

// Anonymous, unbounded-key read-through cache (/img?u=) — bound the rate so a scripted loop of
// distinct query strings against the same host can't be used to hammer outbound fetches / MinIO writes.
// Limit is generous because ordinary browsing legitimately bursts through many images at once (a single
// paginated grid page can carry 25+ set/brick images, and this endpoint also serves inline <img> tags
// throughout the catalog, not just one image per page load).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("img", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 500,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
// Auth here is entirely client-side (JWT in browser storage, checked via CustomAuthStateProvider) —
// there's no HTTP-level authentication scheme. AddRazorComponents() registers just enough
// authorization plumbing that ASP.NET Core auto-adds the HTTP AuthorizationMiddleware, which would
// otherwise enforce [Authorize] page attributes as endpoint metadata on a hard refresh (before any
// circuit exists) and crash calling ChallengeAsync with no IAuthenticationService registered.
// AllowAnonymous() disables that HTTP-level check; Blazor's own AuthorizeRouteView (plus each
// protected page's redirect-to-/not-authorized on session restore) still enforces access client-side.
app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AllowAnonymous();

var minioEndpoint = builder.Configuration["MINIO_ENDPOINT"] ?? "http://minio:9000";
app.MapGet("/media/{**path}", async (string path, IHttpClientFactory factory, CancellationToken ct) =>
{
    try
    {
        var client   = factory.CreateClient();
        var response = await client.GetAsync($"{minioEndpoint.TrimEnd('/')}/{path}", ct);
        if (!response.IsSuccessStatusCode) return Results.NotFound();
        var stream      = await response.Content.ReadAsStreamAsync(ct);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
        return Results.Stream(stream, contentType);
    }
    catch (OperationCanceledException) { return Results.StatusCode(499); }
    catch { return Results.StatusCode(502); }
});

// Read-through image cache: fetches a Rebrickable CDN image, stores it in MinIO on first access,
// then serves from MinIO. Lets imports keep just the remote URL and materialize lazily on demand.
app.MapGet("/img", async (string u, ImageStorageService img, HttpContext ctx, CancellationToken ct) =>
{
    var result = await img.GetThroughCacheAsync(u, ct);
    if (result is null) return Results.NotFound();
    ctx.Response.Headers.CacheControl = "public, max-age=2592000, immutable";
    return Results.File(result.Value.Bytes, result.Value.ContentType);
}).RequireRateLimiting("img");

app.MapGet("/health", () => Results.Ok("healthy"));

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
logger.LogInformation("Lego application starting.");

app.Run();
