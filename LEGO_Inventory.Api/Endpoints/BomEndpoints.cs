using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory.Api.Endpoints;

public static class BomEndpoints
{
    public static void MapBom(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bom").RequireAuthorization();

        group.MapGet("/{setId}/{setIndex:int}", async (
            string setId, int setIndex, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var owned = await db.Set<SetOwned>()
                .AnyAsync(so => so.SetId == setId && so.SetIndex == setIndex && so.UserId == userId);
            if (!owned) return Results.NotFound();

            var setBricks = await db.Set<SetBrick>()
                .AsNoTracking()
                .Where(sb => sb.SetId == setId)
                .Join(db.Set<Brick>().AsNoTracking(), sb => new { sb.PartNum, sb.ColorId },
                    b => new { b.PartNum, ColorId = b.ColorId ?? "" },
                    (sb, b) => new { sb, b })
                .ToListAsync();

            var ownedStock = await db.Set<SetBrickOwned>()
                .AsNoTracking()
                .Where(sbo => sbo.SetId == setId && sbo.SetIndex == setIndex && sbo.UserId == userId)
                .ToDictionaryAsync(sbo => (sbo.PartNum, sbo.ColorId), sbo => sbo.Stock);

            var brickItems = setBricks.Select(x => new BomBrickDto(
                x.sb.PartNum, x.sb.ColorId, x.b.Name, x.b.PartImg, x.b.ColorName, x.b.HexColor,
                x.sb.Count, x.sb.SpareCount,
                ownedStock.GetValueOrDefault((x.sb.PartNum, x.sb.ColorId), 0)));

            var setMinifigs = await db.Set<SetMinifig>()
                .AsNoTracking()
                .Where(sm => sm.SetId == setId)
                .Join(db.Set<Minifig>().AsNoTracking(), sm => sm.MinifigId, m => m.MinifigId, (sm, m) => new { sm, m })
                .ToListAsync();

            var minifigItems = setMinifigs.Select(x => new BomMinifigDto(
                x.sm.MinifigId, x.m.MinifigName, x.m.MinifigImgUrl, x.sm.Count));

            return Results.Ok(new BomResponse(setId, setIndex, brickItems, minifigItems));
        });
    }

    public record BomBrickDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Count, int SpareCount, int Stock);
    public record BomMinifigDto(string MinifigId, string Name, string? ImgUrl, int Count);
    public record BomResponse(string SetId, int SetIndex, IEnumerable<BomBrickDto> Bricks, IEnumerable<BomMinifigDto> Minifigs);
}
