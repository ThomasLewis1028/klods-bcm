namespace Klods.Services;

/// <summary>
/// Catalog display preferences. Currently just the set of "hidden" theme IDs — Rebrickable's catalog
/// includes a lot of non-building gear (bags, keychains, posters, books, plush…) that clutters the set
/// views. Hidden themes are filtered out of every catalog browse/search surface but left in the DB, so
/// toggling a theme back on reveals its sets instantly.
/// </summary>
public static class CatalogSettings
{
    public const string HiddenThemesKey = "catalog.hiddenThemes";

    /// <summary>Rebrickable "Gear" family themes hidden out of the box (admin can change this).</summary>
    public static readonly int[] DefaultHiddenThemes =
    [
        501, // Gear
        503, // Key Chain
        730, // Audio and Visual Media
        731, // Bag and Luggage Tags
        733, // Houseware
        735, // Plush Toys
        736, // Posters and Art Prints
        737, // Role Play Toys and Costumes
        739, // Stationery and Office Supplies
        740, // Storage
        741, // Tabletop Games and Puzzles
        742, // Video Games and Accessories
        757, // Ideas Books
        758, // Non-fiction Books
        759, // Story Books
        760, // Activity Books
        777, // Bags, Totes, & Luggage
    ];

    /// <summary>
    /// The theme IDs currently hidden. Absent setting → the sensible defaults; an explicitly-saved
    /// empty value → nothing hidden (the admin chose to show everything).
    /// </summary>
    public static async Task<int[]> GetHiddenThemeIdsAsync(SettingsService settings, CancellationToken ct = default)
    {
        var raw = await settings.GetAsync(HiddenThemesKey, ct);
        if (raw is null) return DefaultHiddenThemes;

        return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .Distinct()
            .ToArray();
    }

    public static Task SetHiddenThemeIdsAsync(SettingsService settings, IEnumerable<int> ids, CancellationToken ct = default)
        => settings.SetAsync(HiddenThemesKey, string.Join(",", ids.Distinct().OrderBy(x => x)), ct);
}
