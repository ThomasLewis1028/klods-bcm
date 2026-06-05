using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Klods.Database;
using Microsoft.IdentityModel.Tokens;

namespace Klods.Api.Auth;

public class JwtService(IConfiguration config)
{
    private SigningCredentials Credentials()
    {
        var secret = config["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is not configured.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        return new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public string Generate(User user)
    {
        // Use short JWT claim names so they round-trip cleanly with MapInboundClaims = false.
        Claim[] claims =
        [
            new("sub",  user.UserId.ToString()),
            new("name", user.UserName),
            new("role", user.Role),
        ];

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: Credentials());

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
