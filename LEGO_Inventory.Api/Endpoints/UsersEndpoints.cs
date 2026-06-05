using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory.Api.Endpoints;

public static class UsersEndpoints
{
    public static void MapUsers(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").RequireAuthorization();

        group.MapGet("/", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();

            var setCounts = (await db.Set<SetOwned>().AsNoTracking().ToListAsync())
                .GroupBy(so => so.UserId)
                .ToDictionary(g => g.Key, g => g.Count());

            var brickCounts = (await db.Set<BrickOwned>().AsNoTracking().ToListAsync())
                .GroupBy(bo => bo.UserId)
                .ToDictionary(g => g.Key, g => g.Sum(bo => bo.Stock));

            var minifigCounts = (await db.Set<MinifigOwned>().AsNoTracking().ToListAsync())
                .GroupBy(mo => mo.UserId)
                .ToDictionary(g => g.Key, g => g.Sum(mo => mo.Stock));

            var users = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();

            var result = users.Select(u => new UserStatsDto(
                u.UserId, u.UserName, u.Role, u.ProfilePictureUrl,
                setCounts.GetValueOrDefault(u.UserId, 0),
                brickCounts.GetValueOrDefault(u.UserId, 0),
                minifigCounts.GetValueOrDefault(u.UserId, 0))).ToList();

            return Results.Ok(result);
        });
    }

    public record UserStatsDto(int UserId, string UserName, string Role, string? ProfilePictureUrl, int OwnedSets, int OwnedBricks, int OwnedMinifigs);
}
