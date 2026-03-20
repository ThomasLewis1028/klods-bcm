using System.ComponentModel.DataAnnotations.Schema;

namespace LEGO_Inventory;

[Table("Bricks")]
public class Brick
{
    public string PartNum { get; set; }
    
    public string Name { get; set; }
    
    public string? PartURL { get; set; }

    public string? PartImg { get; set; }

    public string? ColorId { get; set; }
    
    public string? ColorName { get; set; }
    
    public string? HexColor { get; set; }
    
    public bool IsTrans { get; set; }
}