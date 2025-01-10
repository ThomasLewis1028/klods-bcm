using System.ComponentModel.DataAnnotations.Schema;

namespace LEGO_Inventory;

[Table("Sets")]
public class Set
{
    public string SetId { get; set; }
    
    public string Name { get; set; }
    
    public string SetURL { get; set; }
    
    public string SetImg { get; set; }
    
    public int NumParts { get; set; }
    
    public int ReleaseYear { get; set; }
    
    public DateTime DateModified { get; set; }
    
    public int OwnCount { get; set; }
    
    public int BuildCount { get; set; }
    
    public string ManualUrl { get; set; }
}