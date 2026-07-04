using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

/// <summary>
/// Per-instance part stock for an owned minifig. Mirrors <see cref="SetBrickOwned"/>:
/// tracks whether <em>this specific</em> minifig copy has each of its parts.
/// </summary>
[Table("MinifigBrickOwned")]
public class MinifigBrickOwned
{
    public int UserId { get; set; }

    public string MinifigId { get; set; }

    public int MinifigIndex { get; set; }

    public string PartNum { get; set; }

    public string ColorId { get; set; }

    public int Stock { get; set; }
}
