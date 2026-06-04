using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory.Api.Endpoints;

public static class MinifigsEndpoints
{
    public static void MapMinifigs(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/minifigs").RequireAuthorization();

        group.MapGet("/", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var minifigs = await db.Set<Minifig>().AsNoTracking().OrderBy(m => m.MinifigName).ToListAsync();
            return Results.Ok(minifigs.Select(MinifigDto.From));
        });

        // Catalog view: all minifigs with part count.
        group.MapGet("/catalog-view", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();

            var partCounts = await db.Set<MinifigBrick>().AsNoTracking()
                .GroupBy(mb => mb.MinifigID)
                .Select(g => new { MinifigId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.MinifigId, x => x.Count);

            var minifigs = await db.Set<Minifig>().AsNoTracking().OrderBy(m => m.MinifigName).ToListAsync();

            return Results.Ok(minifigs.Select(m => new MinifigCatalogViewDto(
                m.MinifigId, m.MinifigName, m.MinifigImgUrl, m.MinifigUrl,
                partCounts.GetValueOrDefault(m.MinifigId, 0))));
        });

        group.MapGet("/owned", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var owned = await db.Set<MinifigOwned>()
                .AsNoTracking()
                .Where(mo => mo.UserId == userId)
                .Join(db.Set<Minifig>().AsNoTracking(), mo => mo.MinifigId, m => m.MinifigId, (mo, m) =>
                    new OwnedMinifigDto(mo.MinifigId, m.MinifigName, m.MinifigImgUrl, mo.Stock))
                .OrderBy(m => m.MinifigName)
                .ToListAsync();

            return Results.Ok(owned);
        });

        // Lazy-load: bricks belonging to a minifig.
        group.MapGet("/{minifigId}/bricks", async (string minifigId, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();

            var rows = await db.Set<MinifigBrick>().AsNoTracking()
                .Where(mb => mb.MinifigID == minifigId)
                .Join(db.Set<Brick>().AsNoTracking(),
                    mb => new { PartNum = mb.BrickID, mb.ColorId },
                    b  => new { b.PartNum, b.ColorId },
                    (mb, b) => new MinifigBrickDto(mb.BrickID, mb.ColorId, b.Name, b.PartImg, b.ColorName, b.HexColor, mb.Quantity))
                .ToListAsync();

            return Results.Ok(rows);
        });

        group.MapPost("/import", async (ImportMinifigRequest req, ImportData importer) =>
        {
            var page = req.Page < 1 ? 1 : req.Page;
            var (resolved, candidates, notFound, hasMore) = await importer.ResolveMinifigId(req.Query, page);
            if (notFound) return Results.NotFound();
            if (resolved is not null) return Results.Ok(new ResolveMinifigResponse([resolved], true, false));
            return Results.Ok(new ResolveMinifigResponse(candidates, false, hasMore));
        });

        group.MapPost("/owned", async (AddOwnedMinifigRequest req, HttpContext http, ImportData importer) =>
        {
            var userId = http.UserId();
            var ok = await importer.AddOwnedMinifig(req.MinifigId, userId, req.Count);
            return ok ? Results.Ok() : Results.BadRequest("Could not add owned minifig.");
        });

        group.MapPatch("/owned/{minifigId}", async (
            string minifigId, UpdateStockRequest req, HttpContext http,
            IDbContextFactory<InventoryContext> dbFactory, UpdateData updater) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var mo = await db.Set<MinifigOwned>()
                .FirstOrDefaultAsync(m => m.MinifigId == minifigId && m.UserId == userId);
            if (mo is null) return Results.NotFound();
            mo.Stock = req.Stock;
            updater.UpdateMinifigOwned(mo, userId);
            return Results.Ok();
        });
    }

    public record MinifigDto(string MinifigId, string MinifigName, string? ImgUrl, string MinifigUrl)
    {
        public static MinifigDto From(Minifig m) => new(m.MinifigId, m.MinifigName, m.MinifigImgUrl, m.MinifigUrl);
    }

    public record MinifigCatalogViewDto(string MinifigId, string MinifigName, string? ImgUrl, string MinifigUrl, int PartCount);
    public record MinifigBrickDto(string BrickId, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Quantity);
    public record OwnedMinifigDto(string MinifigId, string MinifigName, string? ImgUrl, int Stock);
    public record ImportMinifigRequest(string Query, int Page = 0);
    public record ResolveMinifigResponse(IEnumerable<MinifigCandidate> Results, bool Resolved, bool HasMore);
    public record AddOwnedMinifigRequest(string MinifigId, int Count);
    public record UpdateStockRequest(int Stock);
}
