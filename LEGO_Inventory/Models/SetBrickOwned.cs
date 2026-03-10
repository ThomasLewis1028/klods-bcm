using System.ComponentModel.DataAnnotations.Schema;

namespace LEGO_Inventory;

[Table("SetBrickOwned")]
public class SetBrickOwned
{
    public int UserId { get; set; }

    public string SetId { get; set; }

    public int SetIndex { get; set; }

    public string PartNum { get; set; }

    public string ColorId { get; set; }

    public int Stock { get; set; }
}
