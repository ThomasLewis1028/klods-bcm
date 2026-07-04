using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods;

public class DeleteData(IDbContextFactory<InventoryContext> contextFactory, ILogger<DeleteData> logger)
{

    public bool DeleteSetInfo(string? setId, bool moveStock = false)
    {
        logger.LogInformation($"Deleting All Data for set {setId}");

        using var context = contextFactory.CreateDbContext();

        // Delete SetBrickOwned for all instances of this set first (FK constraint order)
        context.Set<SetBrickOwned>()
            .Where(sbo => sbo.SetId == setId)
            .ExecuteDelete();

        // Release any owned minifigs tied to this set's copies back to loose (clears FK to SetOwned)
        context.Set<MinifigOwned>()
            .Where(mo => mo.SetId == setId)
            .ExecuteUpdate(s => s
                .SetProperty(mo => mo.SetId, (string?)null)
                .SetProperty(mo => mo.SetIndex, (int?)null));

        // Delete SetOwned instances
        context.Set<SetOwned>()
            .Where(s => s.SetId == setId)
            .ExecuteDelete();

        // Delete SetBrick BOM entries
        context.Set<SetBrick>()
            .Where(sb => sb.SetId == setId)
            .ExecuteDelete();

        // Delete SetMinifig BOM entries
        context.Set<SetMinifig>()
            .Where(sm => sm.SetId == setId)
            .ExecuteDelete();

        // Delete the set itself
        context.Set<Set>()
            .Where(s => s.SetId == setId)
            .ExecuteDelete();

        context.SaveChanges();
        logger.LogInformation($"{setId} has been deleted");

        return !context.Set<Set>().Any(s => s.SetId == setId);
    }

    public bool DeleteOwnedSetInfo(int userId, string? setId, int setIndex, bool moveStock = false)
    {
        logger.LogInformation($"Deleting All Data for set {setId} - {setIndex} (user {userId})");

        using var context = contextFactory.CreateDbContext();

        if (moveStock)
        {
            var brickOwnedCtx = context.Set<BrickOwned>();

            // Return set-brick stock to loose inventory
            var setBrickStock = context.Set<SetBrickOwned>()
                .Where(sbo => sbo.UserId == userId && sbo.SetId == setId && sbo.SetIndex == setIndex && sbo.Stock > 0)
                .ToList();

            foreach (var sbo in setBrickStock)
            {
                var loose = brickOwnedCtx.FirstOrDefault(bo => bo.UserId == userId && bo.PartNum == sbo.PartNum && bo.ColorId == sbo.ColorId);
                if (loose is not null)
                    loose.Stock += sbo.Stock;
                else
                    brickOwnedCtx.Add(new BrickOwned { UserId = userId, PartNum = sbo.PartNum, ColorId = sbo.ColorId, Stock = sbo.Stock });
            }

            // Return minifig-part stock to loose inventory
            var minifigIndices = context.Set<MinifigOwned>()
                .Where(mo => mo.UserId == userId && mo.SetId == setId && mo.SetIndex == setIndex)
                .Select(mo => new { mo.MinifigId, mo.MinifigIndex })
                .ToList();

            foreach (var fig in minifigIndices)
            {
                var figStock = context.Set<MinifigBrickOwned>()
                    .Where(mbo => mbo.UserId == userId && mbo.MinifigId == fig.MinifigId && mbo.MinifigIndex == fig.MinifigIndex && mbo.Stock > 0)
                    .ToList();

                foreach (var mbo in figStock)
                {
                    var loose = brickOwnedCtx.FirstOrDefault(bo => bo.UserId == userId && bo.PartNum == mbo.PartNum && bo.ColorId == mbo.ColorId);
                    if (loose is not null)
                        loose.Stock += mbo.Stock;
                    else
                        brickOwnedCtx.Add(new BrickOwned { UserId = userId, PartNum = mbo.PartNum, ColorId = mbo.ColorId, Stock = mbo.Stock });
                }
            }

            context.SaveChanges();
        }

        // Delete SetBrickOwned entries for this specific set copy
        context.Set<SetBrickOwned>()
            .Where(sbo => sbo.UserId == userId && sbo.SetId == setId && sbo.SetIndex == setIndex)
            .ExecuteDelete();

        // Delete this copy's owned minifigs (MinifigBrickOwned cascades at the DB)
        context.Set<MinifigOwned>()
            .Where(mo => mo.UserId == userId && mo.SetId == setId && mo.SetIndex == setIndex)
            .ExecuteDelete();

        // Delete the SetOwned record
        context.Set<SetOwned>()
            .Where(s => s.UserId == userId && s.SetId == setId && s.SetIndex == setIndex)
            .ExecuteDelete();

        context.SaveChanges();
        logger.LogInformation($"{setId}-{setIndex} has been deleted");

        return !context.Set<SetOwned>().Any(s => s.UserId == userId && s.SetId == setId && s.SetIndex == setIndex);
    }

    public bool DeleteSetParts(string? setId)
    {
        using var context = contextFactory.CreateDbContext();

        if (!context.Set<Set>().Any(s => s.SetId == setId))
            return false;

        // Delete SetBrickOwned first (references SetBrick)
        context.Set<SetBrickOwned>()
            .Where(sbo => sbo.SetId == setId)
            .ExecuteDelete();

        context.Set<SetBrick>()
            .Where(sb => sb.SetId == setId)
            .ExecuteDelete();

        context.SaveChanges();
        return !context.Set<SetBrick>().Any(sb => sb.SetId == setId);
    }

    public bool DeleteBricks(string? brickId, string? colorId)
    {
        using var context = contextFactory.CreateDbContext();

        if (context.Set<SetBrick>().Any(sb => sb.PartNum == brickId && sb.ColorId == colorId))
            return false;

        context.Set<Brick>()
            .Where(b => b.ColorId == colorId && b.PartNum == brickId)
            .ExecuteDelete();

        context.SaveChanges();
        return !context.Set<Brick>().Any(b => b.ColorId == colorId && b.PartNum == brickId);
    }
}
