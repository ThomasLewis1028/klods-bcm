using System.ComponentModel.DataAnnotations.Schema;

namespace LEGO_Inventory;

[Table("SetBricks")]
public class SetBrick
{
    public string PartNum { get; set; }

    public string ColorId { get; set; }
    
    public string SetId { get; set; }
    
    public int SetIndex { get; set; }
    
    public int Count { get; set; }
    
    public int SpareCount { get; set; }
    
    public int Stock { get; set; }
}