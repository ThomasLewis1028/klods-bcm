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

        // Lightweight stats for the Bricks page header (no row load).
        group.MapGet("/catalog-stats", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var totalBricks = await db.Set<Brick>().CountAsync();
            var ownedLoose  = await db.Set<BrickOwned>().SumAsync(bo => (long)bo.Stock);
            var ownedInSets = await db.Set<SetBrickOwned>().SumAsync(sbo => (long)sbo.Stock);
            return Results.Ok(new BrickCatalogStatsDto(totalBricks, ownedLoose + ownedInSets));
        });

        // Server-side, paginated catalog browse/search. Empty query => most-used bricks first.
        // Per-row stock/needed computed only for the current page; SetCount is the denormalized column.
        group.MapGet("/catalog", async (string? q, int page, int pageSize, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            if (pageSize is <= 0 or > 200) pageSize = 25;
            if (page < 0) page = 0;
            var query = (q ?? "").Trim();

            await using var db = dbFactory.CreateDbContext();

            IQueryable<Brick> baseQ = db.Set<Brick>().AsNoTracking();
            if (query.Length >= 2)
            {
                var like = $"%{query}%";
                baseQ = baseQ.Where(b => EF.Functions.ILike(b.PartNum, like) || EF.Functions.ILike(b.Name, like)
                                         || (b.ColorName != null && EF.Functions.ILike(b.ColorName, like)))
                             .OrderBy(b => b.Name);
            }
            else
            {
                baseQ = baseQ.OrderByDescending(b => b.SetCount).ThenBy(b => b.Name);
            }

            var total = await baseQ.CountAsync();
            var pageItems = await baseQ.Skip(page * pageSize).Take(pageSize).ToListAsync();

            var partNums = pageItems.Select(b => b.PartNum).Distinct().ToList();

            var brickStock = (await db.Set<BrickOwned>().AsNoTracking()
                    .Where(bo => partNums.Contains(bo.PartNum)).ToListAsync())
                .GroupBy(bo => (bo.PartNum, bo.ColorId)).ToDictionary(g => g.Key, g => g.Sum(x => x.Stock));
            var setBrickStock = (await db.Set<SetBrickOwned>().AsNoTracking()
                    .Where(sbo => partNums.Contains(sbo.PartNum)).ToListAsync())
                .GroupBy(sbo => (sbo.PartNum, sbo.ColorId)).ToDictionary(g => g.Key, g => g.Sum(x => x.Stock));

            var setBricks  = await db.Set<SetBrick>().AsNoTracking().Where(sb => partNums.Contains(sb.PartNum)).ToListAsync();
            var setCopies  = await InventoryAggregates.GetSetCopiesAsync(db);
            var neededDict = InventoryAggregates.GetBrickNeededDict(setBricks, setCopies);

            var items = pageItems.Select(b =>
            {
                var key = (b.PartNum, b.ColorId ?? "");
                return new BrickCatalogViewDto(
                    b.PartNum, b.Name, b.PartImg, b.ColorId, b.ColorName, b.HexColor, b.IsTrans, b.BricklinkId,
                    brickStock.GetValueOrDefault(key, 0) + setBrickStock.GetValueOrDefault(key, 0),
                    neededDict.GetValueOrDefault(key, 0),
                    b.SetCount);
            }).ToList();

            return Results.Ok(new BrickCatalogPage(items, total));
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
            return Results.Ok(setBricks);
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
    public record BrickCatalogStatsDto(int TotalBricks, long TotalOwnedStock);
    public record BrickCatalogPage(List<BrickCatalogViewDto> Items, int Total);
    public record ResolveBrickRequest(string PartNum);
    public record ResolveBrickResponse(string? PartName, IEnumerable<PartColorInfo> Colors);
    public record AddLooseBrickRequest(string PartNum, string PartName, string ColorId, string ColorName, string? PartImgUrl, int Quantity);
    public record UpdateStockRequest(int Stock);
}
