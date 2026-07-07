using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

/// <summary>
/// One part-level change within a <see cref="SetUpdateNotification"/>. The part is a loose reference
/// (like ColorId/ThemeId elsewhere) — the UI left-joins to <see cref="Brick"/> for name/image/color.
/// </summary>
[Table("SetUpdateNotificationItems")]
public class SetUpdateNotificationItem
{
    public int Id { get; set; }

    public int NotificationId { get; set; }

    public string PartNum { get; set; }

    public string ColorId { get; set; }

    /// <summary>Serialized <see cref="PartChangeKind"/>: "Added", "Removed", or "QtyChanged".</summary>
    public string ChangeKind { get; set; }

    public int OldCount { get; set; }

    public int NewCount { get; set; }
}
