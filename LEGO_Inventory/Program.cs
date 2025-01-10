using LEGO_Inventory;
using LEGO_Inventory.Components;
using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<InventoryContext>();

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