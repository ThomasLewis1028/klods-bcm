using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

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

    public string? BricklinkId { get; set; }

    public string? BrickOwlId { get; set; }

    /// <summary>LEGO production element number (part+color specific) — what Pick-a-Brick / Bricks &amp; Pieces uses.</summary>
    public string? ElementId { get; set; }

    /// <summary>Rebrickable part category id (see <see cref="PartCategory"/>). Loose reference, not FK-enforced (mirrors ColorId).</summary>
    public int? PartCatId { get; set; }

    /// <summary>Denormalized count of catalog sets this part+color appears in — powers the "most used" default ordering.</summary>
    public int SetCount { get; set; }
}