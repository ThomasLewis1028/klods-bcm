using Klods.Database;
using Klods.Services;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class SetsEndpoints
{
    public static void MapSets(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sets").RequireAuthorization();

        group.MapGet("/", async (IDbContextFactory<InventoryContext> dbFactory, SettingsService settings) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var hidden = await CatalogSettings.GetHiddenThemeIdsAsync(settings);
            var sets = await ExcludeHidden(db.Set<Set>().AsNoTracking(), hidden)
                .OrderBy(s => s.Name)
                .Select(s => new SetDto(s.SetId, s.Name, s.SetImg, s.NumBricks, s.ReleaseYear,
                    db.Set<Theme>().Where(t => t.Id == s.ThemeId).Select(t => t.Name).FirstOrDefault()))
                .ToListAsync();
            return Results.Ok(sets);
        });

        group.MapGet("/owned", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var owned = await db.Set<SetOwned>()
                .AsNoTracking()
                .Where(so => so.UserId == userId)
                .Join(db.Set<Set>().AsNoTracking(), so => so.SetId, s => s.SetId, (so, s) => new OwnedSetDto(
                    so.SetId, so.SetIndex, s.Name, s.SetImg, s.NumBricks, s.ReleaseYear,
                    db.Set<Theme>().Where(t => t.Id == s.ThemeId).Select(t => t.Name).FirstOrDefault()))
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
            string setId, int setIndex, bool moveStock, HttpContext http, DeleteData deleter) =>
        {
            var userId = http.UserId();
            var ok = deleter.DeleteOwnedSetInfo(userId, setId, setIndex, moveStock);
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
        group.MapGet("/my-owned", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var ownedList = await db.Set<SetOwned>().AsNoTracking()
                .Where(so => so.UserId == userId).ToListAsync();

            if (ownedList.Count == 0) return Results.Ok(Array.Empty<MyOwnedSetDto>());

            var setIds = ownedList.Select(so => so.SetId).Distinct().ToList();

            var sets = await db.Set<Set>().AsNoTracking()
                .Where(s => setIds.Contains(s.SetId))
                .ToDictionaryAsync(s => s.SetId);

            var themeIds = sets.Values.Where(s => s.ThemeId != null).Select(s => s.ThemeId!.Value).Distinct().ToList();
            var themeNames = await db.Set<Theme>().AsNoTracking()
                .Where(t => themeIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name);

            // Per-part completeness for every owned copy (bricks + minifig parts, vs the loose pool).
            var completeness = await SetCompleteness.ComputeAsync(db, userId,
                ownedList.Select(so => (so.SetId, so.SetIndex)).ToList());

            var result = ownedList
                .GroupBy(so => so.SetId)
                .Where(g => sets.ContainsKey(g.Key))
                .Select(g =>
                {
                    var set = sets[g.Key];
                    var instances = g.OrderBy(so => so.SetIndex).Select(so =>
                    {
                        var comp = completeness.GetValueOrDefault((so.SetId, so.SetIndex))
                                   ?? new SetCompleteness.Result(0, SetCompleteness.Status.Short, 0, 0, 0);
                        return new OwnedInstanceDto(so.SetIndex, comp.Missing, comp.Have,
                            comp.Percent, comp.Status.ToString().ToLowerInvariant(), so.Location, so.Notes);
                    }).ToList();
                    var themeName = set.ThemeId is int tid ? themeNames.GetValueOrDefault(tid) : null;
                    return new MyOwnedSetDto(set.SetId, set.Name, set.SetImg, set.NumBricks,
                        set.ReleaseYear, themeName, set.ManualUrl, instances);
                })
                .ToList();

            return Results.Ok(result);
        });

        // Set the per-copy location + notes for one owned copy.
        group.MapPut("/owned/{setId}/{setIndex:int}/notes", async (
            string setId, int setIndex, NotesRequest req, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var so = await db.Set<SetOwned>()
                .FirstOrDefaultAsync(s => s.UserId == userId && s.SetId == setId && s.SetIndex == setIndex);
            if (so is null) return Results.NotFound();
            so.Location = NotesRequest.Normalize(req.Location);
            so.Notes = NotesRequest.Normalize(req.Notes);
            await db.SaveChangesAsync();
            return Results.Ok();
        });

        // Lightweight catalog stats (no row load) for the Sets page header.
        group.MapGet("/catalog-stats", async (IDbContextFactory<InventoryContext> dbFactory, SettingsService settings) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var hidden = await CatalogSettings.GetHiddenThemeIdsAsync(settings);
            var visibleSets = ExcludeHidden(db.Set<Set>().AsNoTracking(), hidden);
            var totalSets   = await visibleSets.CountAsync();
            var totalOwned  = await db.Set<SetOwned>().CountAsync();
            var totalOwners = await db.Set<SetOwned>().Select(so => so.UserId).Distinct().CountAsync();
            var totalPieces = await visibleSets.SumAsync(s => (long)s.NumBricks);
            return Results.Ok(new SetCatalogStatsDto(totalSets, totalOwned, totalOwners, totalPieces));
        });

        // Server-side, paginated catalog browse/search with optional theme filter + sort.
        group.MapGet("/catalog", async (
            string? q, int? theme, string? sort, string? dir, int page, int pageSize,
            HttpContext http, IDbContextFactory<InventoryContext> dbFactory, SettingsService settings) =>
        {
            var userId = http.UserId();
            if (pageSize is <= 0 or > 200) pageSize = 25;
            if (page < 0) page = 0;
            var query = (q ?? "").Trim();

            await using var db = dbFactory.CreateDbContext();

            var hidden = await CatalogSettings.GetHiddenThemeIdsAsync(settings);
            IQueryable<Set> baseQ = ExcludeHidden(db.Set<Set>().AsNoTracking(), hidden);
            if (query.Length >= 2)
            {
                var like = $"%{query}%";
                baseQ = baseQ.Where(s => EF.Functions.ILike(s.SetId, like) || EF.Functions.ILike(s.Name, like));
            }
            if (theme is int themeId)
                baseQ = baseQ.Where(s => s.ThemeId == themeId);

            baseQ = SortSets(baseQ, sort, dir, db);

            var total = await baseQ.CountAsync();
            var pageItems = await baseQ.Skip(page * pageSize).Take(pageSize).ToListAsync();

            var ids = pageItems.Select(s => s.SetId).ToList();
            var ownedCounts = (await db.Set<SetOwned>().AsNoTracking()
                    .Where(so => so.UserId == userId && ids.Contains(so.SetId)).ToListAsync())
                .GroupBy(so => so.SetId).ToDictionary(g => g.Key, g => g.Count());

            var pageThemeIds = pageItems.Where(s => s.ThemeId != null).Select(s => s.ThemeId!.Value).Distinct().ToList();
            var themeNames = await db.Set<Theme>().AsNoTracking()
                .Where(t => pageThemeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name);

            var items = pageItems.Select(s => new SetCatalogSearchDto(
                s.SetId, s.Name, s.SetImg, s.NumBricks, s.ReleaseYear,
                s.ThemeId is int tid ? themeNames.GetValueOrDefault(tid) : null, s.ManualUrl,
                ownedCounts.GetValueOrDefault(s.SetId, 0))).ToList();

            return Results.Ok(new SetCatalogPage(items, total));
        });

        // Distinct themes that have visible sets, for the filter dropdown (hidden themes omitted).
        group.MapGet("/themes", async (IDbContextFactory<InventoryContext> dbFactory, SettingsService settings) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var hidden = await CatalogSettings.GetHiddenThemeIdsAsync(settings);
            var themes = await db.Set<Theme>().AsNoTracking()
                .Where(t => !hidden.Contains(t.Id) && db.Set<Set>().Any(s => s.ThemeId == t.Id))
                .OrderBy(t => t.Name)
                .Select(t => new ThemeDto(t.Id, t.Name))
                .ToListAsync();
            return Results.Ok(themes);
        });

        // Admin: remove a set from the catalog entirely.
        group.MapDelete("/{setId}", (string setId, DeleteData deleter) =>
        {
            var ok = deleter.DeleteSetInfo(setId);
            return ok ? Results.Ok() : Results.NotFound();
        }).RequireAuthorization("Admin");
    }

    // Drops sets in admin-hidden themes. Sets with no theme are always shown.
    private static IQueryable<Set> ExcludeHidden(IQueryable<Set> q, int[] hidden) =>
        hidden.Length == 0 ? q : q.Where(s => s.ThemeId == null || !hidden.Contains(s.ThemeId.Value));

    // Whitelisted server-side sort. Default: latest sets first. Theme sort orders by the joined name.
    private static IQueryable<Set> SortSets(IQueryable<Set> q, string? sort, string? dir, InventoryContext db)
    {
        var desc = dir != "asc";
        return (sort ?? "year") switch
        {
            "name"   => desc ? q.OrderByDescending(s => s.Name) : q.OrderBy(s => s.Name),
            "id"     => desc ? q.OrderByDescending(s => s.SetId) : q.OrderBy(s => s.SetId),
            "pieces" => desc ? q.OrderByDescending(s => s.NumBricks) : q.OrderBy(s => s.NumBricks),
            "theme"  => desc ? q.OrderByDescending(s => db.Set<Theme>().Where(t => t.Id == s.ThemeId).Select(t => t.Name).FirstOrDefault())
                             : q.OrderBy(s => db.Set<Theme>().Where(t => t.Id == s.ThemeId).Select(t => t.Name).FirstOrDefault()),
            _        => desc ? q.OrderByDescending(s => s.ReleaseYear).ThenBy(s => s.Name)
                             : q.OrderBy(s => s.ReleaseYear).ThenBy(s => s.Name),
        };
    }

    public record SetDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName);

    public record OwnedSetDto(string SetId, int SetIndex, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName);
    public record ResolveSetRequest(string Query, int Page = 0);
    public record ResolveSetResponse(IEnumerable<SetCandidate> Results, bool Resolved, bool HasMore);
    public record ImportSetRequest(string SetId);
    public record AddOwnedSetRequest(string SetId, bool ApplyBricks);

    public record OwnedInstanceDto(int SetIndex, int MissingPieceCount, int StockCount, int Percent, string Status, string? Location, string? Notes);

    public record NotesRequest(string? Location, string? Notes)
    {
        public static string? Normalize(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
    public record MyOwnedSetDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl, List<OwnedInstanceDto> Instances);
    public record SetCatalogStatsDto(int TotalSets, int TotalOwnedInstances, int TotalOwners, long TotalPieces);
    public record SetCatalogSearchDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName, string ManualUrl, int UserOwnedCount);
    public record SetCatalogPage(List<SetCatalogSearchDto> Items, int Total);
    public record ThemeDto(int Id, string Name);
}
