using System.Text.Json.Nodes;
using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory;

public class UpdateData
{
    public bool UpdateSet(Set set)
    {
        using (var context = new InventoryContext())
        {
            var setContext = context.Set<Set>();
            
            setContext.Update(set);
            
            return  context.SaveChanges() > 0;
        }
    }
    
    public bool UpdateBrick(Brick brick)
    {
        using (var context = new InventoryContext())
        {
            var brickContext = context.Set<Brick>();
            
            brickContext.Update(brick);
            
            return  context.SaveChanges() > 0;
        }
    }
    
    
    
    public bool UpdateMinifig(Minifig minifig)
    {
        using (var context = new InventoryContext())
        {
            var minifigContext = context.Set<Minifig>();
            
            minifigContext.Update(minifig);
            
            return  context.SaveChanges() > 0;
        }
    }
}