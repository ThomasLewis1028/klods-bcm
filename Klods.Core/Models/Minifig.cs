using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

[Table("Minifigs")]
public class Minifig
{
    public string MinifigId { get; set; }

    public string Name { get; set; }

    public string? ImgUrl { get; set; }

    public string? Url { get; set; }

    /// <summary>Denormalized part count (Rebrickable num_parts), mirrors <see cref="Set.NumBricks"/>.</summary>
    public int NumParts { get; set; }

    /// <summary>Rebrickable last_modified_dt — drives the freshness upsert, mirrors <see cref="Set.DateModified"/>.</summary>
    public DateTime DateModified { get; set; }
}
