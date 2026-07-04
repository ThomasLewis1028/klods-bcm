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
        // Each pick is a single random row chosen in SQL (no table is loaded into memory).
        app.MapGet("/api/home/preview", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();

            var catalogSet     = await RandomSet(db, null);
            var catalogBrick   = await RandomBrick(db, null);
            var catalogMinifig = await RandomMinifig(db, null);

            // Which items the current user owns — null when signed out (→ global fallback below).
            List<string>? ownedSetIds = null, ownedPartNums = null, ownedMinifigIds = null;
            if (http.User.Identity?.IsAuthenticated == true)
            {
                var userId = http.UserId();
                ownedSetIds = await db.Set<SetOwned>().AsNoTracking()
                    .Where(so => so.UserId == userId).Select(so => so.SetId).Distinct().ToListAsync();
                ownedPartNums = await db.Set<BrickOwned>().AsNoTracking()
                    .Where(bo => bo.UserId == userId && bo.Stock > 0).Select(bo => bo.PartNum).Distinct().ToListAsync();
                ownedMinifigIds = await db.Set<MinifigOwned>().AsNoTracking()
                    .Where(mo => mo.UserId == userId).Select(mo => mo.MinifigId).Distinct().ToListAsync();
            }

            // Fall back to the global pick when the user owns nothing (with an image).
            var mySet     = await RandomSet(db, ownedSetIds)          ?? catalogSet;
            var myBrick   = await RandomBrick(db, ownedPartNums)      ?? catalogBrick;
            var myMinifig = await RandomMinifig(db, ownedMinifigIds)  ?? catalogMinifig;

            return Results.Ok(new HomePreviewDto(
                catalogSet, catalogBrick, catalogMinifig, mySet, myBrick, myMinifig));
        });
    }

    private static Task<PreviewItemDto?> RandomSet(InventoryContext db, List<string>? ownedIds)
    {
        var q = db.Set<Set>().AsNoTracking().Where(s => s.SetImg != null);
        if (ownedIds is { Count: > 0 }) q = q.Where(s => ownedIds.Contains(s.SetId));
        return q.OrderBy(_ => EF.Functions.Random())
            .Select(s => new PreviewItemDto(s.SetId, s.Name, s.SetImg))
            .FirstOrDefaultAsync();
    }

    private static Task<PreviewItemDto?> RandomBrick(InventoryContext db, List<string>? ownedPartNums)
    {
        var q = db.Set<Brick>().AsNoTracking().Where(b => b.PartImg != null);
        if (ownedPartNums is { Count: > 0 }) q = q.Where(b => ownedPartNums.Contains(b.PartNum));
        return q.OrderBy(_ => EF.Functions.Random())
            .Select(b => new PreviewItemDto(b.PartNum, b.Name, b.PartImg))
            .FirstOrDefaultAsync();
    }

    private static Task<PreviewItemDto?> RandomMinifig(InventoryContext db, List<string>? ownedIds)
    {
        var q = db.Set<Minifig>().AsNoTracking().Where(m => m.ImgUrl != null);
        if (ownedIds is { Count: > 0 }) q = q.Where(m => ownedIds.Contains(m.MinifigId));
        return q.OrderBy(_ => EF.Functions.Random())
            .Select(m => new PreviewItemDto(m.MinifigId, m.Name, m.ImgUrl))
            .FirstOrDefaultAsync();
    }

    public record PreviewItemDto(string Id, string Name, string? ImgUrl);
    public record HomePreviewDto(
        PreviewItemDto? CatalogSet, PreviewItemDto? CatalogBrick, PreviewItemDto? CatalogMinifig,
        PreviewItemDto? MySet, PreviewItemDto? MyBrick, PreviewItemDto? MyMinifig);
}
