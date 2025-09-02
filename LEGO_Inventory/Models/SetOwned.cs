using System.ComponentModel.DataAnnotations.Schema;

namespace LEGO_Inventory;

[Table("SetsOwned")]
public class SetOwned
{
    public string SetId { get; set; }
    
    public int Index { get; set; }
}