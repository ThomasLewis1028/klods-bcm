using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

[Table("SetBrickOwned")]
public class SetBrickOwned
{
    public int UserId { get; set; }

    public string SetId { get; set; }

    public int SetIndex { get; set; }

    public string PartNum { get; set; }

    public string ColorId { get; set; }

    public int Stock { get; set; }
}
