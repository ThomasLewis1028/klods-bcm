using System.ComponentModel.DataAnnotations.Schema;

namespace LEGO_Inventory;

[Table("Colors")]
public class Color
{
    public string Id { get; set; }
    
    public string Name { get; set; }
    
    public string Hex { get; set; }
    
    public bool IsTrans { get; set; }
}