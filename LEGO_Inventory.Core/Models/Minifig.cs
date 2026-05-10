using System.ComponentModel.DataAnnotations.Schema;

namespace LEGO_Inventory;

[Table("Minifigs")]
public class Minifig
{
    public string MinifigId { get; set; }
    
    public string MinifigName { get; set; }
    
    public string? MinifigImgUrl { get; set; }
    
    public string MinifigUrl { get; set; }
}