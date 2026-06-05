using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LEGO_Inventory.Database;

public class InventoryContextDesignTimeFactory : IDesignTimeDbContextFactory<InventoryContext>
{
    public InventoryContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryContext>()
            .UseNpgsql("Host=localhost;Database=design;Username=design;Password=design")
            .Options;

        return new InventoryContext(options);
    }
}
