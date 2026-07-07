using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Klods.Api.Auth;
using Klods.Api.Endpoints;
using Klods.Database;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Threading.RateLimiting;

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
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<SetUpdateService>();
builder.Services.AddHostedService<Klods.Api.SetUpdateBackgroundService>();
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
if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
    throw new InvalidOperationException(
        "JWT_SECRET is too short (must be at least 32 bytes) — a weak secret lets anyone forge tokens, " +
        "including admin ones. Generate one with: openssl rand -base64 48");

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
        // JWTs are long-lived (30 days) and carry Role/Status as of issuance. Re-check both against
        // the DB on every request so a suspension or role change takes effect immediately instead of
        // waiting for the token to expire.
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (!int.TryParse(context.Principal?.FindFirstValue("sub"), out var userId))
                {
                    context.Fail("Invalid token.");
                    return;
                }

                var dbFactory = context.HttpContext.RequestServices
                    .GetRequiredService<IDbContextFactory<InventoryContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var user = await db.Users.AsNoTracking()
                    .Where(u => u.UserId == userId)
                    .Select(u => new { u.Status, u.Role })
                    .FirstOrDefaultAsync();

                if (user is null || user.Status != "Active")
                {
                    context.Fail("Account is no longer active.");
                    return;
                }

                var identity = (ClaimsIdentity)context.Principal!.Identity!;
                foreach (var roleClaim in identity.FindAll("role").ToList())
                    identity.RemoveClaim(roleClaim);
                identity.AddClaim(new Claim("role", user.Role));
            }
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

// ── Rate limiting ──────────────────────────────────────────────────────────
// Login/register are brute-forceable and spammable without this. Keyed on remote IP — note this
// trusts X-Forwarded-For as configured below, so it's only meaningful behind a proxy that strips
// client-supplied forwarding headers (or with no proxy at all).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

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
// A direct client can spoof X-Forwarded-* freely unless we only trust a configured proxy — that
// would defeat IP-based rate limiting and let a client dictate its own request host. When
// TRUSTED_PROXY_NETWORKS (comma-separated CIDRs) isn't set, leave ASP.NET Core's default
// (loopback only) in place rather than trusting every client.
var trustedProxyNetworks = builder.Configuration["TRUSTED_PROXY_NETWORKS"];
if (!string.IsNullOrWhiteSpace(trustedProxyNetworks))
{
    forwardedHeadersOptions.KnownIPNetworks.Clear();
    forwardedHeadersOptions.KnownProxies.Clear();
    foreach (var cidr in trustedProxyNetworks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        forwardedHeadersOptions.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(cidr));
}
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseCors();
app.UseRateLimiter();
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
app.MapExport();
app.MapAdmin();
app.MapUsers();
app.MapHome();
app.MapNotifications();

var appVersion =
    (Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
     ?? "0.0.0-dev").Split('+')[0];
app.MapGet("/health", () => Results.Ok("healthy"));
app.MapGet("/version", () => Results.Ok(new { version = appVersion }));

app.Run();

public partial class Program;
