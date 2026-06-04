using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory.Api.Endpoints;

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
    }

    public record UserDto(int UserId, string UserName, string Role, string? ProfilePictureUrl);
}
