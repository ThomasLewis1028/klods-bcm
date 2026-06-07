using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class MyCatalogEndpoints
{
    public static void MapMyCatalog(this IEndpointRouteBuilder app)
    {
        MapMyBricks(app);
        MapMyMinifigs(app);
    }

    private static void MapMyBricks(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/mybricks").RequireAuthorization();

        // All bricks relevant to the current user: bricks they own loose + bricks needed by their sets.
        // Supports optional search, page, and pageSize.
        group.MapGet("/", async (
            HttpContext http, IDbContextFactory<InventoryContext> dbFactory,
            string? search = null, int page = 0, int pageSize = 0) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var userSetCopies = await InventoryAggregates.GetSetCopiesAsync(db, userId);
            var userSetIds    = userSetCopies.Keys.ToList();

            var setBricks    = await db.Set<SetBrick>().AsNoTracking().Where(sb => userSetIds.Contains(sb.SetId)).ToListAsync();
            var neededDict   = InventoryAggregates.GetBrickNeededDict(setBricks, userSetCopies);
            var setCountDict = InventoryAggregates.GetBrickSetCountDict(setBricks);

            var ownedDict = (await db.Set<BrickOwned>().AsNoTracking().Where(bo => bo.UserId == userId).ToListAsync())
                .ToDictionary(bo => (bo.PartNum, bo.ColorId));

            var allKeys     = neededDict.Keys.Union(ownedDict.Keys).ToHashSet();
            var allPartNums = allKeys.Select(k => k.PartNum).ToList();

            var bricksQuery = db.Set<Brick>().AsNoTracking().Where(b => allPartNums.Contains(b.PartNum));
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                bricksQuery = bricksQuery.Where(b =>
                    b.Name.ToLower().Contains(term) || b.PartNum.ToLower().Contains(term));
            }

            var bricks = (await bricksQuery.OrderBy(b => b.Name).ToListAsync())
                .Where(b => allKeys.Contains((b.PartNum, b.ColorId ?? ""))).ToList();

            var result = bricks.Select(b =>
            {
                var key = (b.PartNum, b.ColorId ?? "");
                ownedDict.TryGetValue(key, out var bo);
                return new MyBrickDto(
                    b.PartNum, b.Name, b.PartImg, b.ColorId, b.ColorName, b.HexColor, b.IsTrans, b.BricklinkId,
                    bo?.Stock ?? 0,
                    neededDict.GetValueOrDefault(key, 0),
                    setCountDict.GetValueOrDefault(key, 0));
            }).ToList();

            bool hasMore = false;
            if (pageSize > 0)
            {
                hasMore = result.Count > page * pageSize + pageSize;
                result = result.Skip(page * pageSize).Take(pageSize).ToList();
            }

            return Results.Ok(new PagedResult<MyBrickDto>(result, hasMore));
        });

        // Upsert loose brick stock — creates BrickOwned if it doesn't exist yet.
        group.MapPut("/{partNum}/{colorId}/stock", async (
            string partNum, string colorId, UpdateStockRequest req, HttpContext http,
            IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var existing = await db.Set<BrickOwned>()
                .FirstOrDefaultAsync(b => b.PartNum == partNum && b.ColorId == colorId && b.UserId == userId);
            if (existing is not null)
            {
                existing.Stock = req.Stock;
                await db.SaveChangesAsync();
            }
            else
            {
                db.Set<BrickOwned>().Add(new BrickOwned { UserId = userId, PartNum = partNum, ColorId = colorId, Stock = req.Stock });
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });

        // Lazy-load: sets the user owns that require a specific brick+color.
        group.MapGet("/{partNum}/{colorId}/sets", async (
            string partNum, string colorId, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var userSetCopies = await InventoryAggregates.GetSetCopiesAsync(db, userId);
            var userSetIds    = userSetCopies.Keys.ToList();

            var setBricks = await db.Set<SetBrick>().AsNoTracking()
                .Where(sb => sb.PartNum == partNum && sb.ColorId == colorId && userSetIds.Contains(sb.SetId))
                .ToListAsync();

            var setIds = setBricks.Select(sb => sb.SetId).Distinct().ToList();
            var sets = await db.Set<Set>().AsNoTracking().Where(s => setIds.Contains(s.SetId)).ToListAsync();
            var setDict = sets.ToDictionary(s => s.SetId);

            var result = setBricks.Select(sb => new MyBrickSetDetailDto(
                sb.SetId,
                setDict.TryGetValue(sb.SetId, out var s) ? s.Name : sb.SetId,
                setDict.TryGetValue(sb.SetId, out var s2) ? s2.SetImg : null,
                sb.Count,
                userSetCopies.GetValueOrDefault(sb.SetId, 0))).ToList();

            return Results.Ok(result);
        });
    }

    private static void MapMyMinifigs(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/myminifigs").RequireAuthorization();

        // All minifigs relevant to the current user: minifigs they own + minifigs needed by their sets.
        // Supports optional search, page, and pageSize.
        group.MapGet("/", async (
            HttpContext http, IDbContextFactory<InventoryContext> dbFactory,
            string? search = null, int page = 0, int pageSize = 0) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var userSetCopies = await InventoryAggregates.GetSetCopiesAsync(db, userId);
            var userSetIds    = userSetCopies.Keys.ToList();

            var setMinifigs  = await db.Set<SetMinifig>().AsNoTracking().Where(sm => userSetIds.Contains(sm.SetId)).ToListAsync();
            var neededDict   = InventoryAggregates.GetMinifigNeededDict(setMinifigs, userSetCopies);
            var setCountDict = InventoryAggregates.GetMinifigSetCountDict(setMinifigs);

            var ownedDict = (await db.Set<MinifigOwned>().AsNoTracking().Where(mo => mo.UserId == userId).ToListAsync())
                .ToDictionary(mo => mo.MinifigId);

            var allIds = neededDict.Keys.Union(ownedDict.Keys).ToHashSet();

            var minifigsQuery = db.Set<Minifig>().AsNoTracking().Where(m => allIds.Contains(m.MinifigId));
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                minifigsQuery = minifigsQuery.Where(m =>
                    m.MinifigName.ToLower().Contains(term) || m.MinifigId.ToLower().Contains(term));
            }

            var minifigs = await minifigsQuery.OrderBy(m => m.MinifigName).ToListAsync();

            var pageIds = minifigs.Select(m => m.MinifigId).ToList();
            var partCounts = await db.Set<MinifigBrick>().AsNoTracking()
                .Where(mb => pageIds.Contains(mb.MinifigID))
                .GroupBy(mb => mb.MinifigID)
                .Select(g => new { MinifigId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.MinifigId, x => x.Count);

            var result = minifigs.Select(m =>
            {
                ownedDict.TryGetValue(m.MinifigId, out var mo);
                return new MyMinifigDto(
                    m.MinifigId, m.MinifigName, m.MinifigImgUrl,
                    mo?.Stock ?? 0,
                    neededDict.GetValueOrDefault(m.MinifigId, 0),
                    setCountDict.GetValueOrDefault(m.MinifigId, 0),
                    partCounts.GetValueOrDefault(m.MinifigId, 0));
            }).ToList();

            bool hasMore = false;
            if (pageSize > 0)
            {
                hasMore = result.Count > page * pageSize + pageSize;
                result = result.Skip(page * pageSize).Take(pageSize).ToList();
            }

            return Results.Ok(new PagedResult<MyMinifigDto>(result, hasMore));
        });

        // Upsert minifig owned stock — creates MinifigOwned if it doesn't exist yet.
        group.MapPut("/{minifigId}/stock", async (
            string minifigId, UpdateStockRequest req, HttpContext http,
            IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var existing = await db.Set<MinifigOwned>()
                .FirstOrDefaultAsync(m => m.MinifigId == minifigId && m.UserId == userId);
            if (existing is not null)
            {
                existing.Stock = req.Stock;
                await db.SaveChangesAsync();
            }
            else
            {
                db.Set<MinifigOwned>().Add(new MinifigOwned { UserId = userId, MinifigId = minifigId, Stock = req.Stock });
                await db.SaveChangesAsync();
            }
            return Results.Ok();
        });

        // Lazy-load: bricks belonging to a minifig (reuses the minifigs endpoint shape).
        group.MapGet("/{minifigId}/bricks", async (string minifigId, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();

            var rows = await db.Set<MinifigBrick>().AsNoTracking()
                .Where(mb => mb.MinifigID == minifigId)
                .Join(db.Set<Brick>().AsNoTracking(),
                    mb => new { PartNum = mb.BrickID, mb.ColorId },
                    b  => new { b.PartNum, b.ColorId },
                    (mb, b) => new MinifigsEndpoints.MinifigBrickDto(mb.BrickID, mb.ColorId, b.Name, b.PartImg, b.ColorName, b.HexColor, mb.Quantity))
                .ToListAsync();

            return Results.Ok(rows);
        });
    }

    public record UpdateStockRequest(int Stock);
    public record MyBrickDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId, int Stock, int UserNeeded, int UserSetCount);
    public record MyBrickSetDetailDto(string SetId, string SetName, string? SetImg, int BrickCount, int CopiesOwned);
    public record MyMinifigDto(string MinifigId, string MinifigName, string? ImgUrl, int Stock, int UserNeeded, int UserSetCount, int PartCount);
}
