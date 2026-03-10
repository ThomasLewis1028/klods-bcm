using System.ComponentModel.DataAnnotations.Schema;

namespace LEGO_Inventory;

[Table("BrickOwned")]
public class BrickOwned
{
    public int UserId { get; set; }

    public string PartNum { get; set; }

    public string ColorId { get; set; }

    public int Stock { get; set; }
}
