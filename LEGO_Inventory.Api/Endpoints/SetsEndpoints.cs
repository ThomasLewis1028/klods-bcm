using LEGO_Inventory.Database;
using Microsoft.EntityFrameworkCore;

namespace LEGO_Inventory.Api.Endpoints;

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
            var (resolved, candidates, notFound, hasMore) = await importer.ResolveSetId(req.Query);
            if (notFound) return Results.NotFound();
            if (resolved is not null) return Results.Ok(new ResolveSetResponse([resolved], true, false));
            return Results.Ok(new ResolveSetResponse(candidates, false, hasMore));
        });

        group.MapPost("/import", async (ImportSetRequest req, ImportData importer) =>
        {
            var ok = await importer.ImportSetInfo(req.SetId);
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
    }

    public record SetDto(string SetId, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName)
    {
        public static SetDto From(Set s) => new(s.SetId, s.Name, s.SetImg, s.NumBricks, s.ReleaseYear, s.ThemeName);
    }

    public record OwnedSetDto(string SetId, int SetIndex, string Name, string? SetImg, int NumBricks, int ReleaseYear, string? ThemeName);
    public record ResolveSetRequest(string Query);
    public record ResolveSetResponse(IEnumerable<SetCandidate> Results, bool Resolved, bool HasMore);
    public record ImportSetRequest(string SetId);
    public record AddOwnedSetRequest(string SetId, bool ApplyBricks);
}
