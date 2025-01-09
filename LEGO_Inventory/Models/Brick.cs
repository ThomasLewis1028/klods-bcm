namespace LEGO_Inventory;

public class Brick
{
    public string PartNum { get; set; }
    
    public string Name { get; set; }
    
    public string? PartURL { get; set; }

    public string? PartImg { get; set; }

    public int Count { get; set; }
    
    public string? ColorId { get; set; }
    
    public string? ColorName { get; set; }
    
    public string? RGB { get; set; }
    
    public bool IsTrans { get; set; }
}