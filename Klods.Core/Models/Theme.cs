using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

/// <summary>
/// Rebrickable theme reference table (synced like <see cref="PartCategory"/>). Referenced loosely by
/// <see cref="Set.ThemeId"/>. <see cref="ParentId"/> is a soft self-reference (no DB FK) forming the
/// theme/subtheme tree — e.g. "Star Wars" under "Licensed", or the "Gear" branch of non-building items.
/// </summary>
[Table("Themes")]
public class Theme
{
    public int Id { get; set; }

    public string Name { get; set; }

    public int? ParentId { get; set; }
}
