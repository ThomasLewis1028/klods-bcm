using System.Security.Cryptography;

namespace Klods.Services;

public static class PasswordHasher
{
    private const int Iterations = 100_000;

    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static bool IsValidLength(string password) =>
        password.Length is >= MinLength and <= MaxLength;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
