using LEGO_Inventory.Components;
using LEGO_Inventory.Database;
using LEGO_Inventory.Services;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Core services ──────────────────────────────────────────────────────────
builder.Services.AddDbContext<InventoryContext>();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("https://rebrickable.com/") });

// ── OAuth (External cookie scheme + optional providers) ────────────────────
const string ExternalScheme = "External";

var auth = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = ExternalScheme;
}).AddCookie(ExternalScheme, options =>
{
    options.Cookie.Name    = "ExternalLogin";
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan  = TimeSpan.FromMinutes(10);
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

// ── Build ──────────────────────────────────────────────────────────────────
var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

await using var scope = app.Services.CreateAsyncScope();
await using var db = scope.ServiceProvider.GetService<InventoryContext>();
await db!.Database.MigrateAsync();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
logger.LogInformation("Lego application starting.");

app.Run();
