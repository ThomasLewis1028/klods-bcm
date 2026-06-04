using System.Text;
using LEGO_Inventory.Api.Auth;
using LEGO_Inventory.Api.Endpoints;
using LEGO_Inventory.Database;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── Database ───────────────────────────────────────────────────────────────
builder.Services.AddDbContextFactory<InventoryContext>();

// ── Business services ──────────────────────────────────────────────────────
builder.Services.AddScoped<RebrickableApi>();
builder.Services.AddScoped<ImportData>();
builder.Services.AddScoped<UpdateData>();
builder.Services.AddScoped<DeleteData>();
builder.Services.AddSingleton<ImageStorageService>();
builder.Services.AddHttpClient();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("https://rebrickable.com/") });

// ── JWT ────────────────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["JWT_SECRET"]
    ?? throw new InvalidOperationException("JWT_SECRET is not configured.");

builder.Services.AddScoped<JwtService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            // Match the short claim names used in JwtService.Generate().
            RoleClaimType = "role",
            NameClaimType = "name",
        };
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
    var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<InventoryContext>>();
    await using var ctx = db.CreateDbContext();
    await ctx.Database.MigrateAsync();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

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
