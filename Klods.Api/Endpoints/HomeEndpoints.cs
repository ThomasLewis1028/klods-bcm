using Klods.Api.Auth;
using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class HomeEndpoints
{
    public static void MapHome(this IEndpointRouteBuilder app)
    {
        // Anonymous: the landing page shows catalog images to everyone. When a valid bearer
        // token is present the "My" picks are drawn from the user's own collection instead.
        app.MapGet("/api/home/preview", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();

            var sets     = await db.Set<Set>().AsNoTracking().Where(s => s.SetImg != null).ToListAsync();
            var bricks   = await db.Set<Brick>().AsNoTracking().Where(b => b.PartImg != null).ToListAsync();
            var minifigs = await db.Set<Minifig>().AsNoTracking().Where(m => m.ImgUrl != null).ToListAsync();

            // Which of those the current user owns — empty when signed out.
            List<string> ownedSetIds = [];
            HashSet<(string, string)> ownedBrickKeys = [];
            List<string> ownedMinifigIds = [];

            if (http.User.Identity?.IsAuthenticated == true)
            {
                var userId = http.UserId();
                ownedSetIds = await db.Set<SetOwned>().AsNoTracking()
                    .Where(so => so.UserId == userId).Select(so => so.SetId).Distinct().ToListAsync();
                ownedBrickKeys = (await db.Set<BrickOwned>().AsNoTracking()
                        .Where(bo => bo.UserId == userId && bo.Stock > 0)
                        .Select(bo => new { bo.PartNum, bo.ColorId }).ToListAsync())
                    .Select(k => (k.PartNum, k.ColorId)).ToHashSet();
                ownedMinifigIds = await db.Set<MinifigOwned>().AsNoTracking()
                    .Where(mo => mo.UserId == userId).Select(mo => mo.MinifigId).Distinct().ToListAsync();
            }

            var mySets     = sets.Where(s => ownedSetIds.Contains(s.SetId)).ToList();
            var myBricks   = bricks.Where(b => ownedBrickKeys.Contains((b.PartNum, b.ColorId ?? ""))).ToList();
            var myMinifigs = minifigs.Where(m => ownedMinifigIds.Contains(m.MinifigId)).ToList();

            // "My" picks fall back to a global random when the user owns nothing (with images).
            var catalogSet     = PickRandom(sets);
            var catalogBrick   = PickRandom(bricks);
            var catalogMinifig = PickRandom(minifigs);
            var mySet     = PickRandom(mySets.Count     > 0 ? mySets     : sets);
            var myBrick   = PickRandom(myBricks.Count   > 0 ? myBricks   : bricks);
            var myMinifig = PickRandom(myMinifigs.Count > 0 ? myMinifigs : minifigs);

            return Results.Ok(new HomePreviewDto(
                catalogSet     is null ? null : new PreviewItemDto(catalogSet.SetId, catalogSet.Name, catalogSet.SetImg),
                catalogBrick   is null ? null : new PreviewItemDto(catalogBrick.PartNum, catalogBrick.Name, catalogBrick.PartImg),
                catalogMinifig is null ? null : new PreviewItemDto(catalogMinifig.MinifigId, catalogMinifig.Name, catalogMinifig.ImgUrl),
                mySet     is null ? null : new PreviewItemDto(mySet.SetId, mySet.Name, mySet.SetImg),
                myBrick   is null ? null : new PreviewItemDto(myBrick.PartNum, myBrick.Name, myBrick.PartImg),
                myMinifig is null ? null : new PreviewItemDto(myMinifig.MinifigId, myMinifig.Name, myMinifig.ImgUrl)
            ));
        });
    }

    private static T? PickRandom<T>(List<T> source) where T : class =>
        source.Count == 0 ? null : source[Random.Shared.Next(source.Count)];

    public record PreviewItemDto(string Id, string Name, string? ImgUrl);
    public record HomePreviewDto(
        PreviewItemDto? CatalogSet, PreviewItemDto? CatalogBrick, PreviewItemDto? CatalogMinifig,
        PreviewItemDto? MySet, PreviewItemDto? MyBrick, PreviewItemDto? MyMinifig);
}
