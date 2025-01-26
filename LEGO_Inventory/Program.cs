using LEGO_Inventory;
using LEGO_Inventory.Components;
using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MudBlazor.Services;
var POSTGRES_HOST = Environment.GetEnvironmentVariable("POSTGRES_HOST");
var POSTGRES_USER = Environment.GetEnvironmentVariable("POSTGRES_USER");
var POSTGRES_PASSWORD = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD");
var POSTGRES_DB = Environment.GetEnvironmentVariable("POSTGRES_DB");


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<InventoryContext>();
var dbContextFactory = new PooledDbContextFactory<InventoryContext>(
    new DbContextOptionsBuilder<InventoryContext>()
        .UseNpgsql($"Host={POSTGRES_HOST};Database={POSTGRES_DB};Username={POSTGRES_USER};Password={POSTGRES_PASSWORD}")
        .Options);


builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddMudServices();

builder.Services.AddHttpClient();

builder.Services.AddScoped(sp =>
    new HttpClient
    {
        BaseAddress = new Uri("https://rebrickable.com/")
    });

builder.Services.AddCors(options =>
    options.AddPolicy("AllowAll",
        builder2 => builder2
            .AllowCredentials()
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader()));

builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}


await using var scope = app.Services.CreateAsyncScope();
await using var db = scope.ServiceProvider.GetService<InventoryContext>();
await db.Database.MigrateAsync();

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

var logger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger<Program>();
logger.LogInformation("Lego application starting.");

app.Run();
logger.LogInformation("Lego application running.");