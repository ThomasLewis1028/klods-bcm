using System.Collections.Concurrent;
using System.Net;
using Klods.Api.Auth;
using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class HomeEndpoints
{
    // Image URLs we've confirmed resolve, so repeat picks skip the network check and the landing
    // page stays fast. Dead URLs are nulled in the DB instead of cached here, so they're never
    // picked again (by this endpoint or the catalog/list pages).
    private static readonly ConcurrentDictionary<string, byte> KnownGoodImages = new();

    // How many times a slot re-rolls past a dead image before giving up and falling back.
    private const int MaxPickAttempts = 5;

    public static void MapHome(this IEndpointRouteBuilder app)
    {
        // Anonymous: the landing page shows catalog images to everyone. When a valid bearer
        // token is present the "My" picks are drawn from the user's own collection instead.
        // Each pick is a single random row chosen in SQL (no table is loaded into memory).
        app.MapGet("/api/home/preview", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory,
            IHttpClientFactory httpFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();
            var client = httpFactory.CreateClient();

            var catalogSet     = await PickSet(db, client, null);
            var catalogBrick   = await PickBrick(db, client, null);
            var catalogMinifig = await PickMinifig(db, client, null);

            // Which items the current user owns — null when signed out (→ global fallback below).
            List<string>? ownedSetIds = null, ownedPartNums = null, ownedMinifigIds = null;
            if (http.User.Identity?.IsAuthenticated == true)
            {
                var userId = http.UserId();
                ownedSetIds = await db.Set<SetOwned>().AsNoTracking()
                    .Where(so => so.UserId == userId).Select(so => so.SetId).Distinct().ToListAsync();
                ownedPartNums = await db.Set<BrickOwned>().AsNoTracking()
                    .Where(bo => bo.UserId == userId && bo.Stock > 0).Select(bo => bo.PartNum).Distinct().ToListAsync();
                ownedMinifigIds = await db.Set<MinifigOwned>().AsNoTracking()
                    .Where(mo => mo.UserId == userId).Select(mo => mo.MinifigId).Distinct().ToListAsync();
            }

            // Fall back to the global pick when the user owns nothing (with a resolvable image).
            var mySet     = await PickSet(db, client, ownedSetIds)          ?? catalogSet;
            var myBrick   = await PickBrick(db, client, ownedPartNums)      ?? catalogBrick;
            var myMinifig = await PickMinifig(db, client, ownedMinifigIds)  ?? catalogMinifig;

            return Results.Ok(new HomePreviewDto(
                catalogSet, catalogBrick, catalogMinifig, mySet, myBrick, myMinifig));
        });
    }

    private static async Task<PreviewItemDto?> PickSet(InventoryContext db, HttpClient client, List<string>? ownedIds)
    {
        var excluded = new List<string>();
        for (var attempt = 0; attempt < MaxPickAttempts; attempt++)
        {
            var q = db.Set<Set>().AsNoTracking().Where(s => s.SetImg != null && !excluded.Contains(s.SetId));
            if (ownedIds is { Count: > 0 }) q = q.Where(s => ownedIds.Contains(s.SetId));
            var pick = await q.OrderBy(_ => EF.Functions.Random())
                .Select(s => new PreviewItemDto(s.SetId, s.Name, s.SetImg))
                .FirstOrDefaultAsync();
            if (pick is null) return null;
            if (await ImageResolves(client, pick.ImgUrl)) return pick;

            await db.Set<Set>().Where(s => s.SetId == pick.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.SetImg, (string?)null));
            excluded.Add(pick.Id);
        }
        return null;
    }

    private static async Task<PreviewItemDto?> PickBrick(InventoryContext db, HttpClient client, List<string>? ownedPartNums)
    {
        var excluded = new List<string>();
        for (var attempt = 0; attempt < MaxPickAttempts; attempt++)
        {
            var q = db.Set<Brick>().AsNoTracking().Where(b => b.PartImg != null && !excluded.Contains(b.PartNum));
            if (ownedPartNums is { Count: > 0 }) q = q.Where(b => ownedPartNums.Contains(b.PartNum));
            var pick = await q.OrderBy(_ => EF.Functions.Random())
                .Select(b => new PreviewItemDto(b.PartNum, b.Name, b.PartImg))
                .FirstOrDefaultAsync();
            if (pick is null) return null;
            if (await ImageResolves(client, pick.ImgUrl)) return pick;

            await db.Set<Brick>().Where(b => b.PartNum == pick.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.PartImg, (string?)null));
            excluded.Add(pick.Id);
        }
        return null;
    }

    private static async Task<PreviewItemDto?> PickMinifig(InventoryContext db, HttpClient client, List<string>? ownedIds)
    {
        var excluded = new List<string>();
        for (var attempt = 0; attempt < MaxPickAttempts; attempt++)
        {
            var q = db.Set<Minifig>().AsNoTracking().Where(m => m.ImgUrl != null && !excluded.Contains(m.MinifigId));
            if (ownedIds is { Count: > 0 }) q = q.Where(m => ownedIds.Contains(m.MinifigId));
            var pick = await q.OrderBy(_ => EF.Functions.Random())
                .Select(m => new PreviewItemDto(m.MinifigId, m.Name, m.ImgUrl))
                .FirstOrDefaultAsync();
            if (pick is null) return null;
            if (await ImageResolves(client, pick.ImgUrl)) return pick;

            await db.Set<Minifig>().Where(m => m.MinifigId == pick.Id)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.ImgUrl, (string?)null));
            excluded.Add(pick.Id);
        }
        return null;
    }

    // True if the image can be shown. Non-http values are local MinIO keys we assume are present;
    // for remote URLs a definitive 404/410 means "gone" (skip + let the caller null it). Any other
    // outcome — 5xx, method-not-allowed, a network hiccup — keeps the pick rather than risk
    // discarding a valid image over a transient failure.
    private static async Task<bool> ImageResolves(HttpClient client, string? url)
    {
        if (string.IsNullOrEmpty(url)) return false;
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return true;
        if (KnownGoodImages.ContainsKey(url)) return true;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var resp = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (resp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone) return false;
            KnownGoodImages.TryAdd(url, 0);
            return true;
        }
        catch
        {
            return true;
        }
    }

    public record PreviewItemDto(string Id, string Name, string? ImgUrl);
    public record HomePreviewDto(
        PreviewItemDto? CatalogSet, PreviewItemDto? CatalogBrick, PreviewItemDto? CatalogMinifig,
        PreviewItemDto? MySet, PreviewItemDto? MyBrick, PreviewItemDto? MyMinifig);
}
