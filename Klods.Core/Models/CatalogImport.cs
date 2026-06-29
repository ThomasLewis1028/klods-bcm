using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

/// <summary>
/// Audit row for each bulk catalog load — so you can see when the catalog was last refreshed
/// and which drops succeeded or failed (e.g. a missing file).
/// </summary>
[Table("CatalogImports")]
public class CatalogImport
{
    public int Id { get; set; }

    public DateTime ImportedAt { get; set; }

    /// <summary>Best-effort date the source files were pulled from Rebrickable (file last-modified), if known.</summary>
    public DateTime? SnapshotDate { get; set; }

    public string Source { get; set; }

    public string Status { get; set; }

    /// <summary>Row-count summary on success, or the failure reason (e.g. missing files).</summary>
    public string? Notes { get; set; }
}
