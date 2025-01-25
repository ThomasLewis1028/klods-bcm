using System.Text.Json.Nodes;
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
        
        using (var context = new InventoryContext())
        {
            var setContext = context.Set<Set>();
            var setBrickContext = context.Set<SetBrick>();
            var brickContext = context.Set<Brick>();

            if(moveStock)
            {
                _logger.LogInformation($"Moving stock back to inventory {setId}");
                
                foreach (var setBrick in setBrickContext.Where(sb => sb.SetId == setId))
                {
                    brickContext.First(b => b.PartNum == setBrick.PartNum 
                                            && b.ColorId == setBrick.ColorId)
                        .Count += setBrick.Stock;
                }
                
                _logger.LogInformation($"Stock for set {setId} has been moved to inventory");
            }
            
            setContext
                .Where(s => s.SetId == setId)
                .ExecuteDelete();
            
            context.SaveChanges();
            
            
            _logger.LogInformation($"{setId} has been deleted");
            
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