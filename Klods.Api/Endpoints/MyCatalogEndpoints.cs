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
        group.MapGet("/", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
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

            var bricks = (await db.Set<Brick>().AsNoTracking().Where(b => allPartNums.Contains(b.PartNum)).ToListAsync())
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

            return Results.Ok(result);
        });

        // Upsert loose brick stock — creates BrickOwned if it doesn't exist yet.
        group.MapPut("/{partNum}/{colorId}/stock", async (
            string partNum, string colorId, UpdateStockRequest req, HttpContext http,
            IDbContextFactory<InventoryContext> dbFactory) =>
        {
            if (!req.IsValid) return Results.BadRequest($"Stock must be between 0 and {UpdateStockRequest.MaxStock}.");

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
        group.MapGet("/", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var userSetCopies = await InventoryAggregates.GetSetCopiesAsync(db, userId);
            var userSetIds    = userSetCopies.Keys.ToList();

            var setMinifigs  = await db.Set<SetMinifig>().AsNoTracking().Where(sm => userSetIds.Contains(sm.SetId)).ToListAsync();
            var neededDict   = InventoryAggregates.GetMinifigNeededDict(setMinifigs, userSetCopies);
            var setCountDict = InventoryAggregates.GetMinifigSetCountDict(setMinifigs);

            // Split owned instances into loose (no set link) and in-use (attached to a set copy).
            var ownedInstances = await db.Set<MinifigOwned>().AsNoTracking()
                .Where(mo => mo.UserId == userId)
                .Select(mo => new { mo.MinifigId, IsLoose = mo.SetId == null })
                .ToListAsync();
            var looseCounts = ownedInstances.Where(o => o.IsLoose)
                .GroupBy(o => o.MinifigId).ToDictionary(g => g.Key, g => g.Count());
            var inUseCounts = ownedInstances.Where(o => !o.IsLoose)
                .GroupBy(o => o.MinifigId).ToDictionary(g => g.Key, g => g.Count());

            var allIds = neededDict.Keys.Union(looseCounts.Keys).Union(inUseCounts.Keys).ToHashSet();

            var partCounts = (await db.Set<MinifigBrick>().AsNoTracking()
                .Where(mb => allIds.Contains(mb.MinifigId))
                .ToListAsync())
                .GroupBy(mb => mb.MinifigId)
                .ToDictionary(g => g.Key, g => g.Count());

            var minifigs = await db.Set<Minifig>().AsNoTracking().Where(m => allIds.Contains(m.MinifigId)).ToListAsync();

            var result = minifigs.Select(m =>
                new MyMinifigDto(
                    m.MinifigId, m.Name, m.ImgUrl,
                    looseCounts.GetValueOrDefault(m.MinifigId, 0),
                    inUseCounts.GetValueOrDefault(m.MinifigId, 0),
                    neededDict.GetValueOrDefault(m.MinifigId, 0),
                    setCountDict.GetValueOrDefault(m.MinifigId, 0),
                    partCounts.GetValueOrDefault(m.MinifigId, 0))).ToList();

            return Results.Ok(result);
        });

        // Set the user's loose count for a fig (adds/removes loose instances). SetLooseMinifigCount
        // inserts one MinifigOwned row per unit, so this needs the tighter row-insert-loop cap, not
        // the generic stock cap used for plain counter fields below.
        group.MapPut("/{minifigId}/stock", async (
            string minifigId, UpdateStockRequest req, HttpContext http, ImportData importer) =>
        {
            if (req.Stock is < 0 or > MinifigsEndpoints.AddOwnedMinifigRequest.MaxCount)
                return Results.BadRequest($"Stock must be between 0 and {MinifigsEndpoints.AddOwnedMinifigRequest.MaxCount}.");

            var userId = http.UserId();
            await importer.SetLooseMinifigCount(userId, minifigId, req.Stock);
            return Results.Ok();
        });

        // Lazy-load: a loose fig's parts with the owned stock for your loose copy (editable).
        group.MapGet("/{minifigId}/loose-bricks", async (string minifigId, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var minifigBricks = await db.Set<MinifigBrick>().AsNoTracking()
                .Where(mb => mb.MinifigId == minifigId).ToListAsync();
            var brickIds = minifigBricks.Select(mb => mb.PartNum).ToHashSet();
            var brickDict = (await db.Set<Brick>().AsNoTracking().Where(b => brickIds.Contains(b.PartNum)).ToListAsync())
                .ToDictionary(b => (b.PartNum, b.ColorId ?? ""));

            // Owned stock tracked against the user's lowest-indexed loose instance of this fig.
            var instanceIndex = await db.Set<MinifigOwned>().AsNoTracking()
                .Where(mo => mo.UserId == userId && mo.MinifigId == minifigId && mo.SetId == null)
                .OrderBy(mo => mo.MinifigIndex)
                .Select(mo => (int?)mo.MinifigIndex)
                .FirstOrDefaultAsync();

            var ownedDict = instanceIndex == null
                ? new Dictionary<(string, string), int>()
                : (await db.Set<MinifigBrickOwned>().AsNoTracking()
                        .Where(x => x.UserId == userId && x.MinifigId == minifigId && x.MinifigIndex == instanceIndex.Value)
                        .ToListAsync())
                    .ToDictionary(x => (x.PartNum, x.ColorId), x => x.Stock);

            var rows = minifigBricks
                .Where(mb => brickDict.ContainsKey((mb.PartNum, mb.ColorId)))
                .Select(mb =>
                {
                    var b = brickDict[(mb.PartNum, mb.ColorId)];
                    ownedDict.TryGetValue((mb.PartNum, mb.ColorId), out var owned);
                    return new MyMinifigBrickDto(mb.PartNum, mb.ColorId, b.Name, b.PartImg, b.ColorName, b.HexColor, mb.Count, owned);
                }).ToList();

            return Results.Ok(rows);
        });

        // Set owned stock of a single part on your loose copy of a fig.
        group.MapPatch("/{minifigId}/loose-bricks/{partNum}/{colorId}", async (
            string minifigId, string partNum, string colorId, UpdateStockRequest req, HttpContext http, ImportData importer) =>
        {
            if (!req.IsValid) return Results.BadRequest($"Stock must be between 0 and {UpdateStockRequest.MaxStock}.");

            var userId = http.UserId();
            await importer.SetLooseMinifigBrickOwnedStock(userId, minifigId, partNum, colorId, req.Stock);
            return Results.Ok();
        });

        // Every owned instance of a fig with its location and per-part completeness.
        group.MapGet("/{minifigId}/instances", async (
            string minifigId, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var instances = await db.Set<MinifigOwned>().AsNoTracking()
                .Where(mo => mo.UserId == userId && mo.MinifigId == minifigId)
                .OrderBy(mo => mo.MinifigIndex).ToListAsync();
            if (instances.Count == 0) return Results.Ok(new List<MinifigInstanceDto>());

            var reqParts = await db.Set<MinifigBrick>().AsNoTracking()
                .Where(mb => mb.MinifigId == minifigId).ToListAsync();
            var partNums = reqParts.Select(p => p.PartNum).ToHashSet();
            var brickInfo = (await db.Set<Brick>().AsNoTracking().Where(b => partNums.Contains(b.PartNum)).ToListAsync())
                .ToDictionary(b => (b.PartNum, b.ColorId ?? ""));

            var indices = instances.Select(i => i.MinifigIndex).ToList();
            var ownedByIndex = (await db.Set<MinifigBrickOwned>().AsNoTracking()
                    .Where(x => x.UserId == userId && x.MinifigId == minifigId && indices.Contains(x.MinifigIndex))
                    .ToListAsync())
                .GroupBy(x => x.MinifigIndex)
                .ToDictionary(g => g.Key, g => g.ToDictionary(x => (x.PartNum, x.ColorId), x => x.Stock));

            var setIds = instances.Where(i => i.SetId != null).Select(i => i.SetId!).Distinct().ToList();
            var sets = (await db.Set<Set>().AsNoTracking().Where(s => setIds.Contains(s.SetId)).ToListAsync())
                .ToDictionary(s => s.SetId);

            var result = instances.Select(inst =>
            {
                var owned = ownedByIndex.GetValueOrDefault(inst.MinifigIndex) ?? new Dictionary<(string, string), int>();
                var parts = reqParts.Select(p =>
                {
                    brickInfo.TryGetValue((p.PartNum, p.ColorId), out var b);
                    owned.TryGetValue((p.PartNum, p.ColorId), out var have);
                    return new MinifigInstancePartDto(p.PartNum, p.ColorId, b?.Name ?? p.PartNum,
                        b?.PartImg, b?.ColorName, b?.HexColor, p.Count, have);
                }).ToList();
                sets.TryGetValue(inst.SetId ?? "", out var set);
                return new MinifigInstanceDto(inst.MinifigIndex, inst.SetId, inst.SetIndex, set?.Name, set?.SetImg, parts);
            }).ToList();

            return Results.Ok(result);
        });

        // Owned set copies that include this fig and still have a free slot — reassignment targets.
        group.MapGet("/{minifigId}/assignable-copies", async (
            string minifigId, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var required = await db.Set<SetMinifig>().AsNoTracking()
                .Where(sm => sm.MinifigId == minifigId)
                .ToDictionaryAsync(sm => sm.SetId, sm => sm.Count);
            if (required.Count == 0) return Results.Ok(new List<AssignableCopyDto>());

            var reqSetIds = required.Keys.ToList();
            var copies = await db.Set<SetOwned>().AsNoTracking()
                .Where(so => so.UserId == userId && reqSetIds.Contains(so.SetId)).ToListAsync();

            var tied = (await db.Set<MinifigOwned>().AsNoTracking()
                    .Where(mo => mo.UserId == userId && mo.MinifigId == minifigId && mo.SetId != null).ToListAsync())
                .GroupBy(mo => (mo.SetId!, mo.SetIndex!.Value))
                .ToDictionary(g => g.Key, g => g.Count());

            var sets = (await db.Set<Set>().AsNoTracking().Where(s => reqSetIds.Contains(s.SetId)).ToListAsync())
                .ToDictionary(s => s.SetId);

            var result = copies
                .Where(c => tied.GetValueOrDefault((c.SetId, c.SetIndex), 0) < required[c.SetId])
                .OrderBy(c => c.SetId).ThenBy(c => c.SetIndex)
                .Select(c => new AssignableCopyDto(c.SetId,
                    sets.TryGetValue(c.SetId, out var s) ? s.Name : c.SetId,
                    sets.GetValueOrDefault(c.SetId)?.SetImg, c.SetIndex))
                .ToList();

            return Results.Ok(result);
        });

        // Set a single part's owned stock for a specific fig instance.
        group.MapPatch("/{minifigId}/instances/{index:int}/parts/{partNum}/{colorId}", async (
            string minifigId, int index, string partNum, string colorId, UpdateStockRequest req,
            HttpContext http, ImportData importer) =>
        {
            if (!req.IsValid) return Results.BadRequest($"Stock must be between 0 and {UpdateStockRequest.MaxStock}.");

            var userId = http.UserId();
            await importer.SetMinifigInstancePartStock(userId, minifigId, index, partNum, colorId, req.Stock);
            return Results.Ok();
        });

        // Move an instance onto a set copy, or back to loose (null set).
        group.MapPatch("/{minifigId}/instances/{index:int}/assign", async (
            string minifigId, int index, AssignInstanceRequest req, HttpContext http,
            ImportData importer, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();

            if (req.SetId != null && req.SetIndex != null)
            {
                await using var db = dbFactory.CreateDbContext();
                var required = await db.Set<SetMinifig>().AsNoTracking()
                    .Where(sm => sm.SetId == req.SetId && sm.MinifigId == minifigId)
                    .Select(sm => (int?)sm.Count).FirstOrDefaultAsync();
                if (required is null) return Results.BadRequest("That set doesn't include this minifig.");

                var tied = await db.Set<MinifigOwned>().AsNoTracking().CountAsync(mo =>
                    mo.UserId == userId && mo.MinifigId == minifigId &&
                    mo.SetId == req.SetId && mo.SetIndex == req.SetIndex && mo.MinifigIndex != index);
                if (tied >= required.Value) return Results.BadRequest("That set copy is already full.");
            }

            var ok = await importer.ReassignMinifigInstance(userId, minifigId, index, req.SetId, req.SetIndex);
            return ok ? Results.Ok() : Results.NotFound();
        });

        // Add a loose instance.
        group.MapPost("/{minifigId}/instances", async (string minifigId, HttpContext http, ImportData importer) =>
        {
            var userId = http.UserId();
            var index = await importer.AddLooseMinifigInstance(userId, minifigId);
            return Results.Ok(new NewInstanceDto(index));
        });

        // Remove an instance.
        group.MapDelete("/{minifigId}/instances/{index:int}", async (
            string minifigId, int index, HttpContext http, ImportData importer) =>
        {
            var userId = http.UserId();
            var ok = await importer.RemoveMinifigInstance(userId, minifigId, index);
            return ok ? Results.Ok() : Results.NotFound();
        });
    }

    public record UpdateStockRequest(int Stock)
    {
        // Generous ceiling — no real collection gets anywhere near this — that just keeps a stray
        // huge value from a client out of stock sums (InventoryAggregates, catalog totals, etc.).
        public const int MaxStock = 1_000_000;
        public bool IsValid => Stock is >= 0 and <= MaxStock;
    }
    public record MyBrickDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId, int Stock, int UserNeeded, int UserSetCount);
    public record MyBrickSetDetailDto(string SetId, string SetName, string? SetImg, int BrickCount, int CopiesOwned);
    public record MyMinifigDto(string MinifigId, string MinifigName, string? ImgUrl, int Stock, int InUseStock, int UserNeeded, int UserSetCount, int PartCount);
    public record MyMinifigBrickDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Need, int Owned);

    public record MinifigInstanceDto(int Index, string? SetId, int? SetIndex, string? SetName, string? SetImg, List<MinifigInstancePartDto> Parts);
    public record MinifigInstancePartDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Need, int Owned);
    public record AssignableCopyDto(string SetId, string SetName, string? SetImg, int SetIndex);
    public record AssignInstanceRequest(string? SetId, int? SetIndex);
    public record NewInstanceDto(int Index);
}
