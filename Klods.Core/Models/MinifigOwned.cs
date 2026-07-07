using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

/// <summary>
/// One physically-owned minifig instance. Instanced like <see cref="SetOwned"/> (each has its own
/// <see cref="MinifigBrickOwned"/> parts inventory), because a minifig is a container, not a fungible count.
/// <para>
/// <see cref="SetId"/>/<see cref="SetIndex"/> are null when the fig is loose, or point at the owned set
/// copy it belongs to. "Tied to a set" is just this pointer being set — the instance never moves tables
/// and <see cref="MinifigIndex"/> is permanent identity.
/// </para>
/// </summary>
[Table("MinifigOwned")]
public class MinifigOwned
{
    public int UserId { get; set; }

    public string MinifigId { get; set; }

    public int MinifigIndex { get; set; }

    public string? SetId { get; set; }

    public int? SetIndex { get; set; }

    [MaxLength(100)]
    public string? Location { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }
}
