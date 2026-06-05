using LEGO_Inventory.Api.Auth;
using LEGO_Inventory.Database;
using LEGO_Inventory.Services;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (LoginRequest req, IDbContextFactory<InventoryContext> dbFactory, JwtService jwt) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == req.Username);
            if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
                return Results.Unauthorized();

            return Results.Ok(new TokenResponse(jwt.Generate(user)));
        });

        group.MapPost("/register", async (RegisterRequest req, IDbContextFactory<InventoryContext> dbFactory, JwtService jwt) =>
        {
            if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
                return Results.BadRequest("Username and password are required.");

            await using var db = dbFactory.CreateDbContext();
            if (await db.Users.AnyAsync(u => u.UserName == req.Username))
                return Results.Conflict("Username already taken.");

            var user = new User
            {
                UserName = req.Username,
                PasswordHash = PasswordHasher.Hash(req.Password),
                Role = "User"
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            return Results.Ok(new TokenResponse(jwt.Generate(user)));
        });
    }

    public record LoginRequest(string Username, string Password);
    public record RegisterRequest(string Username, string Password);
    public record TokenResponse(string Token);
}
