using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

[Table("MinifigBricks")]
public class MinifigBrick
{
    public string MinifigID { get; set; }
    
    public string BrickID { get; set; }
    
    public string ColorId { get; set; }
    
    public int Quantity { get; set; }
}