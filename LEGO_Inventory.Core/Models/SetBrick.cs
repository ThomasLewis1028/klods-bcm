using System.ComponentModel.DataAnnotations.Schema;

namespace LEGO_Inventory;

[Table("SetBricks")]
public class SetBrick
{
    public string SetId { get; set; }

    public string PartNum { get; set; }

    public string ColorId { get; set; }

    public int Count { get; set; }

    public int SpareCount { get; set; }
}
