using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory.Api.Endpoints;

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
    public record ResolveBrickRequest(string PartNum);
    public record ResolveBrickResponse(string? PartName, IEnumerable<PartColorInfo> Colors);
    public record AddLooseBrickRequest(string PartNum, string PartName, string ColorId, string ColorName, string? PartImgUrl, int Quantity);
    public record UpdateStockRequest(int Stock);
}
