using System.ComponentModel.DataAnnotations.Schema;

namespace LEGO_Inventory;

[Table("SetMinifig")]
public class SetMinifig
{
    public string SetId { get; set; }

    public string MinifigId { get; set; }

    public int Count { get; set; }
}
