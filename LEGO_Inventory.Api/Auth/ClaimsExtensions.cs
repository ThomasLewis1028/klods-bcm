using System.Security.Claims;

namespace LEGO_Inventory.Api.Auth;

public static class ClaimsExtensions
{
    public static int UserId(this HttpContext http) =>
        int.Parse(http.User.FindFirstValue("sub")
            ?? throw new InvalidOperationException("UserId claim missing."));
}
