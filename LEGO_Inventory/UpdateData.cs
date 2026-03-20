using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory;

public class UpdateData
{
    public bool UpdateBrick(Brick brick)
    {
        using var context = new InventoryContext();
        context.Set<Brick>().Update(brick);
        return context.SaveChanges() > 0;
    }

    public bool UpdateSetBrick(SetBrick setBrick)
    {
        using var context = new InventoryContext();
        context.Set<SetBrick>().Update(setBrick);
        return context.SaveChanges() > 0;
    }

    public bool UpdateSetBrickOwned(SetBrickOwned sbo, int callerUserId)
    {
        if (sbo.UserId != callerUserId) return false;
        using var context = new InventoryContext();
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
        using var context = new InventoryContext();
        var affected = context.Set<BrickOwned>()
            .Where(e => e.UserId == bo.UserId && e.PartNum == bo.PartNum && e.ColorId == bo.ColorId)
            .ExecuteUpdate(s => s.SetProperty(e => e.Stock, bo.Stock));
        return affected > 0;
    }

    public bool UpdateMinifig(Minifig minifig)
    {
        using var context = new InventoryContext();
        context.Set<Minifig>().Update(minifig);
        return context.SaveChanges() > 0;
    }

    public bool UpdateMinifigOwned(MinifigOwned mo, int callerUserId)
    {
        if (mo.UserId != callerUserId) return false;
        using var context = new InventoryContext();
        var affected = context.Set<MinifigOwned>()
            .Where(e => e.UserId == mo.UserId && e.MinifigId == mo.MinifigId)
            .ExecuteUpdate(s => s.SetProperty(e => e.Stock, mo.Stock));
        return affected > 0;
    }

    public bool UpdateSetMinifig(SetMinifig setMinifig)
    {
        using var context = new InventoryContext();
        context.Set<SetMinifig>().Update(setMinifig);
        return context.SaveChanges() > 0;
    }
}
