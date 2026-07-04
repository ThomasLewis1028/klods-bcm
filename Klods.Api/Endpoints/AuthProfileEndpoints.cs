using System.Security.Claims;
using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class AuthProfileEndpoints
{
    public static void MapAuthProfile(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth/me").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user is null) return Results.NotFound();
            return Results.Ok(UserProfileDto.From(user));
        });

        group.MapPatch("/username", async (ChangeUsernameRequest req, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            if (string.IsNullOrWhiteSpace(req.NewUsername)) return Results.BadRequest("Username is required.");
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            if (await db.Users.AnyAsync(u => u.UserName == req.NewUsername && u.UserId != userId))
                return Results.Conflict("Username already taken.");
            var rows = await db.Users.Where(u => u.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.UserName, req.NewUsername));
            return rows > 0 ? Results.Ok() : Results.NotFound();
        });

        group.MapPatch("/password", async (ChangePasswordRequest req, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserId == userId);
            if (user is null) return Results.NotFound();
            if (string.IsNullOrEmpty(user.PasswordHash) || !PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
                return Results.BadRequest("Current password is incorrect.");
            user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        group.MapPatch("/theme", async (ChangeThemeRequest req, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            await db.Users.Where(u => u.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.PrimaryColor, req.Color));
            return Results.Ok();
        });

        group.MapPatch("/fontscale", async (ChangeFontScaleRequest req, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            await db.Users.Where(u => u.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.FontScale, req.Scale));
            return Results.Ok();
        });

        group.MapPatch("/picture", async (ChangePictureRequest req, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            await db.Users.Where(u => u.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.ProfilePictureUrl, req.Url));
            return Results.Ok();
        });

        group.MapGet("/logins", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var logins = await db.UserExternalLogins
                .AsNoTracking()
                .Where(l => l.UserId == userId)
                .Select(l => new LinkedLoginDto(l.Provider))
                .ToListAsync();
            return Results.Ok(logins);
        });

        group.MapDelete("/logins/{provider}", async (string provider, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user is null) return Results.NotFound();

            var logins = await db.UserExternalLogins.Where(l => l.UserId == userId).ToListAsync();
            var target = logins.FirstOrDefault(l => l.Provider == provider);
            if (target is null) return Results.NotFound();

            var hasPassword = !string.IsNullOrEmpty(user.PasswordHash);
            if (!hasPassword && logins.Count <= 1)
                return Results.BadRequest("Cannot unlink your only sign-in method.");

            db.UserExternalLogins.Remove(target);
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }

    public record UserProfileDto(int UserId, string UserName, string Role, string? ProfilePictureUrl, string? PrimaryColor, bool HasPassword, double FontScale)
    {
        public static UserProfileDto From(User u) =>
            new(u.UserId, u.UserName, u.Role, u.ProfilePictureUrl, u.PrimaryColor, !string.IsNullOrEmpty(u.PasswordHash), u.FontScale);
    }

    public record ChangeUsernameRequest(string NewUsername);
    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
    public record ChangeThemeRequest(string? Color);
    public record ChangeFontScaleRequest(double Scale);
    public record ChangePictureRequest(string? Url);
    public record LinkedLoginDto(string Provider);
}
