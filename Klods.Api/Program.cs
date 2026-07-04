using System.Security.Cryptography;
using System.Text;
using Klods.Api.Auth;
using Klods.Api.Endpoints;
using Klods.Database;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Database ───────────────────────────────────────────────────────────────
builder.Services.AddDbContextFactory<InventoryContext>();

// ── Business services ──────────────────────────────────────────────────────
builder.Services.AddScoped<RebrickableApi>();
builder.Services.AddScoped<ImportData>();
builder.Services.AddScoped<BulkImportService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<RssUpdateService>();
builder.Services.AddHostedService<Klods.Api.RssBackgroundService>();
builder.Services.AddScoped<UpdateData>();
builder.Services.AddScoped<DeleteData>();

// Bulk catalog upload can be large (gzipped CSVs); raise the multipart limit.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
    o.MultipartBodyLengthLimit = 512L * 1024 * 1024);
builder.Services.AddSingleton<ImageStorageService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("https://rebrickable.com/") });

// ── JWT ────────────────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException("JWT_SECRET is not configured.");

builder.Services.AddScoped<JwtService>();

// ── Authentication: JWT Bearer (API default) + External cookie (OAuth flow) ──
const string ExternalScheme = "External";

var authBuilder = builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = false,
            ValidateAudience         = false,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            RoleClaimType            = "role",
            NameClaimType            = "name",
        };
    })
    .AddCookie(ExternalScheme, options =>
    {
        options.Cookie.Name     = "ExternalLogin";
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan  = TimeSpan.FromMinutes(10);
    });

var enabledProviders = new List<string>();

var googleId     = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
var googleSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
if (!string.IsNullOrEmpty(googleId) && !string.IsNullOrEmpty(googleSecret))
{
    authBuilder.AddGoogle(o => { o.SignInScheme = ExternalScheme; o.ClientId = googleId; o.ClientSecret = googleSecret; });
    enabledProviders.Add("Google");
}

var msId     = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_ID");
var msSecret = Environment.GetEnvironmentVariable("MICROSOFT_CLIENT_SECRET");
if (!string.IsNullOrEmpty(msId) && !string.IsNullOrEmpty(msSecret))
{
    authBuilder.AddMicrosoftAccount(o => { o.SignInScheme = ExternalScheme; o.ClientId = msId; o.ClientSecret = msSecret; });
    enabledProviders.Add("Microsoft");
}

var discordId     = Environment.GetEnvironmentVariable("DISCORD_CLIENT_ID");
var discordSecret = Environment.GetEnvironmentVariable("DISCORD_CLIENT_SECRET");
if (!string.IsNullOrEmpty(discordId) && !string.IsNullOrEmpty(discordSecret))
{
    authBuilder.AddDiscord(o => { o.SignInScheme = ExternalScheme; o.ClientId = discordId; o.ClientSecret = discordSecret; });
    enabledProviders.Add("Discord");
}

var githubId     = Environment.GetEnvironmentVariable("GITHUB_CLIENT_ID");
var githubSecret = Environment.GetEnvironmentVariable("GITHUB_CLIENT_SECRET");
if (!string.IsNullOrEmpty(githubId) && !string.IsNullOrEmpty(githubSecret))
{
    authBuilder.AddGitHub(o => { o.SignInScheme = ExternalScheme; o.ClientId = githubId; o.ClientSecret = githubSecret; });
    enabledProviders.Add("GitHub");
}

builder.Services.AddSingleton(new PendingAuthService { EnabledProviders = enabledProviders });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
});

// ── CORS ───────────────────────────────────────────────────────────────────
var blazorOrigin = builder.Configuration["BLAZOR_ORIGIN"] ?? "http://localhost:8080";
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p => p.WithOrigins(blazorOrigin).AllowAnyMethod().AllowAnyHeader()));

// ── Build ──────────────────────────────────────────────────────────────────
var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InventoryContext>>();
    await using var ctx = dbFactory.CreateDbContext();
    await ctx.Database.MigrateAsync();

    if (!await ctx.Users.AnyAsync(u => u.Role == "Admin"))
    {
        var adminUsername = builder.Configuration["ADMIN_USERNAME"] ?? "admin";
        var configuredPassword = builder.Configuration["ADMIN_DEFAULT_PASSWORD"];

        // No insecure default: if a password wasn't supplied, generate a strong random one and
        // surface it once in the logs. Charset excludes easily-confused characters (0/O, 1/l/I).
        var generated = string.IsNullOrWhiteSpace(configuredPassword);
        var adminPassword = generated
            ? RandomNumberGenerator.GetString("abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789", 20)
            : configuredPassword!;

        ctx.Users.Add(new User
        {
            UserName     = adminUsername,
            PasswordHash = PasswordHasher.Hash(adminPassword),
            Role         = "Admin"
        });
        await ctx.SaveChangesAsync();

        var seedLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        if (generated)
            seedLogger.LogWarning(
                "No admin user found — created admin '{Username}' with a generated password: {Password}\n" +
                "Save it now and change it after signing in. Set ADMIN_DEFAULT_PASSWORD to choose your own.",
                adminUsername, adminPassword);
        else
            seedLogger.LogWarning(
                "No admin user found — created admin '{Username}' from ADMIN_DEFAULT_PASSWORD. Change it after signing in.",
                adminUsername);
    }
}

var imageStorage = app.Services.GetRequiredService<ImageStorageService>();
await imageStorage.InitializeAsync();

var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost
};
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapOAuth();
app.MapAuth();
app.MapAuthProfile();
app.MapSets();
app.MapMinifigs();
app.MapBricks();
app.MapMyCatalog();
app.MapBom();
app.MapAdmin();
app.MapUsers();
app.MapHome();

app.Run();

public partial class Program;
