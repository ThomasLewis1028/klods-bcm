using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

[Table("SetsOwned")]
public class SetOwned
{
    public int UserId { get; set; }

    public string SetId { get; set; }

    public int SetIndex { get; set; }

    [MaxLength(100)]
    public string? Location { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }
}
