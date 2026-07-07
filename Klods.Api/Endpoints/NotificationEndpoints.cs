using Klods.Api.Auth;
using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods.Api.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotifications(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").RequireAuthorization();

        group.MapGet("/", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();

            var notifs = await db.Set<SetUpdateNotification>().AsNoTracking()
                .Where(n => n.UserId == userId)
                .Include(n => n.Items)
                .OrderByDescending(n => n.DetectedAt)
                .ToListAsync();

            if (notifs.Count == 0)
                return Results.Ok(new List<NotificationDto>());

            var setIds = notifs.Select(n => n.SetId).Distinct().ToList();
            var sets = await db.Set<Set>().AsNoTracking()
                .Where(s => setIds.Contains(s.SetId))
                .Select(s => new { s.SetId, s.Name, s.SetImg })
                .ToDictionaryAsync(s => s.SetId);

            var partNums = notifs.SelectMany(n => n.Items).Select(i => i.PartNum).Distinct().ToList();
            var bricks = (await db.Set<Brick>().AsNoTracking()
                    .Where(b => partNums.Contains(b.PartNum))
                    .Select(b => new { b.PartNum, b.ColorId, b.Name, b.PartImg, b.ColorName, b.HexColor })
                    .ToListAsync())
                .ToDictionary(b => (b.PartNum, b.ColorId));

            var dtos = notifs.Select(n =>
            {
                sets.TryGetValue(n.SetId, out var s);
                var items = n.Items
                    .OrderBy(i => i.ChangeKind).ThenBy(i => i.PartNum)
                    .Select(i =>
                    {
                        bricks.TryGetValue((i.PartNum, i.ColorId), out var b);
                        return new NotificationItemDto(
                            i.PartNum, b?.Name, b?.PartImg, b?.ColorName, b?.HexColor,
                            i.ChangeKind, i.OldCount, i.NewCount);
                    })
                    .ToList();
                return new NotificationDto(n.Id, n.SetId, s?.Name ?? n.SetId, s?.SetImg, n.DetectedAt, n.ReadAt != null, items);
            }).ToList();

            return Results.Ok(dtos);
        });

        group.MapGet("/unread-count", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var count = await db.Set<SetUpdateNotification>().CountAsync(n => n.UserId == userId && n.ReadAt == null);
            return Results.Ok(count);
        });

        group.MapPost("/read-all", async (HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var now = DateTime.UtcNow;
            await db.Set<SetUpdateNotification>()
                .Where(n => n.UserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
            return Results.Ok();
        });

        group.MapPost("/{id:int}/read", async (int id, HttpContext http, IDbContextFactory<InventoryContext> dbFactory) =>
        {
            var userId = http.UserId();
            await using var db = dbFactory.CreateDbContext();
            var now = DateTime.UtcNow;
            var affected = await db.Set<SetUpdateNotification>()
                .Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
            return affected > 0 ? Results.Ok() : Results.NotFound();
        });
    }

    public record NotificationDto(
        int Id, string SetId, string SetName, string? SetImg, DateTime DetectedAt, bool Read, List<NotificationItemDto> Items);

    public record NotificationItemDto(
        string PartNum, string? PartName, string? PartImg, string? ColorName, string? HexColor,
        string ChangeKind, int OldCount, int NewCount);
}
