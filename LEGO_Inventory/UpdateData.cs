using LEGO_Inventory.Database;

namespace LEGO_Inventory;

public class UpdateData
{
    public bool UpdateSet(Set set)
    {
        using var context = new InventoryContext();
        context.Set<Set>().Update(set);
        return context.SaveChanges() > 0;
    }

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

    public bool UpdateSetBrickOwned(SetBrickOwned sbo)
    {
        using var context = new InventoryContext();
        context.Set<SetBrickOwned>().Update(sbo);
        return context.SaveChanges() > 0;
    }

    public bool UpdateBrickOwned(BrickOwned bo)
    {
        using var context = new InventoryContext();
        context.Set<BrickOwned>().Update(bo);
        return context.SaveChanges() > 0;
    }

    public bool UpdateMinifig(Minifig minifig)
    {
        using var context = new InventoryContext();
        context.Set<Minifig>().Update(minifig);
        return context.SaveChanges() > 0;
    }

    public bool UpdateMinifigOwned(MinifigOwned mo)
    {
        using var context = new InventoryContext();
        context.Set<MinifigOwned>().Update(mo);
        return context.SaveChanges() > 0;
    }

    public bool UpdateSetMinifig(SetMinifig setMinifig)
    {
        using var context = new InventoryContext();
        context.Set<SetMinifig>().Update(setMinifig);
        return context.SaveChanges() > 0;
    }
}
