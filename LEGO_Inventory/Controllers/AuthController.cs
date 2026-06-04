using System.Security.Claims;
using LEGO_Inventory.Database;
using LEGO_Inventory.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory.Controllers;

[Route("auth")]
public class AuthController(
    PendingAuthService pendingAuth,
    JwtTokenService jwtTokenService,
    IDbContextFactory<InventoryContext> contextFactory) : Controller
{
    private const string ExternalScheme = "External";

    [HttpGet("challenge")]
    public IActionResult Challenge(
        [FromQuery] string provider,
        [FromQuery] int? linkUserId = null)
    {
        var redirectUrl = Url.Action("Finalize", "Auth", new { linkUserId });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, provider);
    }

    [HttpGet("finalize")]
    public async Task<IActionResult> Finalize([FromQuery] int? linkUserId = null)
    {
        var result = await HttpContext.AuthenticateAsync(ExternalScheme);
        if (!result.Succeeded)
            return Redirect("/?auth_error=failed");

        var provider    = result.Properties?.Items[".AuthScheme"] ?? string.Empty;
        var providerKey = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var displayName = result.Principal?.FindFirstValue(ClaimTypes.Name)
                       ?? result.Principal?.FindFirstValue("urn:discord:username")
                       ?? providerKey;

        if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(providerKey))
            return Redirect("/?auth_error=missing_claims");

        await HttpContext.SignOutAsync(ExternalScheme);

        using var context = contextFactory.CreateDbContext();

        var existing = context.UserExternalLogins
            .FirstOrDefault(l => l.Provider == provider && l.ProviderKey == providerKey);

        User user;

        if (linkUserId.HasValue)
        {
            if (existing != null && existing.UserId != linkUserId.Value)
                return Redirect("/profile?link_error=already_linked");

            if (existing == null)
            {
                context.UserExternalLogins.Add(new UserExternalLogin
                {
                    UserId = linkUserId.Value,
                    Provider = provider,
                    ProviderKey = providerKey
                });
                context.SaveChanges();
            }

            user = context.Users.First(u => u.UserId == linkUserId.Value);
        }
        else
        {
            if (existing != null)
            {
                user = context.Users.First(u => u.UserId == existing.UserId);
            }
            else
            {
                var username = displayName;
                var counter  = 1;
                while (context.Users.Any(u => u.UserName == username))
                    username = $"{displayName}{counter++}";

                user = new User { UserName = username, PasswordHash = string.Empty };
                context.Users.Add(user);
                context.SaveChanges();

                context.UserExternalLogins.Add(new UserExternalLogin
                {
                    UserId = user.UserId,
                    Provider = provider,
                    ProviderKey = providerKey
                });
                context.SaveChanges();
            }
        }

        var jwt   = jwtTokenService.Generate(user);
        var token = pendingAuth.Store(jwt);
        return Redirect($"/auth/complete?token={token}&linked={linkUserId.HasValue}");
    }
}
