using Klods.Api.Auth;
using Klods.Database;
using Klods.Services;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class AuthEndpoints
{
    public const string AutoApproveKey = "registration.auto_approve";

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (LoginRequest req, IDbContextFactory<InventoryContext> dbFactory, JwtService jwt) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == req.Username);
            if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
                return Results.Unauthorized();

            if (user.Status != "Active")
                return Results.StatusCode(403);

            return Results.Ok(new TokenResponse(jwt.Generate(user)));
        });

        group.MapPost("/register", async (RegisterRequest req, IDbContextFactory<InventoryContext> dbFactory, JwtService jwt, SettingsService settings) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest("Username and password are required.");

            await using var db = dbFactory.CreateDbContext();
            if (await db.Users.AnyAsync(u => u.UserName == req.Username))
                return Results.Conflict("Username already taken.");

            var autoApprove = await settings.GetBoolAsync(AutoApproveKey, fallback: true);

            var user = new User
            {
                UserName = req.Username,
                PasswordHash = PasswordHasher.Hash(req.Password),
                Role = "User",
                Status = autoApprove ? "Active" : "Pending"
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            if (!autoApprove)
                return Results.Accepted();

            return Results.Ok(new TokenResponse(jwt.Generate(user)));
        });
    }

    public record LoginRequest(string Username, string Password);
    public record RegisterRequest(string Username, string Password);
    public record TokenResponse(string Token);
}
