using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LEGO_Inventory.Database;
using Microsoft.IdentityModel.Tokens;

namespace LEGO_Inventory.Api.Auth;

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
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Role, user.Role),
        ];

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: Credentials());

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
