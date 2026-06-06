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
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

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

var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<Program>();
logger.LogInformation("Lego application starting.");

app.Run();
