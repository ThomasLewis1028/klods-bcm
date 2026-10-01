using System.Reflection;
using Klods.Mcp;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

var apiUrl = builder.Configuration["KLODS_API_URL"]
    ?? throw new InvalidOperationException("KLODS_API_URL is not configured (e.g. http://klods_api:8080).");

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<KlodsApiClient>(c => c.BaseAddress = new Uri(apiUrl));

builder.Services.AddAuthentication(KeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, KeyAuthenticationHandler>(KeyAuthenticationHandler.SchemeName, null);
builder.Services.AddAuthorization();

var version =
    (Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
     ?? "0.0.0-dev").Split('+')[0];

builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "klods", Version = version })
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapMcp("/mcp").RequireAuthorization();
app.MapGet("/health", () => Results.Ok("healthy"));

app.Run();
