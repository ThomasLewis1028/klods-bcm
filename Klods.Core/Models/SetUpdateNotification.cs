using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

/// <summary>
/// A per-user "this set you own changed" notice, created when the update poller re-imports a set the user
/// owns and the part list actually changed. <see cref="Items"/> carries the individual part changes.
/// Cleared some days after being read (and, if never read, after a longer window) by a cleanup pass.
/// </summary>
[Table("SetUpdateNotifications")]
public class SetUpdateNotification
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string SetId { get; set; }

    public DateTime DetectedAt { get; set; }

    /// <summary>When the user viewed the notification; null while unread.</summary>
    public DateTime? ReadAt { get; set; }

    public List<SetUpdateNotificationItem> Items { get; set; } = [];
}
