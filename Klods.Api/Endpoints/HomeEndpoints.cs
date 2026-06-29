using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class HomeEndpoints
{
    public static void MapHome(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/home/preview", async (IDbContextFactory<InventoryContext> dbFactory) =>
        {
            await using var db = dbFactory.CreateDbContext();

            var sets = await db.Set<Set>().AsNoTracking().Where(s => s.SetImg != null).ToListAsync();
            var bricks = await db.Set<Brick>().AsNoTracking().Where(b => b.PartImg != null).ToListAsync();
            var minifigs = await db.Set<Minifig>().AsNoTracking().Where(m => m.ImgUrl != null).ToListAsync();

            return Results.Ok(new HomePreviewDto(
                PickDistinct(sets, 3).Select(s => new PreviewItemDto(s.SetId, s.Name, s.SetImg)).ToList(),
                PickDistinct(bricks, 2).Select(b => new PreviewItemDto(b.PartNum, b.Name, b.PartImg)).ToList(),
                PickDistinct(minifigs, 2).Select(m => new PreviewItemDto(m.MinifigId, m.Name, m.ImgUrl)).ToList()
            ));
        }).RequireAuthorization();
    }

    private static List<T> PickDistinct<T>(List<T> source, int count)
    {
        count = Math.Min(count, source.Count);
        var indices = new HashSet<int>();
        var result = new List<T>(count);
        while (result.Count < count)
        {
            var i = Random.Shared.Next(source.Count);
            if (indices.Add(i)) result.Add(source[i]);
        }
        return result;
    }

    public record PreviewItemDto(string Id, string Name, string? ImgUrl);
    public record HomePreviewDto(List<PreviewItemDto> Sets, List<PreviewItemDto> Bricks, List<PreviewItemDto> Minifigs);
}
