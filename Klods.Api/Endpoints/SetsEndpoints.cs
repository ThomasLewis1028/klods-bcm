using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class SetsEndpoints
{
    public static void MapSets(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sets").RequireAuthorization();

        group.MapGet("/", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var sets = await db.Set<Set>().AsNoTracking().OrderBy(s => s.Name).ToListAsync();
            return Results.Ok(sets.Select(SetDto.From));
        });

        group.MapGet("/owned", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var owned = await db.Set<SetOwned>()
                .AsNoTracking()
                .Where(so => so.UserId == userId)
                .Join(db.Set<Set>().AsNoTracking(), so => so.SetId, s => s.SetId, (so, s) => new OwnedSetDto(
                    so.SetId, so.SetIndex, s.Name, s.SetImg, s.NumBricks, s.ReleaseYear, s.ThemeName))
                .OrderBy(s => s.Name)
                .ToListAsync();

            return Results.Ok(owned);
        });

        group.MapPost("/resolve", async (ResolveSetRequest req, ImportData importer) =>
        {
            var page = req.Page < 1 ? 1 : req.Page;
            var (resolved, candidates, notFound, hasMore) = await importer.ResolveSetId(req.Query, page);
            if (notFound) return Results.NotFound();
            if (resolved is not null) return Results.Ok(new ResolveSetResponse([resolved], true, false));
            return Results.Ok(new ResolveSetResponse(candidates, false, hasMore));
        });

        group.MapPost("/import", async (ImportSetRequest req, ImportData importer) =>
        {
            var ok = await importer.ImportAll([req.SetId]);
            return ok ? Results.Ok() : Results.BadRequest("Import failed.");
        });

        group.MapPost("/owned", async (AddOwnedSetRequest req, HttpContext http, ImportData importer) =>
        {
            var userId = http.UserId();
            var ok = await importer.AddOwnedSet(req.SetId, userId, req.ApplyBricks);
            return ok ? Results.Ok() : Results.BadRequest("Could not add owned set.");
        });

        group.MapDelete("/owned/{setId}/{setIndex:int}", async (
            string setId, int setIndex, HttpContext http, DeleteData deleter) =>
        {
            var userId = http.UserId();
            var ok = deleter.DeleteOwnedSetInfo(userId, setId, setIndex, moveStock: false);
            return ok ? Results.Ok() : Results.NotFound();
        });

        // Delete the highest-index owned copy of a set — used by the catalog page decrement button.
        group.MapDelete("/owned/{setId}/last", async (
            string setId, HttpContext http, IDbContextFactory<InventoryContext> dbFactory, DeleteData deleter) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var toRemove = await db.Set<SetOwned>()
                .Where(so => so.UserId == userId && so.SetId == setId)
                .OrderByDescending(so => so.SetIndex)
                .FirstOrDefaultAsync();
            if (toRemove is null) return Results.NotFound();
            var ok = deleter.DeleteOwnedSetInfo(userId, setId, toRemove.SetIndex, moveStock: false);
            return ok ? Results.Ok() : Results.NotFound();
        });

        // Returns each set the user owns, grouped with all their copy instances + per-instance stats.
        // Supports optional search, page, and pageSize.
        group.MapGet("/my-owned", async (
            HttpContext http, IDbContextFactory<InventoryContext> dbFactory,
            string? search = null, int page = 0, int pageSize = 0) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var ownedList = await db.Set<SetOwned>().AsNoTracking()
                .Where(so => so.UserId == userId).ToListAsync();

            if (ownedList.Count == 0) return Results.Ok(new PagedResult<MyOwnedSetDto>([], false));

            var allSetIds = ownedList.Select(so => so.SetId).Distinct().ToList();

            var sets = await db.Set<Set>().AsNoTracking()
                .Where(s => allSetIds.Contains(s.SetId))
                .ToDictionaryAsync(s => s.SetId);

            var requiredPerSet = await db.Set<SetBrick>().AsNoTracking()
                .Where(sb => allSetIds.Contains(sb.SetId))
                .GroupBy(sb => sb.SetId)
                .Select(g => new { SetId = g.Key, Total = g.Sum(sb => sb.Count) })
                .ToDictionaryAsync(x => x.SetId, x => x.Total);

            var stockPerInstance = await db.Set<SetBrickOwned>().AsNoTracking()
                .Where(sbo => sbo.UserId == userId && allSetIds.Contains(sbo.SetId))
                .GroupBy(sbo => new { sbo.SetId, sbo.SetIndex })
                .Select(g => new { g.Key.SetId, g.Key.SetIndex, Stock = g.Sum(sbo => sbo.Stock) })
                .ToListAsync();
            var stockDict = stockPerInstance.ToDictionary(x => (x.SetId, x.SetIndex), x => x.Stock);

            var allResults = ownedList
                .GroupBy(so => so.SetId)
                .Where(g => sets.ContainsKey(g.Key))
                .Select(g =>
                {
                    var set = sets[g.Key];
                    var required = requiredPerSet.GetValueOrDefault(g.Key, 0);
                    var instances = g.OrderBy(so => so.SetIndex).Select(so =>
                    {
                        var stock = stockDict.GetValueOrDefault((so.SetId, so.SetIndex), 0);
                        return new OwnedInstanceDto(so.SetIndex, Math.Max(0, required - stock), stock);
                    }).ToList();
                    return new MyOwnedSetDto(set.SetId, set.Name, set.SetImg, set.NumBricks,
                        set.ReleaseYear, set.ThemeName, set.ManualUrl, instances);
                })
                .AsEnumerable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                allResults = allResults.Where(s =>
                    s.Name.ToLower().Contains(term) || s.SetId.ToLower().Contains(term));
            }

            var resultList = allResults.OrderBy(s => s.Name).ToList();
            bool hasMore = false;
            if (pageSize > 0)
            {
                hasMore = resultList.Count > page * pageSize + pageSize;
                resultList = resultList.Skip(page * pageSize).Take(pageSize).ToList();
            }

            return Results.Ok(new PagedResult<MyOwnedSetDto>(resultList, hasMore));
        });

        // Global catalog view: each set + current user's owned count + global totals.
        // Supports optional search, page, and pageSize.
        group.MapGet("/catalog-view", async (
            HttpContext http, IDbContextFactory<InventoryContext> dbFactory,
            string? search = null, int page = 0, int pageSize = 0) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            IQueryable<Set> setsQuery = db.Set<Set>().AsNoTracking().OrderBy(s => s.Name);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.ToLower();
                setsQuery = setsQuery.Where(s =>
                    s.Name.ToLower().Contains(term) || s.SetId.ToLower().Contains(term));
            }

            bool hasMore = false;
            List<Set> sets;
            if (pageSize > 0)
            {
                var raw = await setsQuery.Skip(page * pageSize).Take(pageSize + 1).ToListAsync();
                hasMore = raw.Count > pageSize;
                sets = raw.Count > pageSize ? raw.Take(pageSize).ToList() : raw;
            }
            else
            {
                sets = await setsQuery.ToListAsync();
            }

            var pageSetIds = sets.Select(s => s.SetId).ToList();

            var userOwned = await db.Set<SetOwned>().AsNoTracking()
                .Where(so => so.UserId == userId && pageSetIds.Contains(so.SetId))
                .GroupBy(so => so.SetId)
                .Select(g => new { SetId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.SetId, x => x.Count);

            var totalInstances = await db.Set<SetOwned>().CountAsync();
            var totalOwners    = await db.Set<SetOwned>().Select(so => so.UserId).Distinct().CountAsync();
            var totalPieces    = await db.Set<Set>().AsNoTracking().SumAsync(s => (long)s.NumBricks);

            var rows = sets.Select(s => new SetCatalogViewDto(
                s.SetId, s.Name, s.SetImg, s.NumBricks, s.ReleaseYear, s.ThemeName, s.ManualUrl,
                userOwned.GetValueOrDefault(s.SetId, 0))).ToList();

            return Results.Ok(new SetCatalogViewResponse(rows, totalInstances, totalOwners, (int)totalPieces, hasMore));
        });

        // Admin: remove a set from the catalog entirely.
        group.MapDelete("/{setId}", (string setId, DeleteData deleter) =>
        {
            var ok = deleter.DeleteSetInfo(setId);
            return ok ? Results.Ok() : Results.NotFound();
        }).RequireAuthorization("Admin");
    }

    public record SetDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName)
    {
        public static SetDto From(Set s) => new(s.SetId, s.Name, s.SetImg, s.NumBricks, s.ReleaseYear, s.ThemeName);
    }

    public record OwnedSetDto(string SetId, int SetIndex, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName);
    public record ResolveSetRequest(string Query, int Page = 0);
    public record ResolveSetResponse(IEnumerable<SetCandidate> Results, bool Resolved, bool HasMore);
    public record ImportSetRequest(string SetId);
    public record AddOwnedSetRequest(string SetId, bool ApplyBricks);

    public record OwnedInstanceDto(int SetIndex, int MissingPieceCount, int StockCount);
    public record MyOwnedSetDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl, List<OwnedInstanceDto> Instances);
    public record SetCatalogViewDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl, int UserOwnedCount);
    public record SetCatalogViewResponse(List<SetCatalogViewDto> Sets, int TotalOwnedInstances, int TotalOwners, int TotalPieces, bool HasMore = false);
}
