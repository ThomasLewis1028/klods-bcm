namespace Klods;

/// <summary>
/// The available Klods mascot eye-color schemes. Each name maps to a
/// pre-composited image at wwwroot/images/klods-{name}.png in Klods.Web.
/// </summary>
public static class MascotVariants
{
    public const string Default = "teal";

    /// <summary>Variant name → human-readable label.</summary>
    public static readonly IReadOnlyList<(string Name, string Label)> All =
    [
        ("teal",   "Teal"),
        ("blue",   "Blue"),
        ("orange", "Orange"),
        ("yellow", "Yellow"),
        ("lime",   "Lime"),
        ("purple", "Purple"),
        ("pink",   "Pink"),
        ("coral",  "Coral"),
        ("clear",  "Clear"),
    ];

    public static bool IsValid(string? name) =>
        name is not null && All.Any(v => v.Name == name);

    /// <summary>Falls back to the default when the stored value is null or unknown.</summary>
    public static string Resolve(string? name) => IsValid(name) ? name! : Default;
}
