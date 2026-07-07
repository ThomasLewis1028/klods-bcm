using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

/// <summary>
/// A user-declared "fill": some quantity of a substitute brick used toward one set-brick requirement
/// on one owned copy. Scoped per-copy (<see cref="SetIndex"/>). The substitute may be a different mold
/// entirely — physical fit is not validated. Multiple rows can target the same requirement (a mix of
/// colours/parts). <see cref="PulledFromLoose"/> records how many units were drawn from the user's loose
/// stock when the fill was created, so removing the fill returns exactly that many.
/// </summary>
[Table("SetBrickSubstitutions")]
public class SetBrickSubstitution
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string SetId { get; set; }

    public int SetIndex { get; set; }

    // The requirement being filled — matches a SetBrick (SetId, PartNum, ColorId) row.
    public string ReqPartNum { get; set; }

    public string ReqColorId { get; set; }

    // What was actually used — any catalog brick (may differ in mold from the requirement).
    public string SubPartNum { get; set; }

    public string SubColorId { get; set; }

    public int Count { get; set; }

    public int PulledFromLoose { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }
}
