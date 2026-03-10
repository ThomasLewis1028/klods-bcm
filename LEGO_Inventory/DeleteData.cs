using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory;

public class DeleteData
{
    private readonly ILogger<ImportData> _logger =
        LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ImportData>();

    public bool DeleteSetInfo(string? setId, bool moveStock = false)
    {
        _logger.LogInformation($"Deleting All Data for set {setId}");

        using var context = new InventoryContext();

        // Delete SetBrickOwned for all instances of this set first (FK constraint order)
        context.Set<SetBrickOwned>()
            .Where(sbo => sbo.SetId == setId)
            .ExecuteDelete();

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
        _logger.LogInformation($"{setId} has been deleted");

        return !context.Set<Set>().Any(s => s.SetId == setId);
    }

    public bool DeleteOwnedSetInfo(int userId, string? setId, int setIndex, bool moveStock = false)
    {
        _logger.LogInformation($"Deleting All Data for set {setId} - {setIndex} (user {userId})");

        using var context = new InventoryContext();

        // Delete SetBrickOwned entries for this specific set copy
        context.Set<SetBrickOwned>()
            .Where(sbo => sbo.UserId == userId && sbo.SetId == setId && sbo.SetIndex == setIndex)
            .ExecuteDelete();

        // Delete the SetOwned record
        context.Set<SetOwned>()
            .Where(s => s.UserId == userId && s.SetId == setId && s.SetIndex == setIndex)
            .ExecuteDelete();

        context.SaveChanges();
        _logger.LogInformation($"{setId}-{setIndex} has been deleted");

        return !context.Set<SetOwned>().Any(s => s.UserId == userId && s.SetId == setId && s.SetIndex == setIndex);
    }

    public bool DeleteSetParts(string? setId)
    {
        using var context = new InventoryContext();

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
        using var context = new InventoryContext();

        if (context.Set<SetBrick>().Any(sb => sb.PartNum == brickId && sb.ColorId == colorId))
            return false;

        context.Set<Brick>()
            .Where(b => b.ColorId == colorId && b.PartNum == brickId)
            .ExecuteDelete();

        context.SaveChanges();
        return !context.Set<Brick>().Any(b => b.ColorId == colorId && b.PartNum == brickId);
    }
}
