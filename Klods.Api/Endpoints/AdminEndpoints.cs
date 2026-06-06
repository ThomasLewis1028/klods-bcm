using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdmin(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").RequireAuthorization("Admin");

        group.MapPost("/import-colors", async (ImportData importer) =>
        {
            var ok = await importer.ImportColors();
            return ok ? Results.Ok() : Results.BadRequest("Color import failed.");
        });

        group.MapPost("/backfill-images", async (ImportData importer, CancellationToken ct) =>
        {
            await importer.BackfillImagesAsync(null, ct);
            return Results.Ok();
        });

        group.MapGet("/users", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var users = await db.Users.AsNoTracking()
                .OrderBy(u => u.UserName)
                .Select(u => new UserDto(u.UserId, u.UserName, u.Role, u.ProfilePictureUrl))
                .ToListAsync();
            return Results.Ok(users);
        });

        // Count of images still hosted at external URLs (not yet migrated to MinIO).
        group.MapGet("/pending-count", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var count =
                await db.Set<Set>().CountAsync(s => s.SetImg != null && s.SetImg.StartsWith("http")) +
                await db.Set<Minifig>().CountAsync(m => m.MinifigImgUrl != null && m.MinifigImgUrl.StartsWith("http")) +
                await db.Set<Brick>().CountAsync(b => b.PartImg != null && b.PartImg.StartsWith("http"));
            return Results.Ok(new PendingCountDto(count));
        });

        group.MapPatch("/users/{userId:int}/role", async (
            int userId, SetRoleRequest req, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            if (req.Role != "Admin" && req.Role != "User")
                return Results.BadRequest("Role must be 'Admin' or 'User'.");
            await using var db = dbFactory.CreateDbContext();
            var rows = await db.Users.Where(u => u.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, req.Role));
            return rows > 0 ? Results.Ok() : Results.NotFound();
        });
    }

    public record UserDto(int UserId, string UserName, string Role, string? ProfilePictureUrl);
    public record PendingCountDto(int Count);
    public record SetRoleRequest(string Role);
}
