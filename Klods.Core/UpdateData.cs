using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods;

public class UpdateData(IDbContextFactory<InventoryContext> contextFactory)
{
    public bool UpdateBrick(Brick brick)
    {
        using var context = contextFactory.CreateDbContext();
        context.Set<Brick>().Update(brick);
        return context.SaveChanges() > 0;
    }

    public bool UpdateSetBrick(SetBrick setBrick)
    {
        using var context = contextFactory.CreateDbContext();
        context.Set<SetBrick>().Update(setBrick);
        return context.SaveChanges() > 0;
    }

    public bool UpdateSetBrickOwned(SetBrickOwned sbo, int callerUserId)
    {
        if (sbo.UserId != callerUserId) return false;
        using var context = contextFactory.CreateDbContext();
        var affected = context.Set<SetBrickOwned>()
            .Where(e => e.UserId == sbo.UserId && e.SetId == sbo.SetId &&
                        e.SetIndex == sbo.SetIndex && e.PartNum == sbo.PartNum &&
                        e.ColorId == sbo.ColorId)
            .ExecuteUpdate(s => s.SetProperty(e => e.Stock, sbo.Stock));
        return affected > 0;
    }

    public bool UpdateBrickOwned(BrickOwned bo, int callerUserId)
    {
        if (bo.UserId != callerUserId) return false;
        using var context = contextFactory.CreateDbContext();
        var affected = context.Set<BrickOwned>()
            .Where(e => e.UserId == bo.UserId && e.PartNum == bo.PartNum && e.ColorId == bo.ColorId)
            .ExecuteUpdate(s => s.SetProperty(e => e.Stock, bo.Stock));
        return affected > 0;
    }

    public enum BulkBrickOp
    {
        ClearFromSet,     // discard all bricks placed in this copy (nothing returned to loose)
        MoveSetToLoose,   // "unbuild" — return every placed brick to loose inventory
        FillFromThinAir,  // bring each part's placed stock up to the required count, no loose deducted
        FillFromLoose,    // bring each part up to required using only loose stock actually on hand
    }

