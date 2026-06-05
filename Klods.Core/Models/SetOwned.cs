using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

[Table("SetsOwned")]
public class SetOwned
{
    public int UserId { get; set; }

    public string SetId { get; set; }

    public int SetIndex { get; set; }
}
