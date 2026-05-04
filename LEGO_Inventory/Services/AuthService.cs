using System.Security.Cryptography;
using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory.Services;

public class AuthService(IDbContextFactory<InventoryContext> contextFactory)
{
    private const int PasswordHashIterations = 100_000;
    public User? CurrentUser { get; private set; }
    public bool IsSessionRestored { get; private set; }
    public event Action? OnChange;
    public event Action? SessionRestored;

    public void MarkSessionRestored()
    {
        IsSessionRestored = true;
        SessionRestored?.Invoke();
    }

    public bool Login(string username, string password)
    {
        using var context = contextFactory.CreateDbContext();
        var user = context.Users.FirstOrDefault(u => u.UserName == username);
        if (user == null || string.IsNullOrEmpty(user.PasswordHash) || !VerifyPassword(password, user.PasswordHash))
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
        using var context = contextFactory.CreateDbContext();
        var user = context.Users.FirstOrDefault(u => u.UserId == userId);
        if (user != null)
        {
            CurrentUser = user;
            OnChange?.Invoke();
        }
    }

    public bool Register(string username, string password)
    {
        using var context = contextFactory.CreateDbContext();
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

        using var context = contextFactory.CreateDbContext();
        if (context.Users.Any(u => u.UserName == newUsername && u.UserId != CurrentUser.UserId))
            return false;

        var user = context.Users.FirstOrDefault(u => u.UserId == CurrentUser.UserId);
        if (user == null) return false;
        user.UserName = newUsername;
        try { context.SaveChanges(); } catch { return false; }

        CurrentUser.UserName = newUsername;
        OnChange?.Invoke();
        return true;
    }

    public bool ChangePassword(string currentPassword, string newPassword)
    {
        if (CurrentUser == null) return false;
        if (!VerifyPassword(currentPassword, CurrentUser.PasswordHash)) return false;

        using var context = contextFactory.CreateDbContext();
        var user = context.Users.FirstOrDefault(u => u.UserId == CurrentUser.UserId);
        if (user == null) return false;
        user.PasswordHash = HashPassword(newPassword);
        try { context.SaveChanges(); } catch { return false; }

        CurrentUser.PasswordHash = user.PasswordHash;
        return true;
    }

    public List<UserExternalLogin> GetLinkedLogins()
    {
        if (CurrentUser == null) return [];
        using var context = contextFactory.CreateDbContext();
        return context.UserExternalLogins
            .Where(l => l.UserId == CurrentUser.UserId)
            .ToList();
    }

    public bool UnlinkExternalLogin(string provider)
    {
        if (CurrentUser == null) return false;

        using var context = contextFactory.CreateDbContext();
        var logins = context.UserExternalLogins
            .Where(l => l.UserId == CurrentUser.UserId)
            .ToList();

        var hasPassword = !string.IsNullOrEmpty(CurrentUser.PasswordHash);
        if (!hasPassword && logins.Count <= 1) return false; // last auth method

        var login = logins.FirstOrDefault(l => l.Provider == provider);
        if (login == null) return false;

        context.UserExternalLogins.Remove(login);
        context.SaveChanges();
        return true;
    }

    public bool ChangeThemeColor(string? hex)
    {
        if (CurrentUser == null) return false;

        using var context = contextFactory.CreateDbContext();
        var user = context.Users.FirstOrDefault(u => u.UserId == CurrentUser.UserId);
        if (user == null) return false;
        user.PrimaryColor = hex;
        try { context.SaveChanges(); } catch { return false; }

        CurrentUser.PrimaryColor = hex;
        OnChange?.Invoke();
        return true;
    }

    public bool ChangeProfilePicture(string? url)
    {
        if (CurrentUser == null) return false;

        using var context = contextFactory.CreateDbContext();
        var user = context.Users.FirstOrDefault(u => u.UserId == CurrentUser.UserId);
        if (user == null) return false;
        user.ProfilePictureUrl = url;
        try { context.SaveChanges(); } catch { return false; }

        CurrentUser.ProfilePictureUrl = url;
        OnChange?.Invoke();
        return true;
    }

    internal static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordHashIterations, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyPassword(string password, string storedHash)
    {
        var parts = storedHash.Split('.');
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var expectedHash = Convert.FromBase64String(parts[1]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordHashIterations, HashAlgorithmName.SHA256, 32);

        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
