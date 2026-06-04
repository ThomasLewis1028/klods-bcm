using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory.Api.Endpoints;

public static class OAuthEndpoints
{
    private const string ExternalScheme = "External";

    public static void MapOAuth(this IEndpointRouteBuilder app)
    {
        // Browser-navigated — initiates the external provider challenge.
        app.MapGet("/auth/challenge", (string provider, int? link_user_id) =>
        {
            // RedirectUri is where ASP.NET Core sends us after validating the external callback.
            var properties = new AuthenticationProperties { RedirectUri = "/auth/finalize" };
            if (link_user_id.HasValue)
                properties.Items["link_user_id"] = link_user_id.Value.ToString();
            return Results.Challenge(properties, [provider]);
        });

        // Browser-navigated — OAuth provider posts back here.
        app.MapGet("/auth/finalize", async (
            HttpContext ctx,
            IDbContextFactory<InventoryContext> dbFactory,
            JwtService jwtService,
            PendingAuthService pending,
            IConfiguration config) =>
        {
            var result = await ctx.AuthenticateAsync(ExternalScheme);
            if (!result.Succeeded)
                return Results.Redirect(BlazorUrl(config, "/?auth_error=failed"));

            var provider    = result.Properties?.Items[".AuthScheme"] ?? string.Empty;
            var providerKey = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
            var displayName = result.Principal?.FindFirstValue(ClaimTypes.Name)
                           ?? result.Principal?.FindFirstValue("urn:discord:username")
                           ?? providerKey;

            if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(providerKey))
                return Results.Redirect(BlazorUrl(config, "/?auth_error=missing_claims"));

            result.Properties!.Items.TryGetValue("link_user_id", out var linkUserIdStr);
            var linkUserId = int.TryParse(linkUserIdStr, out var id) ? (int?)id : null;

            await ctx.SignOutAsync(ExternalScheme);

            await using var db = dbFactory.CreateDbContext();
            User user;

            if (linkUserId.HasValue)
            {
                var existing = await db.UserExternalLogins
                    .FirstOrDefaultAsync(l => l.Provider == provider && l.ProviderKey == providerKey);

                if (existing != null && existing.UserId != linkUserId.Value)
                    return Results.Redirect(BlazorUrl(config, "/profile?link_error=already_linked"));

                if (existing == null)
                {
                    db.UserExternalLogins.Add(new UserExternalLogin
                    {
                        UserId      = linkUserId.Value,
                        Provider    = provider,
                        ProviderKey = providerKey
                    });
                    await db.SaveChangesAsync();
                }

                user = await db.Users.FirstAsync(u => u.UserId == linkUserId.Value);
            }
            else
            {
                var existing = await db.UserExternalLogins
                    .FirstOrDefaultAsync(l => l.Provider == provider && l.ProviderKey == providerKey);

                if (existing != null)
                {
                    user = await db.Users.FirstAsync(u => u.UserId == existing.UserId);
                }
                else
                {
                    var username = displayName;
                    var counter  = 1;
                    while (await db.Users.AnyAsync(u => u.UserName == username))
                        username = $"{displayName}{counter++}";

                    user = new User { UserName = username, PasswordHash = string.Empty };
                    db.Users.Add(user);
                    await db.SaveChangesAsync();

                    db.UserExternalLogins.Add(new UserExternalLogin
                    {
                        UserId      = user.UserId,
                        Provider    = provider,
                        ProviderKey = providerKey
                    });
                    await db.SaveChangesAsync();
                }
            }

            var jwt   = jwtService.Generate(user);
            var token = pending.Store(jwt);
            return Results.Redirect(BlazorUrl(config, $"/auth/complete?token={token}&linked={linkUserId.HasValue}"));
        });

        // JSON API — client exchanges the one-time token for a JWT.
        app.MapGroup("/api/auth")
            .MapGet("/exchange/{token}", (string token, PendingAuthService pending) =>
            {
                var jwt = pending.Consume(token);
                return jwt is not null ? Results.Ok(new TokenResponse(jwt)) : Results.NotFound();
            });

        // JSON API — returns the list of enabled OAuth providers.
        app.MapGroup("/api/auth")
            .MapGet("/providers", (PendingAuthService pending) =>
                Results.Ok(pending.EnabledProviders));
    }

    private static string BlazorUrl(IConfiguration config, string path)
    {
        var origin = config["BLAZOR_ORIGIN"] ?? "http://localhost:8080";
        return $"{origin.TrimEnd('/')}{path}";
    }

    public record TokenResponse(string Token);
}
