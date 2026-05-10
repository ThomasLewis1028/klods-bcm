using System.ComponentModel.DataAnnotations.Schema;

namespace LEGO_Inventory;

[Table("MinifigOwned")]
public class MinifigOwned
{
    public int UserId { get; set; }

    public string MinifigId { get; set; }

    public int Stock { get; set; }
}
