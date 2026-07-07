using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

[Table("BrickOwned")]
public class BrickOwned
{
    public int UserId { get; set; }

    public string PartNum { get; set; }

    public string ColorId { get; set; }

    public int Stock { get; set; }

    [MaxLength(100)]
    public string? Location { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }
}
