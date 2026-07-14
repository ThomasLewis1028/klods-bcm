namespace Klods;

/// <summary>
/// The available Klods body color schemes. Each name maps to a
/// pre-rendered image at wwwroot/images/klods-body-{name}.png in Klods.Web
/// (whole robot, empty eye sockets — the eye layer is composited on top).
/// </summary>
public static class MascotBodies
{
    public const string Default = "classic";

    /// <summary>Body style name → human-readable label.</summary>
    public static readonly IReadOnlyList<(string Name, string Label)> All =
    [
        ("classic",  "Classic"),
        ("red",      "Red"),
        ("slate",    "Slate"),
        ("stealth",  "Stealth"),
        ("shadow",   "Shadow"),
        ("tuxedo",   "Tuxedo"),
        ("storm",    "Storm"),
        ("arctic",   "Arctic"),
        ("blizzard", "Blizzard"),
        ("midnight", "Midnight"),
        ("marine",   "Marine"),
    ];

    public static bool IsValid(string? name) =>
        name is not null && All.Any(v => v.Name == name);

    /// <summary>Falls back to the default when the stored value is null or unknown.</summary>
    public static string Resolve(string? name) => IsValid(name) ? name! : Default;
}
