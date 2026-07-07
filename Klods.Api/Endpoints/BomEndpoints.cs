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

            var ownedCopy = await db.Set<SetOwned>().AsNoTracking()
                .FirstOrDefaultAsync(so => so.SetId == setId && so.SetIndex == setIndex && so.UserId == userId);
            if (ownedCopy is null) return Results.NotFound();

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
                .GetValueOrDefault((setId, setIndex)) ?? new SetCompleteness.Result(0, SetCompleteness.Status.Short, 0, 0, 0, 0);

            return Results.Ok(new BomResponse(
                setId, setIndex, set.Name, set.ManualUrl,
                ownedInstances, ownedSetIds,
                brickItems, minifigItems,
                comp.Percent, comp.Status.ToString().ToLowerInvariant(), comp.SubstitutedPercent, comp.HaveSubstituted > 0,
                ownedCopy.Location, ownedCopy.Notes));
        });

        // Just the completeness for a copy — cheap enough to re-poll after each edit for a live bar.
        group.MapGet("/{setId}/{setIndex:int}/completeness", async (
            string setId, int setIndex, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var comp = (await SetCompleteness.ComputeAsync(db, userId, new[] { (setId, setIndex) }))
                .GetValueOrDefault((setId, setIndex)) ?? new SetCompleteness.Result(0, SetCompleteness.Status.Short, 0, 0, 0, 0);
            return Results.Ok(new CompletenessDto(comp.Percent, comp.Status.ToString().ToLowerInvariant(), comp.SubstitutedPercent, comp.HaveSubstituted > 0));
        });

        // Fig instances tied to THIS set copy, each with its own per-part completeness (one row per physical fig).
        group.MapGet("/{setId}/{setIndex:int}/minifigs/{minifigId}/instances", async (
            string setId, int setIndex, string minifigId, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var instances = await db.Set<MinifigOwned>().AsNoTracking()
                .Where(mo => mo.UserId == userId && mo.MinifigId == minifigId && mo.SetId == setId && mo.SetIndex == setIndex)
                .OrderBy(mo => mo.MinifigIndex).ToListAsync();
            if (instances.Count == 0) return Results.Ok(new List<BomMinifigInstanceDto>());

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

            var result = instances.Select(inst =>
            {
                var owned = ownedByIndex.GetValueOrDefault(inst.MinifigIndex) ?? new Dictionary<(string, string), int>();
                var parts = reqParts
                    .Where(p => brickInfo.ContainsKey((p.PartNum, p.ColorId)))
                    .Select(p =>
                    {
                        var b = brickInfo[(p.PartNum, p.ColorId)];
                        owned.TryGetValue((p.PartNum, p.ColorId), out var have);
                        return new BomMinifigInstancePartDto(p.PartNum, p.ColorId, b.Name, b.PartImg, b.ColorName, b.HexColor, p.Count, have);
                    }).ToList();
                return new BomMinifigInstanceDto(inst.MinifigIndex, parts);
            }).ToList();

            return Results.Ok(result);
        });

        // Add one fig instance to this set copy ("I have this one") — returns the new instance index.
        group.MapPost("/{setId}/{setIndex:int}/minifigs/{minifigId}/instances", async (
            string setId, int setIndex, string minifigId, HttpContext http, ImportData importer) =>
        {
            var userId = http.UserId();
            var index = await importer.AddMinifigInstance(userId, minifigId, setId, setIndex);
            return Results.Ok(new NewInstanceDto(index));
        });

        // Update SetBrickOwned stock (the stock "used in this set instance").
        group.MapPatch("/{setId}/{setIndex:int}/bricks/{partNum}/{colorId}", async (
            string setId, int setIndex, string partNum, string colorId,
            UpdateStockRequest req, HttpContext http, UpdateData updater) =>
        {
            if (!req.IsValid) return Results.BadRequest($"Stock must be between 0 and {UpdateStockRequest.MaxStock}.");

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
            if (!req.IsValid) return Results.BadRequest($"Stock must be between 0 and {UpdateStockRequest.MaxStock}.");

            var userId = http.UserId();
            var bo = new BrickOwned { UserId = userId, PartNum = partNum, ColorId = colorId, Stock = req.Stock };
            var ok = updater.UpdateBrickOwned(bo, userId);
            return ok ? Results.Ok() : Results.NotFound();
        });

        // Whole-copy brick operation: clear, unbuild-to-loose, fill-from-thin-air, or fill-from-loose.
        // Returns how many pieces were changed/moved.
        group.MapPost("/{setId}/{setIndex:int}/bulk-bricks", async (
            string setId, int setIndex, BulkBricksRequest req, HttpContext http, UpdateData updater) =>
        {
            if (!Enum.TryParse<UpdateData.BulkBrickOp>(req.Operation, ignoreCase: true, out var op))
                return Results.BadRequest("Unknown operation.");

            var userId = http.UserId();
            var affected = await updater.BulkSetBricksAsync(userId, setId, setIndex, op);
            return affected < 0 ? Results.NotFound() : Results.Ok(new BulkBricksResult(affected));
        });

        // Remove a specific fig instance from this copy (deletes the droid; its parts cascade at the DB).
        group.MapDelete("/{setId}/{setIndex:int}/minifigs/{minifigId}/instances/{index:int}", async (
            string minifigId, int index, HttpContext http, ImportData importer) =>
        {
            var userId = http.UserId();
            var ok = await importer.RemoveMinifigInstance(userId, minifigId, index);
            return ok ? Results.Ok() : Results.NotFound();
        });

        // Set owned stock of a single part for a specific fig instance on this copy.
        group.MapPatch("/{setId}/{setIndex:int}/minifigs/{minifigId}/instances/{index:int}/parts/{partNum}/{colorId}", async (
            string minifigId, int index, string partNum, string colorId,
            UpdateStockRequest req, HttpContext http, ImportData importer) =>
        {
            if (!req.IsValid) return Results.BadRequest($"Stock must be between 0 and {UpdateStockRequest.MaxStock}.");

            var userId = http.UserId();
            await importer.SetMinifigInstancePartStock(userId, minifigId, index, partNum, colorId, req.Stock);
            return Results.Ok();
        });

        // All substitution fills recorded on this copy (across every requirement), with substitute display info.
        group.MapGet("/{setId}/{setIndex:int}/substitutions", async (
            string setId, int setIndex, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var subs = await db.Set<SetBrickSubstitution>().AsNoTracking()
                .Where(s => s.UserId == userId && s.SetId == setId && s.SetIndex == setIndex)
                .ToListAsync();
            if (subs.Count == 0) return Results.Ok(new List<SubstitutionDto>());

            var subPartNums = subs.Select(s => s.SubPartNum).ToHashSet();
            var brickDict = (await db.Set<Brick>().AsNoTracking()
                    .Where(b => subPartNums.Contains(b.PartNum))
                    .ToListAsync())
                .ToDictionary(b => (b.PartNum, b.ColorId ?? ""));

            // The user's current loose stock of each substitute — bounds how much a fill can pull from loose.
            var looseDict = (await db.Set<BrickOwned>().AsNoTracking()
                    .Where(bo => bo.UserId == userId && subPartNums.Contains(bo.PartNum))
                    .ToListAsync())
                .ToDictionary(bo => (bo.PartNum, bo.ColorId), bo => bo.Stock);

            var result = subs.Select(s =>
            {
                brickDict.TryGetValue((s.SubPartNum, s.SubColorId), out var b);
                return new SubstitutionDto(s.Id, s.ReqPartNum, s.ReqColorId, s.SubPartNum, s.SubColorId,
                    b?.Name, b?.PartImg, b?.ColorName, b?.HexColor,
                    looseDict.GetValueOrDefault((s.SubPartNum, s.SubColorId), 0), s.Count, s.PulledFromLoose, s.Notes);
            }).ToList();
            return Results.Ok(result);
        });

        // Record a substitution fill toward a requirement, pulling from loose stock as available.
        group.MapPost("/{setId}/{setIndex:int}/substitutions", async (
            string setId, int setIndex, AddSubstitutionRequest req, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            if (req.Count is < 1 or > UpdateStockRequest.MaxStock)
                return Results.BadRequest($"Count must be between 1 and {UpdateStockRequest.MaxStock}.");

            await using var db = dbFactory.CreateDbContext();

            var ownsCopy = await db.Set<SetOwned>()
                .AnyAsync(so => so.UserId == userId && so.SetId == setId && so.SetIndex == setIndex);
            if (!ownsCopy) return Results.NotFound();

            var reqExists = await db.Set<SetBrick>()
                .AnyAsync(sb => sb.SetId == setId && sb.PartNum == req.ReqPartNum && sb.ColorId == req.ReqColorId);
            if (!reqExists) return Results.NotFound();

            var subExists = await db.Set<Brick>()
                .AnyAsync(b => b.PartNum == req.SubPartNum && b.ColorId == req.SubColorId);
            if (!subExists) return Results.NotFound();

            // Pull exactly what the user chose from loose, bounded by the count and what they actually have.
            // The remainder of the count is declared "from thin air" with no deduction.
            var loose = await db.Set<BrickOwned>()
                .FirstOrDefaultAsync(bo => bo.UserId == userId && bo.PartNum == req.SubPartNum && bo.ColorId == req.SubColorId);
            var pulled = Math.Clamp(req.PulledFromLoose, 0, Math.Min(req.Count, loose?.Stock ?? 0));
            if (pulled > 0) loose!.Stock -= pulled;

            db.Set<SetBrickSubstitution>().Add(new SetBrickSubstitution
            {
                UserId = userId, SetId = setId, SetIndex = setIndex,
                ReqPartNum = req.ReqPartNum, ReqColorId = req.ReqColorId,
                SubPartNum = req.SubPartNum, SubColorId = req.SubColorId,
                Count = req.Count, PulledFromLoose = pulled,
                Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            });
            await db.SaveChangesAsync();
            return Results.Ok(new NewSubstitutionDto(pulled));
        });

        // Adjust an existing fill's total count and/or how much of it is pulled from loose, moving
        // loose stock in or out to match. Loose can never be over-drawn.
        group.MapPatch("/{setId}/{setIndex:int}/substitutions/{id:int}", async (
            string setId, int setIndex, int id, UpdateSubstitutionRequest req, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            if (req.Count is < 1 or > UpdateStockRequest.MaxStock)
                return Results.BadRequest($"Count must be between 1 and {UpdateStockRequest.MaxStock}.");

            await using var db = dbFactory.CreateDbContext();

            var sub = await db.Set<SetBrickSubstitution>()
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && s.SetId == setId && s.SetIndex == setIndex);
            if (sub is null) return Results.NotFound();

            var loose = await db.Set<BrickOwned>()
                .FirstOrDefaultAsync(bo => bo.UserId == userId && bo.PartNum == sub.SubPartNum && bo.ColorId == sub.SubColorId);

            // Target pull is bounded by the new count; an increase is further bounded by loose on hand.
            var desired = Math.Clamp(req.PulledFromLoose, 0, req.Count);
            var delta = desired - sub.PulledFromLoose;      // >0 draws more from loose, <0 returns to loose
            if (delta > (loose?.Stock ?? 0)) delta = loose?.Stock ?? 0;

            if (delta != 0)
            {
                if (loose is null)
                    db.Set<BrickOwned>().Add(new BrickOwned
                    {
                        UserId = userId, PartNum = sub.SubPartNum, ColorId = sub.SubColorId, Stock = -delta,
                    });
                else
                    loose.Stock -= delta;
            }

            sub.Count = req.Count;
            sub.PulledFromLoose += delta;
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // Remove a substitution fill, returning whatever it pulled from loose back to loose.
        group.MapDelete("/{setId}/{setIndex:int}/substitutions/{id:int}", async (
            string setId, int setIndex, int id, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var sub = await db.Set<SetBrickSubstitution>()
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId && s.SetId == setId && s.SetIndex == setIndex);
            if (sub is null) return Results.NotFound();

            if (sub.PulledFromLoose > 0)
            {
                var loose = await db.Set<BrickOwned>()
                    .FirstOrDefaultAsync(bo => bo.UserId == userId && bo.PartNum == sub.SubPartNum && bo.ColorId == sub.SubColorId);
                if (loose is null)
                    db.Set<BrickOwned>().Add(new BrickOwned
                    {
                        UserId = userId, PartNum = sub.SubPartNum, ColorId = sub.SubColorId, Stock = sub.PulledFromLoose,
                    });
                else
                    loose.Stock += sub.PulledFromLoose;
            }

            db.Set<SetBrickSubstitution>().Remove(sub);
            await db.SaveChangesAsync();
            return Results.Ok();
        });
    }

    public record BomBrickDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Count, int SpareCount, int SetStock, int LooseStock, string? BricklinkId);
    public record BomMinifigDto(string MinifigId, string Name, string? ImgUrl, int Count, int OwnedStock);
    public record BomMinifigInstanceDto(int Index, List<BomMinifigInstancePartDto> Parts);
    public record BomMinifigInstancePartDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Need, int Owned);
    public record BomResponse(string SetId, int SetIndex, string SetName, string ManualUrl, List<int> OwnedInstances, List<string> OwnedSetIds, List<BomBrickDto> Bricks, List<BomMinifigDto> Minifigs, int Percent, string Status, int SubPercent, bool Substituted, string? Location, string? Notes);
    public record UpdateStockRequest(int Stock)
    {
        // Generous ceiling — no real collection gets anywhere near this — that just keeps a stray
        // huge value from a client out of stock sums (InventoryAggregates, catalog totals, etc.).
        public const int MaxStock = 1_000_000;
        public bool IsValid => Stock is >= 0 and <= MaxStock;
    }
    public record NewInstanceDto(int Index);
    public record CompletenessDto(int Percent, string Status, int SubPercent, bool Substituted);
    public record SubstitutionDto(int Id, string ReqPartNum, string ReqColorId, string SubPartNum, string SubColorId, string? SubName, string? SubPartImg, string? SubColorName, string? SubHexColor, int SubLooseStock, int Count, int PulledFromLoose, string? Notes);
    public record AddSubstitutionRequest(string ReqPartNum, string ReqColorId, string SubPartNum, string SubColorId, int Count, int PulledFromLoose, string? Notes);
    public record UpdateSubstitutionRequest(int Count, int PulledFromLoose);
    public record NewSubstitutionDto(int PulledFromLoose);
    public record BulkBricksRequest(string Operation);
    public record BulkBricksResult(int PiecesAffected);
}
