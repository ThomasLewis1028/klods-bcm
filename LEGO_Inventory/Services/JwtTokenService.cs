using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LEGO_Inventory.Database;

namespace LEGO_Inventory.Services;

/// <summary>
/// Generates JWTs for OAuth-authenticated users using the same secret and claim
/// names as LEGO_Inventory.Api's JwtService, so the API can validate them.
/// </summary>
public class JwtTokenService(IConfiguration config)
{
    public string Generate(User user)
    {
        var secret = config["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is not configured.");
        var exp = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();

        var header  = B64(JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" }));
        var payload = B64(JsonSerializer.Serialize(new { sub = user.UserId.ToString(), name = user.UserName, role = user.Role, exp }));
        var signing = $"{header}.{payload}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = B64(hmac.ComputeHash(Encoding.UTF8.GetBytes(signing)));

        return $"{signing}.{sig}";
    }

    private static string B64(string s) => B64(Encoding.UTF8.GetBytes(s));
    private static string B64(byte[] b) => Convert.ToBase64String(b).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
