using System.Text.Json.Nodes;
using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory;

public class DeleteData
{
    public bool DeleteSetInfo(string? setId)
    {
        using (var context = new InventoryContext())
        {
            var setContext = context.Set<Set>();
            
            setContext
                .Where(s => s.SetId == setId)
                .ExecuteDelete();
            
            context.SaveChanges();
            
            return !setContext.Any(s => s.SetId == setId);
        }
    }
    
    public bool DeleteSetParts(string? setId)
    {
        using (var context = new InventoryContext())
        {
            var setContext = context.Set<Set>();

            if (setContext.Any(s => s.SetId == setId))
            {
                var set = setContext.First(s => s.SetId == setId);
                
                var setBrickContext = context.Set<SetBrick>();

                setBrickContext
                    .Where(sb => sb.SetId == setId)
                    .ExecuteDelete();

                context.SaveChanges();

                return !setBrickContext.Any(sb => sb.SetId == setId);
            }
        }
        
        return false;
    }

    public bool DeleteBricks(string? brickId, string? colorId)
    {
        using (var context = new InventoryContext())
        {
            var brickContext = context.Set<Brick>();
            var setBrickContext = context.Set<SetBrick>();

            if (setBrickContext.Any(sb => sb.PartNum == brickId && sb.ColorId == colorId))
                return false;
            
            brickContext
                .Where(b => b.ColorId == colorId && b.PartNum == brickId)
                .ExecuteDelete();
            
            context.SaveChanges();
            
            return !brickContext.Any(b => b.ColorId == colorId && b.PartNum == brickId);
        }
    }
}