    /// <summary>
    /// Applies a whole-copy brick operation and returns the number of pieces changed/moved
    /// (or -1 if the caller does not own that copy). Minifigs are untouched — they keep their
    /// own per-instance workflow. FillFromLoose already counts substitutions toward a requirement,
    /// so loose stock is only pulled for the genuine remaining shortfall.
    /// </summary>
    public async Task<int> BulkSetBricksAsync(int userId, string setId, int setIndex, BulkBrickOp op)
    {
        await using var context = await contextFactory.CreateDbContextAsync();

        var ownsCopy = await context.Set<SetOwned>()
            .AnyAsync(so => so.UserId == userId && so.SetId == setId && so.SetIndex == setIndex);
        if (!ownsCopy) return -1;

        var setStock = await context.Set<SetBrickOwned>()
            .Where(sbo => sbo.UserId == userId && sbo.SetId == setId && sbo.SetIndex == setIndex)
            .ToListAsync();

        switch (op)
        {
            case BulkBrickOp.ClearFromSet:
            {
                var moved = 0;
                foreach (var sbo in setStock.Where(s => s.Stock > 0))
                {
                    moved += sbo.Stock;
                    sbo.Stock = 0;
                }
                await context.SaveChangesAsync();
                return moved;
            }

            case BulkBrickOp.MoveSetToLoose:
            {
                var toMove = setStock.Where(s => s.Stock > 0).ToList();
                var partNums = toMove.Select(s => s.PartNum).ToHashSet();
                var loose = (await context.Set<BrickOwned>()
                        .Where(bo => bo.UserId == userId && partNums.Contains(bo.PartNum))
                        .ToListAsync())
                    .ToDictionary(bo => (bo.PartNum, bo.ColorId));

                var moved = 0;
                foreach (var sbo in toMove)
                {
                    if (loose.TryGetValue((sbo.PartNum, sbo.ColorId), out var bo))
                        bo.Stock += sbo.Stock;
                    else
                        context.Set<BrickOwned>().Add(new BrickOwned
                            { UserId = userId, PartNum = sbo.PartNum, ColorId = sbo.ColorId, Stock = sbo.Stock });
                    moved += sbo.Stock;
                    sbo.Stock = 0;
                }
                await context.SaveChangesAsync();
                return moved;
            }

            case BulkBrickOp.FillFromThinAir:
            {
                var bom = await context.Set<SetBrick>().AsNoTracking()
                    .Where(sb => sb.SetId == setId)
                    .Select(sb => new { sb.PartNum, sb.ColorId, sb.Count })
                    .ToListAsync();
                var byKey = setStock.ToDictionary(s => (s.PartNum, s.ColorId));

                var added = 0;
                foreach (var req in bom)
                {
                    if (byKey.TryGetValue((req.PartNum, req.ColorId), out var sbo))
                    {
                        if (req.Count <= sbo.Stock) continue;
                        added += req.Count - sbo.Stock;
                        sbo.Stock = req.Count;
                    }
                    else
                    {
                        added += req.Count;
                        context.Set<SetBrickOwned>().Add(new SetBrickOwned
                        {
                            UserId = userId, SetId = setId, SetIndex = setIndex,
                            PartNum = req.PartNum, ColorId = req.ColorId, Stock = req.Count,
                        });
                    }
                }
                await context.SaveChangesAsync();
                return added;
            }

            case BulkBrickOp.FillFromLoose:
            {
                var bom = await context.Set<SetBrick>().AsNoTracking()
                    .Where(sb => sb.SetId == setId)
                    .Select(sb => new { sb.PartNum, sb.ColorId, sb.Count })
                    .ToListAsync();

                var subbed = (await context.Set<SetBrickSubstitution>().AsNoTracking()
                        .Where(s => s.UserId == userId && s.SetId == setId && s.SetIndex == setIndex)
                        .Select(s => new { s.ReqPartNum, s.ReqColorId, s.Count })
                        .ToListAsync())
                    .GroupBy(s => (s.ReqPartNum, s.ReqColorId))
                    .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));

                var byKey = setStock.ToDictionary(s => (s.PartNum, s.ColorId));
                var reqPartNums = bom.Select(b => b.PartNum).ToHashSet();
                var loose = (await context.Set<BrickOwned>()
                        .Where(bo => bo.UserId == userId && reqPartNums.Contains(bo.PartNum))
                        .ToListAsync())
                    .ToDictionary(bo => (bo.PartNum, bo.ColorId));

                var moved = 0;
                foreach (var req in bom)
                {
                    var key = (req.PartNum, req.ColorId);
                    if (!loose.TryGetValue(key, out var bo) || bo.Stock <= 0) continue;

                    byKey.TryGetValue(key, out var sbo);
                    var shortfall = req.Count - (sbo?.Stock ?? 0) - subbed.GetValueOrDefault(key, 0);
                    var pull = Math.Min(shortfall, bo.Stock);
                    if (pull <= 0) continue;

                    if (sbo is null)
                    {
                        sbo = new SetBrickOwned
                        {
                            UserId = userId, SetId = setId, SetIndex = setIndex,
                            PartNum = req.PartNum, ColorId = req.ColorId, Stock = 0,
                        };
                        context.Set<SetBrickOwned>().Add(sbo);
                    }
                    sbo.Stock += pull;
                    bo.Stock -= pull;
                    moved += pull;
                }
                await context.SaveChangesAsync();
                return moved;
            }

            default:
                return 0;
        }
    }

    public bool UpdateMinifig(Minifig minifig)
    {
        using var context = contextFactory.CreateDbContext();
        context.Set<Minifig>().Update(minifig);
        return context.SaveChanges() > 0;
    }

    public bool UpdateSetMinifig(SetMinifig setMinifig)
    {
        using var context = contextFactory.CreateDbContext();
        context.Set<SetMinifig>().Update(setMinifig);
        return context.SaveChanges() > 0;
    }
}
