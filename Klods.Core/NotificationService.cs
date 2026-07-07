using Klods.Database;
using Microsoft.EntityFrameworkCore;

namespace Klods;

/// <summary>
/// Creates and prunes per-user "a set you own changed" notifications. The update poller calls
/// <see cref="WriteForSetChangeAsync"/> after re-importing a changed set, and <see cref="CleanupAsync"/>
/// on each poll to age out old notices.
/// </summary>
public class NotificationService(IDbContextFactory<InventoryContext> contextFactory, ILogger<NotificationService> logger)
{
    public const int ReadRetentionDays = 7;
    public const int UnreadRetentionDays = 90;

    /// <summary>
    /// Fans out one notification (with per-part items) to every user who currently owns the set. Owners are
    /// resolved at call time, which is "owned prior to the update" — re-import doesn't change ownership.
    /// </summary>
    public async Task WriteForSetChangeAsync(
        string setId, IReadOnlyList<PartChange> changes, DateTime detectedAt, CancellationToken ct = default)
    {
        if (changes.Count == 0)
            return;

        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var owners = await db.Set<SetOwned>().AsNoTracking()
            .Where(so => so.SetId == setId)
            .Select(so => so.UserId)
            .Distinct()
            .ToListAsync(ct);

        if (owners.Count == 0)
            return;

        foreach (var userId in owners)
        {
            db.Set<SetUpdateNotification>().Add(new SetUpdateNotification
            {
                UserId = userId,
                SetId = setId,
                DetectedAt = detectedAt,
                Items = changes.Select(c => new SetUpdateNotificationItem
                {
                    PartNum = c.PartNum,
                    ColorId = c.ColorId,
                    ChangeKind = c.Kind.ToString(),
                    OldCount = c.OldCount,
                    NewCount = c.NewCount,
                }).ToList(),
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Wrote set-update notifications for {SetId} to {OwnerCount} owner(s), {ItemCount} change(s) each",
            setId, owners.Count, changes.Count);
    }

    /// <summary>Deletes read notifications older than the read window and unread ones older than the longer window.</summary>
    public async Task<int> CleanupAsync(CancellationToken ct = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var readCutoff = now.AddDays(-ReadRetentionDays);
        var unreadCutoff = now.AddDays(-UnreadRetentionDays);

        // Items cascade at the DB, so deleting the notification rows is enough.
        var deleted = await db.Set<SetUpdateNotification>()
            .Where(n => (n.ReadAt != null && n.ReadAt < readCutoff)
                        || (n.ReadAt == null && n.DetectedAt < unreadCutoff))
            .ExecuteDeleteAsync(ct);

        if (deleted > 0)
            logger.LogInformation("Cleaned up {Count} expired set-update notification(s)", deleted);
        return deleted;
    }
}
