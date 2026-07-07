namespace Klods;

/// <summary>The kind of change a re-import made to a single part in a set (or minifig) BOM.</summary>
public enum PartChangeKind { Added, Removed, QtyChanged }

/// <summary>
/// A single part-level change produced by re-importing a set. Emitted by <see cref="ImportData"/> so the
/// poller can persist a per-owner changelog. Counts are the regular (buildable) quantity.
/// </summary>
public record PartChange(string PartNum, string ColorId, PartChangeKind Kind, int OldCount, int NewCount);

/// <summary>
/// Compares pre- and post-import regular counts (keyed by part+color) and appends Added / Removed /
/// QtyChanged entries. No-op deltas (e.g. spare-only rows) are skipped. Shared by the per-set importer
/// (<see cref="ImportData"/>) and the bulk importer so both produce identical changelogs.
/// </summary>
public static class PartDiff
{
    public static void Collect(
        Dictionary<(string PartNum, string ColorId), int> before,
        Dictionary<(string PartNum, string ColorId), int> after,
        List<PartChange> changes)
    {
        foreach (var (key, newCount) in after)
        {
            if (!before.TryGetValue(key, out var oldCount))
            {
                if (newCount != 0)
                    changes.Add(new PartChange(key.PartNum, key.ColorId, PartChangeKind.Added, 0, newCount));
            }
            else if (oldCount != newCount)
            {
                changes.Add(new PartChange(key.PartNum, key.ColorId, PartChangeKind.QtyChanged, oldCount, newCount));
            }
        }

        foreach (var (key, oldCount) in before)
        {
            if (!after.ContainsKey(key) && oldCount != 0)
                changes.Add(new PartChange(key.PartNum, key.ColorId, PartChangeKind.Removed, oldCount, 0));
        }
    }
}
