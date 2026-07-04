using System.ComponentModel.DataAnnotations.Schema;

namespace Klods;

/// <summary>
/// Rebrickable part category reference table (synced like <see cref="Color"/>).
/// Referenced loosely by <see cref="Brick.PartCatId"/>.
/// </summary>
[Table("PartCategories")]
public class PartCategory
{
    public int Id { get; set; }

    public string Name { get; set; }
}
