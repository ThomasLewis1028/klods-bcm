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

        group.MapPost("/import", async (ImportMinifigRequest req, ImportData importer) =>
        {
            var (resolved, candidates, notFound, hasMore) = await importer.ResolveMinifigId(req.Query);
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

    public record OwnedMinifigDto(string MinifigId, string MinifigName, string? ImgUrl, int Stock);
    public record ImportMinifigRequest(string Query);
    public record ResolveMinifigResponse(IEnumerable<MinifigCandidate> Results, bool Resolved, bool HasMore);
    public record AddOwnedMinifigRequest(string MinifigId, int Count);
    public record UpdateStockRequest(int Stock);
}
