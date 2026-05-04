using LEGO_Inventory;
using LEGO_Inventory.Components;
using LEGO_Inventory.Database;
using LEGO_Inventory.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Core services ──────────────────────────────────────────────────────────
builder.Services.AddDbContextFactory<InventoryContext>();
builder.Services.AddSingleton<ImageStorageService>();
builder.Services.AddScoped<ImportData>();
builder.Services.AddScoped<UpdateData>();
builder.Services.AddScoped<DeleteData>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("https://rebrickable.com/") });

// ── OAuth (External cookie scheme + optional providers) ────────────────────
const string ExternalScheme = "External";
const int ExternalCookieExpiryMinutes = 10;

var auth = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = ExternalScheme;
}).AddCookie(ExternalScheme, options =>
{
    options.Cookie.Name    = "ExternalLogin";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan  = TimeSpan.FromMinutes(ExternalCookieExpiryMinutes);
});

var enabledProviders = new List<string>();

var googleId     = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
var googleSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
if (!string.IsNullOrEmpty(googleId) && !string.IsNullOrEmpty(googleSecret))
{
    auth.AddGoogle(o => { o.SignInScheme = ExternalScheme; o.ClientId = googleId; o.ClientSecret = googleSecret; });
    enabledProviders.Add("Google");
}

var msId     = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_ID");
var msSecret = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_SECRET");
if (!string.IsNullOrEmpty(msId) && !string.IsNullOrEmpty(msSecret))
{
    auth.AddMicrosoftAccount(o => { o.SignInScheme = ExternalScheme; o.ClientId = msId; o.ClientSecret = msSecret; });
    enabledProviders.Add("Microsoft");
}

var discordId     = Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID");
var discordSecret = Environment.GetEnvironmentVariable("DISCORD_CLIENT_SECRET");
if (!string.IsNullOrEmpty(discordId) && !string.IsNullOrEmpty(discordSecret))
{
    auth.AddDiscord(o => { o.SignInScheme = ExternalScheme; o.ClientId = discordId; o.ClientSecret = discordSecret; });
    enabledProviders.Add("Discord");
}

// Expose which providers are enabled to the app
builder.Services.AddSingleton(new PendingAuthService { EnabledProviders = enabledProviders });

builder.Services.AddCors(options =>
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── Build ──────────────────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

await using var scope = app.Services.CreateAsyncScope();
var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InventoryContext>>();
await using var db = dbFactory.CreateDbContext();
await db.Database.MigrateAsync();

if (!await db.Users.AnyAsync(u => u.Role == "Admin"))
{
    var defaultPassword = builder.Configuration["ADMIN_DEFAULT_PASSWORD"] ?? "admin";
    db.Users.Add(new User
    {
        UserName = "admin",
        PasswordHash = AuthService.HashPassword(defaultPassword),
        Role = "Admin"
    });
    await db.SaveChangesAsync();
    var seedLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    seedLogger.LogWarning("No admin user found — created default admin. Username: admin, Password: {Password}. Change this immediately.", defaultPassword);
}

var imageStorage = app.Services.GetRequiredService<ImageStorageService>();
await imageStorage.InitializeAsync();

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

var minioEndpoint = builder.Configuration["MINIO_ENDPOINT"] ?? "http://minio:9000";
app.MapGet("/media/{**path}", async (string path, IHttpClientFactory factory, CancellationToken ct) =>
{
    try
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync($"{minioEndpoint.TrimEnd('/')}/{path}", ct);
        if (!response.IsSuccessStatusCode) return Results.NotFound();
        var stream = await response.Content.ReadAsStreamAsync(ct);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
        return Results.Stream(stream, contentType);
    }
    catch (OperationCanceledException) { return Results.StatusCode(499); }
    catch { return Results.StatusCode(502); }
});

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
logger.LogInformation("Lego application starting.");

app.Run();
