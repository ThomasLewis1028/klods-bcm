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
            var totalUsed   = await db.Set<SetBrick>().SumAsync(sb => (long)sb.Count);
            return Results.Ok(new BrickCatalogStatsDto(totalBricks, totalUsed));
        });

        // Server-side, paginated catalog browse/search. Empty query => most-used bricks first.
        // Per-row used-count computed only for the current page; SetCount is the denormalized column.
        group.MapGet("/catalog", async (string? q, string? sort, string? dir, int page, int pageSize, IDbContextFactory<InventoryContext> dbFactory) =>
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
                                         || (b.ColorName != null && EF.Functions.ILike(b.ColorName, like)));
            }

            baseQ = SortBricks(baseQ, sort, dir);

            var total = await baseQ.CountAsync();
            var pageItems = await baseQ.Skip(page * pageSize).Take(pageSize).ToListAsync();

            var partNums = pageItems.Select(b => b.PartNum).Distinct().ToList();

            // Loose + in-set owned stock (community aggregate) — still surfaced to the Add Brick dialog.
            var brickStock = (await db.Set<BrickOwned>().AsNoTracking()
                    .Where(bo => partNums.Contains(bo.PartNum)).ToListAsync())
                .GroupBy(bo => (bo.PartNum, bo.ColorId)).ToDictionary(g => g.Key, g => g.Sum(x => x.Stock));
            var setBrickStock = (await db.Set<SetBrickOwned>().AsNoTracking()
                    .Where(sbo => partNums.Contains(sbo.PartNum)).ToListAsync())
                .GroupBy(sbo => (sbo.PartNum, sbo.ColorId)).ToDictionary(g => g.Key, g => g.Sum(x => x.Stock));

            var setBricks = await db.Set<SetBrick>().AsNoTracking().Where(sb => partNums.Contains(sb.PartNum)).ToListAsync();
            var usedDict  = InventoryAggregates.GetBrickUsedDict(setBricks);

            var items = pageItems.Select(b =>
            {
                var key = (b.PartNum, b.ColorId ?? "");
                return new BrickCatalogViewDto(
                    b.PartNum, b.Name, b.PartImg, b.ColorId, b.ColorName, b.HexColor, b.IsTrans, b.BricklinkId,
                    brickStock.GetValueOrDefault(key, 0) + setBrickStock.GetValueOrDefault(key, 0),
                    usedDict.GetValueOrDefault(key, 0),
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

        // Paginated + searchable sets containing a brick+color, with set name/image (row expander + detail dialog).
        group.MapGet("/{partNum}/{colorId}/sets/paged", async (
            string partNum, string colorId, string? q, int page, int pageSize, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            if (pageSize is <= 0 or > 50) pageSize = 10;
            if (page < 0) page = 0;
            var query = (q ?? "").Trim();

            await using var db = dbFactory.CreateDbContext();

            var joined = db.Set<SetBrick>().AsNoTracking()
                .Where(sb => sb.PartNum == partNum && sb.ColorId == colorId)
                .Join(db.Set<Set>().AsNoTracking(), sb => sb.SetId, s => s.SetId,
                    (sb, s) => new { s.SetId, s.Name, s.SetImg, sb.Count });

            if (query.Length >= 1)
            {
                var like = $"%{query}%";
                joined = joined.Where(x => EF.Functions.ILike(x.SetId, like) || EF.Functions.ILike(x.Name, like));
            }

            var total = await joined.CountAsync();
            var items = await joined.OrderBy(x => x.SetId)
                .Skip(page * pageSize).Take(pageSize)
                .Select(x => new SetForBrickDto(x.SetId, x.Name, x.SetImg, x.Count))
                .ToListAsync();

            return Results.Ok(new SetForBrickPage(items, total));
        });

        // Current user's loose stock for a single brick (for the detail dialog).
        group.MapGet("/{partNum}/{colorId}/owned", async (
            string partNum, string colorId, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var bo = await db.Set<BrickOwned>().AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == userId && b.PartNum == partNum && b.ColorId == colorId);
            return Results.Ok(new OwnedStockDto(bo?.Stock ?? 0, bo?.Location, bo?.Notes));
        });

        // Set the location + notes for the user's loose stock of a brick. Upserts (a needed-but-unowned
        // brick has no BrickOwned row yet, but the user may still want to note where they'll store it).
        group.MapPut("/owned/{partNum}/{colorId}/notes", async (
            string partNum, string colorId, NotesRequest req, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            if (!NotesRequest.IsValid(req)) return Results.BadRequest("Location must be under 100 characters; notes under 2000.");

            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var bo = await db.Set<BrickOwned>()
                .FirstOrDefaultAsync(b => b.UserId == userId && b.PartNum == partNum && b.ColorId == colorId);
            if (bo is null)
            {
                bo = new BrickOwned { UserId = userId, PartNum = partNum, ColorId = colorId, Stock = 0 };
                db.Set<BrickOwned>().Add(bo);
            }
            bo.Location = NotesRequest.Normalize(req.Location);
            bo.Notes = NotesRequest.Normalize(req.Notes);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        group.MapPost("/resolve", async (ResolveBrickRequest req, ImportData importer) =>
        {
            var (name, colors, notFound) = await importer.ResolvePartColors(req.PartNum);
            if (notFound) return Results.NotFound();
            return Results.Ok(new ResolveBrickResponse(name, colors));
        });

        group.MapPost("/owned", async (AddLooseBrickRequest req, HttpContext http, ImportData importer) =>
        {
            if (!req.IsValid) return Results.BadRequest($"Quantity must be between 0 and {UpdateStockRequest.MaxStock}.");

            var userId = http.UserId();
            var colorInfo = new PartColorInfo(req.ColorId, req.ColorName, req.PartImgUrl);
            await importer.AddLooseBrick(req.PartNum, req.PartName, colorInfo, req.Quantity, userId);
            return Results.Ok();
        });

        group.MapPatch("/owned/{partNum}/{colorId}", async (
            string partNum, string colorId, UpdateStockRequest req, HttpContext http,
            IDbContextFactory<InventoryContext> dbFactory, UpdateData updater) =>
        {
            if (!req.IsValid) return Results.BadRequest($"Stock must be between 0 and {UpdateStockRequest.MaxStock}.");

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

    // Whitelisted server-side sort. Default: most-used (set count) first. Used is a per-page aggregate, not sortable.
    private static IQueryable<Brick> SortBricks(IQueryable<Brick> q, string? sort, string? dir)
    {
        var desc = dir != "asc";
        return (sort ?? "sets") switch
        {
            "id"    => desc ? q.OrderByDescending(b => b.PartNum) : q.OrderBy(b => b.PartNum),
            "name"  => desc ? q.OrderByDescending(b => b.Name) : q.OrderBy(b => b.Name),
            "color" => desc ? q.OrderByDescending(b => b.ColorName) : q.OrderBy(b => b.ColorName),
            _       => desc ? q.OrderByDescending(b => b.SetCount).ThenBy(b => b.Name)
                            : q.OrderBy(b => b.SetCount).ThenBy(b => b.Name),
        };
    }

    public record BrickDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId)
    {
        public static BrickDto From(Brick b) => new(b.PartNum, b.Name, b.PartImg, b.ColorId, b.ColorName, b.HexColor, b.IsTrans, b.BricklinkId);
    }

    public record OwnedBrickDto(string PartNum, string ColorId, string Name, string? PartImg, string? ColorName, string? HexColor, int Stock);
    public record BrickCatalogViewDto(string PartNum, string Name, string? PartImg, string? ColorId, string? ColorName, string? HexColor, bool IsTrans, string? BricklinkId, int TotalStock, int TotalUsed, int SetCount);
    public record BrickCatalogStatsDto(int TotalBricks, long TotalUsed);
    public record BrickCatalogPage(List<BrickCatalogViewDto> Items, int Total);
    public record SetForBrickDto(string SetId, string Name, string? SetImg, int Count);
    public record SetForBrickPage(List<SetForBrickDto> Items, int Total);
    public record OwnedStockDto(int Stock, string? Location, string? Notes);

    public record NotesRequest(string? Location, string? Notes)
    {
        public static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // Matches the BrickOwned column caps (Location varchar(100), Notes varchar(2000)) so an
        // over-length value gets a clean 400 instead of a DB error.
        public static bool IsValid(NotesRequest req) =>
            (req.Location?.Length ?? 0) <= 100 && (req.Notes?.Length ?? 0) <= 2000;
    }
    public record ResolveBrickRequest(string PartNum);
    public record ResolveBrickResponse(string? PartName, IEnumerable<PartColorInfo> Colors);
    public record AddLooseBrickRequest(string PartNum, string PartName, string ColorId, string ColorName, string? PartImgUrl, int Quantity)
    {
        public bool IsValid => Quantity is >= 0 and <= UpdateStockRequest.MaxStock;
    }
    public record UpdateStockRequest(int Stock)
    {
        // Generous ceiling — no real collection gets anywhere near this — that just keeps a stray
        // huge value from a client out of stock sums (InventoryAggregates, catalog totals, etc.).
        public const int MaxStock = 1_000_000;
        public bool IsValid => Stock is >= 0 and <= MaxStock;
    }
}
