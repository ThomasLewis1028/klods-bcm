using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class BricksEndpoints
{
    public static void MapBricks(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bricks").RequireAuthorization();

        group.MapGet("/", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var bricks = await db.Set<Brick>().AsNoTracking().OrderBy(b => b.Name).ToListAsync();
            return Results.Ok(bricks.Select(BrickDto.From));
        });

        // Global catalog view: all bricks with total stock (all users) + needed + set count.
        group.MapGet("/catalog-view", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();

            var allBrickOwned = await db.Set<BrickOwned>().AsNoTracking().ToListAsync();
            var brickStock = allBrickOwned
                .GroupBy(bo => (bo.PartNum, bo.ColorId))
                .ToDictionary(g => g.Key, g => g.Sum(bo => bo.Stock));

            var allSetBrickOwned = await db.Set<SetBrickOwned>().AsNoTracking().ToListAsync();
            var setBrickStock = allSetBrickOwned
                .GroupBy(sbo => (sbo.PartNum, sbo.ColorId))
                .ToDictionary(g => g.Key, g => g.Sum(sbo => sbo.Stock));

            var setCopies    = await InventoryAggregates.GetSetCopiesAsync(db);
            var allSetBricks = await db.Set<SetBrick>().AsNoTracking().ToListAsync();
            var neededDict   = InventoryAggregates.GetBrickNeededDict(allSetBricks, setCopies);
            var setCountDict = InventoryAggregates.GetBrickSetCountDict(allSetBricks);

            var bricks = await db.Set<Brick>().AsNoTracking().OrderBy(b => b.Name).ToListAsync();
            var rows = bricks.Select(b =>
            {
                var key = (b.PartNum, b.ColorId ?? "");
                return new BrickCatalogViewDto(
                    b.PartNum, b.Name, b.PartImg, b.ColorId, b.ColorName, b.HexColor, b.IsTrans, b.BricklinkId,
                    brickStock.GetValueOrDefault(key, 0) + setBrickStock.GetValueOrDefault(key, 0),
                    neededDict.GetValueOrDefault(key, 0),
                    setCountDict.GetValueOrDefault(key, 0));
            }).ToList();

            return Results.Ok(rows);
        });

        group.MapGet("/owned", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var owned = await db.Set<BrickOwned>()
                .AsNoTracking()
                .Where(bo => bo.UserId == userId)
                .Join(db.Set<Brick>().AsNoTracking(), bo => new { bo.PartNum, bo.ColorId },
                    b => new { b.PartNum, ColorId = b.ColorId ?? "" },
                    (bo, b) => new OwnedBrickDto(bo.PartNum, bo.ColorId, b.Name, b.PartImg, b.ColorName, b.HexColor, bo.Stock))
                .OrderBy(b => b.Name)
                .ToListAsync();

            return Results.Ok(owned);
        });

        // Lazy-load: which sets contain a given brick+color combination.
        group.MapGet("/{partNum}/{colorId}/sets", async (
            string partNum, string colorId, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var setBricks = await db.Set<SetBrick>().AsNoTracking()
                .Where(sb => sb.PartNum == partNum && sb.ColorId == colorId)
                .ToListAsync();
            var setIds = setBricks.Select(sb => sb.SetId).ToList();
            var sets = await db.Set<Set>().AsNoTracking()
                .Where(s => setIds.Contains(s.SetId))
                .ToDictionaryAsync(s => s.SetId);
            var result = setBricks
                .Where(sb => sets.ContainsKey(sb.SetId))
                .Select(sb => new BrickSetDto(sb.SetId, sets[sb.SetId].Name, sets[sb.SetId].SetImg, sb.Count, sb.SpareCount))
                .OrderBy(dto => dto.SetName)
                .ToList();
            return Results.Ok(result);
        });

        group.MapPost("/resolve", async (ResolveBrickRequest req, ImportData importer) =>
        {
            var (name, colors, notFound) = await importer.ResolvePartColors(req.PartNum);
            if (notFound) return Results.NotFound();
            return Results.Ok(new ResolveBrickResponse(name, colors));
        });

        group.MapPost("/owned", async (AddLooseBrickRequest req, HttpContext http, ImportData importer) =>
        {
            var userId = http.UserId();
            var colorInfo = new PartColorInfo(req.ColorId, req.ColorName, req.PartImgUrl);
            await importer.AddLooseBrick(req.PartNum, req.PartName, colorInfo, req.Quantity, userId);
            return Results.Ok();
        });

        group.MapPatch("/owned/{partNum}/{colorId}", async (
            string partNum, string colorId, UpdateStockRequest req, HttpContext http,
            IDbContextFactory<InventoryContext> dbFactory, UpdateData updater) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var bo = await db.Set<BrickOwned>()
                .FirstOrDefaultAsync(b => b.PartNum == partNum && b.ColorId == colorId && b.UserId == userId);
            if (bo is null) return Results.NotFound();
            bo.Stock = req.Stock;
            updater.UpdateBrickOwned(bo, userId);
            return Results.Ok();
        });
    }

    public record BrickDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId)
    {
        public static BrickDto From(Brick b) => new(b.PartNum, b.Name, b.PartImg, b.ColorId, b.ColorName, b.HexColor, b.IsTrans, b.BricklinkId);
    }

    public record OwnedBrickDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Stock);
    public record BrickCatalogViewDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId, int TotalStock, int TotalNeeded, int SetCount);
    public record ResolveBrickRequest(string PartNum);
    public record ResolveBrickResponse(string? PartName, IEnumerable<PartColorInfo> Colors);
    public record AddLooseBrickRequest(string PartNum, string PartName, string ColorId, string ColorName, string? PartImgUrl, int Quantity);
    public record UpdateStockRequest(int Stock);
    public record BrickSetDto(string SetId, string SetName, string? SetImg, int Count, int SpareCount);
}
