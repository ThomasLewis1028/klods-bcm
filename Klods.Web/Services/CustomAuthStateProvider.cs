using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Klods.Services;

public class CustomAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private readonly AuthService _authService;

    public CustomAuthStateProvider(AuthService authService)
    {
        _authService = authService;
        _authService.OnChange += NotifyChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        // Don't report "anonymous" until the token has had a chance to rehydrate from browser
        // storage, otherwise protected routes redirect to not-authorized during the restore window.
        if (_authService.CurrentUser is null && !_authService.IsSessionRestored)
            await _authService.WaitForSessionRestoreAsync();

        if (_authService.CurrentUser is null)
            return Anonymous;

        var identity = new ClaimsIdentity(
        [
            new Claim("sub",  _authService.CurrentUser.UserId.ToString()),
            new Claim("name", _authService.CurrentUser.UserName),
            new Claim("role", _authService.CurrentUser.Role),
        ], "Custom",
        nameType: "name",
        roleType: "role");

        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    private void NotifyChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose() => _authService.OnChange -= NotifyChanged;
}
