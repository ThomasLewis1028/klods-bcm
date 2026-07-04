using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

/// <summary>
/// A minifig's parts inventory (the minifig is a "mini set"). Mirrors <see cref="SetBrick"/>.
/// </summary>
[Table("MinifigBricks")]
public class MinifigBrick
{
    public string MinifigId { get; set; }

    public string PartNum { get; set; }

    public string ColorId { get; set; }

    public int Count { get; set; }

    public int SpareCount { get; set; }
}
