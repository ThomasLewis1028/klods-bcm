using System.Security.Claims;
using LEGO_Inventory.Database;
using LEGO_Inventory.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace LEGO_Inventory.Controllers;

[Route("auth")]
public class AuthController(PendingAuthService pendingAuth) : Controller
{
    private const string ExternalScheme = "External";

    /// <summary>
    /// Initiates an OAuth challenge for the given provider.
    /// linkUserId: when set, links the external account to an existing user instead of creating one.
    /// </summary>
    [HttpGet("challenge")]
    public IActionResult Challenge(
        [FromQuery] string provider,
        [FromQuery] int? linkUserId = null)
    {
        var redirectUrl = Url.Action("Finalize", "Auth", new { linkUserId });
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        return Challenge(properties, provider);
    }

    /// <summary>
    /// Called by the OAuth middleware after a successful external login.
    /// Finds or creates the internal user, then hands off to the Blazor circuit via a token.
    /// </summary>
    [HttpGet("finalize")]
    public async Task<IActionResult> Finalize([FromQuery] int? linkUserId = null)
    {
        var result = await HttpContext.AuthenticateAsync(ExternalScheme);
        if (!result.Succeeded)
            return Redirect("/?auth_error=failed");

        var provider   = result.Properties?.Items[".AuthScheme"] ?? string.Empty;
        var providerKey = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var displayName = result.Principal?.FindFirstValue(ClaimTypes.Name)
                       ?? result.Principal?.FindFirstValue("urn:discord:username")
                       ?? providerKey;

        if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(providerKey))
            return Redirect("/?auth_error=missing_claims");

        // Clean up the temporary external cookie
        await HttpContext.SignOutAsync(ExternalScheme);

        using var context = new InventoryContext();

        var existing = context.UserExternalLogins
            .FirstOrDefault(l => l.Provider == provider && l.ProviderKey == providerKey);

        int userId;

        if (linkUserId.HasValue)
        {
            // Linking an external account to an already-signed-in user
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

            userId = linkUserId.Value;
        }
        else
        {
            // Normal sign-in: find or create a user
            if (existing != null)
            {
                userId = existing.UserId;
            }
            else
            {
                // Ensure the username is unique
                var username = displayName;
                var counter  = 1;
                while (context.Users.Any(u => u.UserName == username))
                    username = $"{displayName}{counter++}";

                var user = new User { UserName = username, PasswordHash = string.Empty };
                context.Users.Add(user);
                context.SaveChanges();

                context.UserExternalLogins.Add(new UserExternalLogin
                {
                    UserId = user.UserId,
                    Provider = provider,
                    ProviderKey = providerKey
                });
                context.SaveChanges();

                userId = user.UserId;
            }
        }

        var token = pendingAuth.Store(userId);
        return Redirect($"/auth/complete?token={token}&linked={linkUserId.HasValue}");
    }
}
