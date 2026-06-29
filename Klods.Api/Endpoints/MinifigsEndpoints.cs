using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class MinifigsEndpoints
{
    public static void MapMinifigs(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/minifigs").RequireAuthorization();

        // Server-side catalog search (the full ~17k-fig catalog is too large to ship to the client).
        // Only returns figs that have an image, since this powers the profile-picture picker.
        group.MapGet("/catalog-search", async (string q, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var query = (q ?? "").Trim();
            if (query.Length < 2) return Results.Ok(Array.Empty<MinifigSearchDto>());

            await using var db = dbFactory.CreateDbContext();
            var like = $"%{query}%";

            var matches = await db.Set<Minifig>().AsNoTracking()
                .Where(m => m.ImgUrl != null && m.ImgUrl != ""
                            && (EF.Functions.ILike(m.MinifigId, like) || EF.Functions.ILike(m.Name, like)))
                .OrderBy(m => m.Name)
                .Take(100)
                .Select(m => new MinifigSearchDto(m.MinifigId, m.Name, m.ImgUrl))
                .ToListAsync();

            return Results.Ok(matches);
        });

        // Current user's loose-owned count for a single fig (for the detail dialog).
        group.MapGet("/{minifigId}/loose-count", async (
            string minifigId, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var count = await db.Set<MinifigOwned>().AsNoTracking()
                .CountAsync(mo => mo.UserId == userId && mo.MinifigId == minifigId && mo.SetId == null);
            return Results.Ok(new LooseCountDto(count));
        });

        // Lightweight stats for the Minifigs page header.
        group.MapGet("/catalog-stats", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var total = await db.Set<Minifig>().CountAsync();
            var totalParts = await db.Set<Minifig>().SumAsync(m => (long)m.NumParts);
            return Results.Ok(new MinifigCatalogStatsDto(total, totalParts));
        });

        // Server-side, paginated catalog browse/search. Empty query => figs with the most parts first.
        group.MapGet("/catalog", async (string? q, string? sort, string? dir, int page, int pageSize, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            if (pageSize is <= 0 or > 200) pageSize = 25;
            if (page < 0) page = 0;
            var query = (q ?? "").Trim();

            await using var db = dbFactory.CreateDbContext();

            IQueryable<Minifig> baseQ = db.Set<Minifig>().AsNoTracking();
            if (query.Length >= 2)
            {
                var like = $"%{query}%";
                baseQ = baseQ.Where(m => EF.Functions.ILike(m.MinifigId, like) || EF.Functions.ILike(m.Name, like));
            }

            baseQ = SortMinifigs(baseQ, sort, dir);

            var total = await baseQ.CountAsync();
            var pageItems = await baseQ.Skip(page * pageSize).Take(pageSize).ToListAsync();

            var ids = pageItems.Select(m => m.MinifigId).ToList();
            var partCounts = (await db.Set<MinifigBrick>().AsNoTracking()
                    .Where(mb => ids.Contains(mb.MinifigId)).ToListAsync())
                .GroupBy(mb => mb.MinifigId).ToDictionary(g => g.Key, g => g.Count());

            var items = pageItems.Select(m => new MinifigCatalogViewDto(
                m.MinifigId, m.Name, m.ImgUrl, m.Url ?? "", partCounts.GetValueOrDefault(m.MinifigId, 0))).ToList();

            return Results.Ok(new MinifigCatalogPage(items, total));
        });

        // Owned (loose + on-set instances), aggregated to a per-fig count.
        group.MapGet("/owned", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var counts = (await db.Set<MinifigOwned>().AsNoTracking()
                    .Where(mo => mo.UserId == userId).ToListAsync())
                .GroupBy(mo => mo.MinifigId)
                .ToDictionary(g => g.Key, g => g.Count());

            var figs = await db.Set<Minifig>().AsNoTracking()
                .Where(m => counts.Keys.Contains(m.MinifigId)).ToListAsync();

            var owned = figs
                .Select(m => new OwnedMinifigDto(m.MinifigId, m.Name, m.ImgUrl, counts[m.MinifigId]))
                .OrderBy(o => o.MinifigName)
                .ToList();

            return Results.Ok(owned);
        });

        // Lazy-load: bricks belonging to a minifig.
        group.MapGet("/{minifigId}/bricks", async (string minifigId, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();

            var rows = await db.Set<MinifigBrick>().AsNoTracking()
                .Where(mb => mb.MinifigId == minifigId)
                .Join(db.Set<Brick>().AsNoTracking(),
                    mb => new { mb.PartNum, mb.ColorId },
                    b  => new { b.PartNum, b.ColorId },
                    (mb, b) => new MinifigBrickDto(mb.PartNum, mb.ColorId, b.Name, b.PartImg, b.ColorName, b.HexColor, mb.Count))
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

        // Set the user's loose count for a fig (adds/removes loose instances).
        group.MapPatch("/owned/{minifigId}", async (
            string minifigId, UpdateStockRequest req, HttpContext http, ImportData importer) =>
        {
            var userId = http.UserId();
            await importer.SetLooseMinifigCount(userId, minifigId, req.Stock);
            return Results.Ok();
        });
    }


    // Whitelisted server-side sort. Default: most parts first (sorts by NumParts; the Parts column shows distinct part count).
    private static IQueryable<Minifig> SortMinifigs(IQueryable<Minifig> q, string? sort, string? dir)
    {
        var desc = dir != "asc";
        return (sort ?? "parts") switch
        {
            "id"   => desc ? q.OrderByDescending(m => m.MinifigId) : q.OrderBy(m => m.MinifigId),
            "name" => desc ? q.OrderByDescending(m => m.Name) : q.OrderBy(m => m.Name),
            _      => desc ? q.OrderByDescending(m => m.NumParts).ThenBy(m => m.Name)
                           : q.OrderBy(m => m.NumParts).ThenBy(m => m.Name),
        };
    }

    public record MinifigCatalogViewDto(string MinifigId, string MinifigName, string? ImgUrl, string MinifigUrl, int PartCount);
    public record MinifigSearchDto(string MinifigId, string Name, string? ImgUrl);
    public record MinifigCatalogStatsDto(int TotalMinifigs, long TotalParts);
    public record MinifigCatalogPage(List<MinifigCatalogViewDto> Items, int Total);
    public record LooseCountDto(int Count);
    public record MinifigBrickDto(string BrickId, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Quantity);
    public record OwnedMinifigDto(string MinifigId, string MinifigName, string? ImgUrl, int Stock);
    public record ImportMinifigRequest(string Query, int Page = 0);
    public record ResolveMinifigResponse(IEnumerable<MinifigCandidate> Results, bool Resolved, bool HasMore);
    public record AddOwnedMinifigRequest(string MinifigId, int Count);
    public record UpdateStockRequest(int Stock);
}
