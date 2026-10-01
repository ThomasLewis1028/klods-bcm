using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

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
                .ToDictionary(g => g.Key, g => g.Count());

            var users = await db.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();

            var result = users.Select(u => new UserStatsDto(
                u.UserId, u.UserName, u.Role, u.ProfilePictureUrl,
                setCounts.GetValueOrDefault(u.UserId, 0),
                brickCounts.GetValueOrDefault(u.UserId, 0),
                minifigCounts.GetValueOrDefault(u.UserId, 0),
                u.BodyStyle, u.MascotVariant)).ToList();

            return Results.Ok(result);
        }).AllowApiKey();

        MapCollection(group);
    }

    // Read-only views of another user's collection. Only GETs live here: every write endpoint takes the
    // owner from the caller's token, so there is no route by which one user can change another's data.
    // Location and notes are personal and are never returned on these routes.
    private static void MapCollection(RouteGroupBuilder users)
    {
        var owner = users.MapGroup("/{userId:int}")
            .AddEndpointFilter(async (ctx, next) =>
            {
                if (ctx.HttpContext.GetRouteValue("userId") is not string raw || !int.TryParse(raw, out var userId))
                    return Results.NotFound();
                var dbFactory = ctx.HttpContext.RequestServices.GetRequiredService<IDbContextFactory<InventoryContext>>();
                await using var db = dbFactory.CreateDbContext();
                return await db.Users.AnyAsync(u => u.UserId == userId && u.Status == "Active")
                    ? await next(ctx)
                    : Results.NotFound();
            })
            .AllowApiKey();

        owner.MapGet("/", async (int userId, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var u = await db.Users.AsNoTracking().SingleAsync(x => x.UserId == userId);
            return Results.Ok(new UserStatsDto(
                u.UserId, u.UserName, u.Role, u.ProfilePictureUrl,
                await db.Set<SetOwned>().CountAsync(so => so.UserId == userId),
                await db.Set<BrickOwned>().Where(bo => bo.UserId == userId).SumAsync(bo => bo.Stock),
                await db.Set<MinifigOwned>().CountAsync(mo => mo.UserId == userId),
                u.BodyStyle, u.MascotVariant));
        });

        owner.MapGet("/sets", (int userId, IDbContextFactory<InventoryContext> dbFactory) =>
            SetsEndpoints.OwnedSetsAsync(dbFactory, userId, withNotes: false));

        owner.MapGet("/bom/{setId}/{setIndex:int}", (int userId, string setId, int setIndex, IDbContextFactory<InventoryContext> dbFactory) =>
            BomEndpoints.BomAsync(dbFactory, userId, setId, setIndex, withNotes: false));

        owner.MapGet("/bom/{setId}/{setIndex:int}/substitutions", (int userId, string setId, int setIndex, IDbContextFactory<InventoryContext> dbFactory) =>
            BomEndpoints.SubstitutionsAsync(dbFactory, userId, setId, setIndex, withNotes: false));

        owner.MapGet("/bom/{setId}/{setIndex:int}/minifigs/{minifigId}/instances", (
            int userId, string setId, int setIndex, string minifigId, IDbContextFactory<InventoryContext> dbFactory) =>
            BomEndpoints.MinifigInstancesAsync(dbFactory, userId, setId, setIndex, minifigId));

        owner.MapGet("/bricks", (int userId, IDbContextFactory<InventoryContext> dbFactory) =>
            MyCatalogEndpoints.BricksAsync(dbFactory, userId));

        owner.MapGet("/bricks/{partNum}/{colorId}", (int userId, string partNum, string colorId, IDbContextFactory<InventoryContext> dbFactory) =>
            BricksEndpoints.OwnedStockAsync(dbFactory, userId, partNum, colorId, withNotes: false));

        owner.MapGet("/bricks/{partNum}/{colorId}/sets", (int userId, string partNum, string colorId, IDbContextFactory<InventoryContext> dbFactory) =>
            MyCatalogEndpoints.SetsNeedingBrickAsync(dbFactory, userId, partNum, colorId));

        owner.MapGet("/minifigs", (int userId, IDbContextFactory<InventoryContext> dbFactory) =>
            MyCatalogEndpoints.MinifigsAsync(dbFactory, userId));

        owner.MapGet("/minifigs/{minifigId}/instances", (int userId, string minifigId, IDbContextFactory<InventoryContext> dbFactory) =>
            MyCatalogEndpoints.MinifigInstancesAsync(dbFactory, userId, minifigId));

        owner.MapGet("/minifigs/{minifigId}/loose-count", (int userId, string minifigId, IDbContextFactory<InventoryContext> dbFactory) =>
            MinifigsEndpoints.LooseCountAsync(dbFactory, userId, minifigId));
    }

    public record UserStatsDto(int UserId, string UserName, string Role, string? ProfilePictureUrl, int OwnedSets, int OwnedBricks, int OwnedMinifigs, string? BodyStyle, string? MascotVariant);
}
