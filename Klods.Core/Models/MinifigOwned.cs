using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

[Table("MinifigOwned")]
public class MinifigOwned
{
    public int UserId { get; set; }

    public string MinifigId { get; set; }

    public int Stock { get; set; }
}
