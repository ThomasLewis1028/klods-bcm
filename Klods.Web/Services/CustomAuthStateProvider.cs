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

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (_authService.CurrentUser is null)
            return Task.FromResult(Anonymous);

        var identity = new ClaimsIdentity(
        [
            new Claim("sub",  _authService.CurrentUser.UserId.ToString()),
            new Claim("name", _authService.CurrentUser.UserName),
            new Claim("role", _authService.CurrentUser.Role),
        ], "Custom",
        nameType: "name",
        roleType: "role");

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
    }

    private void NotifyChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    public void Dispose() => _authService.OnChange -= NotifyChanged;
}
