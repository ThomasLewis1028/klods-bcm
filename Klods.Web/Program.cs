using Klods;
using Klods.Components;
using Klods.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<ImageStorageService>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();

var apiBase = builder.Configuration["API_BASE_URL"] ?? "http://klods_api:8080";
builder.Services.AddHttpClient("api", c => c.BaseAddress = new Uri(apiBase));
builder.Services.AddScoped<ApiClient>();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
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
});

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
logger.LogInformation("Lego application starting.");

app.Run();
