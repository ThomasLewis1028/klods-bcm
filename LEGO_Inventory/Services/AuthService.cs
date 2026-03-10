using System.Security.Cryptography;
using LEGO_Inventory.Database;

namespace LEGO_Inventory.Services;

public class AuthService
{
    public User? CurrentUser { get; private set; }
    public event Action? OnChange;

    public bool Login(string username, string password)
    {
        using var context = new InventoryContext();
        var user = context.Users.FirstOrDefault(u => u.UserName == username);
        if (user == null || !VerifyPassword(password, user.PasswordHash))
            return false;

        CurrentUser = user;
        OnChange?.Invoke();
        return true;
    }

    public void Logout()
    {
        CurrentUser = null;
        OnChange?.Invoke();
    }

    public void RestoreUser(int userId)
    {
        using var context = new InventoryContext();
        var user = context.Users.FirstOrDefault(u => u.UserId == userId);
        if (user != null)
        {
            CurrentUser = user;
            OnChange?.Invoke();
        }
    }

    public bool Register(string username, string password)
    {
        using var context = new InventoryContext();
        if (context.Users.Any(u => u.UserName == username))
            return false;

        var user = new User
        {
            UserName = username,
            PasswordHash = HashPassword(password)
        };

        context.Users.Add(user);
        context.SaveChanges();

        CurrentUser = user;
        OnChange?.Invoke();
        return true;
    }

    public bool ChangeUsername(string newUsername)
    {
        if (CurrentUser == null) return false;

        using var context = new InventoryContext();
        if (context.Users.Any(u => u.UserName == newUsername && u.UserId != CurrentUser.UserId))
            return false;

        var user = context.Users.First(u => u.UserId == CurrentUser.UserId);
        user.UserName = newUsername;
        context.SaveChanges();

        CurrentUser.UserName = newUsername;
        OnChange?.Invoke();
        return true;
    }

    public bool ChangePassword(string currentPassword, string newPassword)
    {
        if (CurrentUser == null) return false;
        if (!VerifyPassword(currentPassword, CurrentUser.PasswordHash)) return false;

        using var context = new InventoryContext();
        var user = context.Users.First(u => u.UserId == CurrentUser.UserId);
        user.PasswordHash = HashPassword(newPassword);
        context.SaveChanges();

        CurrentUser.PasswordHash = user.PasswordHash;
        return true;
    }

    public void ChangeProfilePicture(string? url)
    {
        if (CurrentUser == null) return;

        using var context = new InventoryContext();
        var user = context.Users.First(u => u.UserId == CurrentUser.UserId);
        user.ProfilePictureUrl = url;
        context.SaveChanges();

        CurrentUser.ProfilePictureUrl = url;
        OnChange?.Invoke();
    }

    private static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = Convert.FromBase64String(parts[1]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);

        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
