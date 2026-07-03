using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class BomEndpoints
{
    public static void MapBom(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bom").RequireAuthorization();

        // Full BOM for a specific set instance: bricks + minifigs + stock + context for navigation.
        group.MapGet("/{setId}/{setIndex:int}", async (
            string setId, int setIndex, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var set = await db.Set<Set>().AsNoTracking().FirstOrDefaultAsync(s => s.SetId == setId);
            if (set is null) return Results.NotFound();

            var owned = await db.Set<SetOwned>().AnyAsync(so => so.SetId == setId && so.SetIndex == setIndex && so.UserId == userId);
            if (!owned) return Results.NotFound();

            // All owned instances of this set (for index switching in the UI)
            var ownedInstances = await db.Set<SetOwned>().AsNoTracking()
                .Where(so => so.SetId == setId && so.UserId == userId)
                .Select(so => so.SetIndex)
                .ToListAsync();

            // All owned set IDs (for the set picker)
            var ownedSetIds = await db.Set<SetOwned>().AsNoTracking()
                .Where(so => so.UserId == userId)
                .Select(so => so.SetId)
                .Distinct()
                .ToListAsync();

            // Bricks
            var setBricks = await db.Set<SetBrick>().AsNoTracking().Where(sb => sb.SetId == setId).ToListAsync();
            var setBrickPartNums = setBricks.Select(sb => sb.PartNum).ToHashSet();

            var brickDict = (await db.Set<Brick>().AsNoTracking()
                .Where(b => setBrickPartNums.Contains(b.PartNum))
                .ToListAsync())
                .ToDictionary(b => (b.PartNum, b.ColorId ?? ""));

            var setBrickOwnedDict = (await db.Set<SetBrickOwned>().AsNoTracking()
                .Where(sbo => sbo.UserId == userId && sbo.SetId == setId && sbo.SetIndex == setIndex)
                .ToListAsync())
                .ToDictionary(sbo => (sbo.PartNum, sbo.ColorId));

            var brickOwnedDict = (await db.Set<BrickOwned>().AsNoTracking()
                .Where(bo => bo.UserId == userId && setBrickPartNums.Contains(bo.PartNum))
                .ToListAsync())
                .ToDictionary(bo => (bo.PartNum, bo.ColorId));

            var brickItems = setBricks
                .Where(sb => brickDict.ContainsKey((sb.PartNum, sb.ColorId)))
                .Select(sb =>
                {
                    var brick = brickDict[(sb.PartNum, sb.ColorId)];
                    setBrickOwnedDict.TryGetValue((sb.PartNum, sb.ColorId), out var sbo);
                    brickOwnedDict.TryGetValue((sb.PartNum, sb.ColorId), out var bo);
                    return new BomBrickDto(
                        sb.PartNum, sb.ColorId, brick.Name, brick.PartImg, brick.ColorName, brick.HexColor,
                        sb.Count, sb.SpareCount,
                        sbo?.Stock ?? 0,
                        bo?.Stock ?? 0,
                        brick.BricklinkId);
                }).ToList();

            // Minifigs
            var setMinifigs   = await db.Set<SetMinifig>().AsNoTracking().Where(sm => sm.SetId == setId).ToListAsync();
            var minifigIds    = setMinifigs.Select(sm => sm.MinifigId).ToHashSet();
            var minifigDict   = (await db.Set<Minifig>().AsNoTracking().Where(m => minifigIds.Contains(m.MinifigId)).ToListAsync())
                .ToDictionary(m => m.MinifigId);
            // Owned count for THIS set copy = number of fig instances linked to (setId, setIndex).
            var minifigOwnedCounts = (await db.Set<MinifigOwned>().AsNoTracking()
                .Where(mo => mo.UserId == userId && mo.SetId == setId && mo.SetIndex == setIndex && minifigIds.Contains(mo.MinifigId))
                .ToListAsync())
                .GroupBy(mo => mo.MinifigId)
                .ToDictionary(g => g.Key, g => g.Count());

            var minifigItems = setMinifigs
                .Where(sm => minifigDict.ContainsKey(sm.MinifigId))
                .Select(sm =>
                {
                    var minifig = minifigDict[sm.MinifigId];
                    return new BomMinifigDto(sm.MinifigId, minifig.Name, minifig.ImgUrl, sm.Count,
                        minifigOwnedCounts.GetValueOrDefault(sm.MinifigId, 0));
                }).ToList();

            var comp = (await SetCompleteness.ComputeAsync(db, userId, new[] { (setId, setIndex) }))
                .GetValueOrDefault((setId, setIndex)) ?? new SetCompleteness.Result(0, SetCompleteness.Status.Short, 0, 0, 0);

            return Results.Ok(new BomResponse(
                setId, setIndex, set.Name, set.ManualUrl,
                ownedInstances, ownedSetIds,
                brickItems, minifigItems,
                comp.Percent, comp.Status.ToString().ToLowerInvariant()));
        });

        // Just the completeness for a copy — cheap enough to re-poll after each edit for a live bar.
        group.MapGet("/{setId}/{setIndex:int}/completeness", async (
            string setId, int setIndex, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var comp = (await SetCompleteness.ComputeAsync(db, userId, new[] { (setId, setIndex) }))
                .GetValueOrDefault((setId, setIndex)) ?? new SetCompleteness.Result(0, SetCompleteness.Status.Short, 0, 0, 0);
            return Results.Ok(new CompletenessDto(comp.Percent, comp.Status.ToString().ToLowerInvariant()));
        });

        // Inline bricks for a minifig within the BOM context (SetBrickOwned + BrickOwned context).
        group.MapGet("/{setId}/{setIndex:int}/minifigs/{minifigId}/bricks", async (
            string setId, int setIndex, string minifigId, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var minifigBricks = await db.Set<MinifigBrick>().AsNoTracking()
                .Where(mb => mb.MinifigId == minifigId).ToListAsync();

            var brickIds = minifigBricks.Select(mb => mb.PartNum).ToHashSet();

            var brickDict = (await db.Set<Brick>().AsNoTracking().Where(b => brickIds.Contains(b.PartNum)).ToListAsync())
                .ToDictionary(b => (b.PartNum, b.ColorId ?? ""));

            // Owned part stock is tracked against this copy's lowest-indexed instance of the fig.
            var instanceIndex = await db.Set<MinifigOwned>().AsNoTracking()
                .Where(mo => mo.UserId == userId && mo.MinifigId == minifigId && mo.SetId == setId && mo.SetIndex == setIndex)
                .OrderBy(mo => mo.MinifigIndex)
                .Select(mo => (int?)mo.MinifigIndex)
                .FirstOrDefaultAsync();

            var ownedDict = instanceIndex == null
                ? new Dictionary<(string, string), int>()
                : (await db.Set<MinifigBrickOwned>().AsNoTracking()
                        .Where(x => x.UserId == userId && x.MinifigId == minifigId && x.MinifigIndex == instanceIndex.Value)
                        .ToListAsync())
                    .ToDictionary(x => (x.PartNum, x.ColorId), x => x.Stock);

            var result = minifigBricks
                .Where(mb => brickDict.ContainsKey((mb.PartNum, mb.ColorId)))
                .Select(mb =>
                {
                    var brick = brickDict[(mb.PartNum, mb.ColorId)];
                    ownedDict.TryGetValue((mb.PartNum, mb.ColorId), out var owned);
                    return new BomBrickDto(
                        mb.PartNum, mb.ColorId, brick.Name, brick.PartImg, brick.ColorName, brick.HexColor,
                        mb.Count, mb.SpareCount,
                        owned, 0, brick.BricklinkId);
                }).ToList();

            return Results.Ok(result);
        });

        // Update SetBrickOwned stock (the stock "used in this set instance").
        group.MapPatch("/{setId}/{setIndex:int}/bricks/{partNum}/{colorId}", async (
            string setId, int setIndex, string partNum, string colorId,
            UpdateStockRequest req, HttpContext http, UpdateData updater) =>
        {
            var userId = http.UserId();
            var sbo = new SetBrickOwned { UserId = userId, SetId = setId, SetIndex = setIndex, PartNum = partNum, ColorId = colorId, Stock = req.Stock };
            var ok = updater.UpdateSetBrickOwned(sbo, userId);
            return ok ? Results.Ok() : Results.NotFound();
        });

        // Update BrickOwned stock (the user's loose personal stock of this brick).
        group.MapPatch("/{setId}/{setIndex:int}/loose-bricks/{partNum}/{colorId}", async (
            string setId, int setIndex, string partNum, string colorId,
            UpdateStockRequest req, HttpContext http, UpdateData updater) =>
        {
            var userId = http.UserId();
            var bo = new BrickOwned { UserId = userId, PartNum = partNum, ColorId = colorId, Stock = req.Stock };
            var ok = updater.UpdateBrickOwned(bo, userId);
            return ok ? Results.Ok() : Results.NotFound();
        });

        // Set how many of this fig are present on this set copy (adds/removes linked instances).
        group.MapPatch("/{setId}/{setIndex:int}/minifigs/{minifigId}", async (
            string setId, int setIndex, string minifigId,
            UpdateStockRequest req, HttpContext http, ImportData importer) =>
        {
            var userId = http.UserId();
            await importer.SetSetCopyMinifigCount(userId, setId, setIndex, minifigId, req.Stock);
            return Results.Ok();
        });

        // Set owned stock of a single part on this copy's fig instance.
        group.MapPatch("/{setId}/{setIndex:int}/minifigs/{minifigId}/bricks/{partNum}/{colorId}", async (
            string setId, int setIndex, string minifigId, string partNum, string colorId,
            UpdateStockRequest req, HttpContext http, ImportData importer) =>
        {
            var userId = http.UserId();
            await importer.SetMinifigBrickOwnedStock(userId, setId, setIndex, minifigId, partNum, colorId, req.Stock);
            return Results.Ok();
        });
    }

    public record BomBrickDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Count, int SpareCount, int SetStock, int LooseStock, string? BricklinkId);
    public record BomMinifigDto(string MinifigId, string Name, string? ImgUrl, int Count, int OwnedStock);
    public record BomResponse(string SetId, int SetIndex, string SetName, string ManualUrl, List<int> OwnedInstances, List<string> OwnedSetIds, List<BomBrickDto> Bricks, List<BomMinifigDto> Minifigs, int Percent, string Status);
    public record UpdateStockRequest(int Stock);
    public record CompletenessDto(int Percent, string Status);
}
