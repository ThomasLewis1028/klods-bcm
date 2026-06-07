namespace Klods.Mobile.Services;

public class ThemeService
{
    private const string DefaultHex = "#512BD4";

    public void Apply(string? hex, string? serverId = null)
    {
        if (serverId is not null)
            Preferences.Set($"theme_{serverId}", hex ?? string.Empty);

        var primary = ParseColor(hex) ?? ParseColor(DefaultHex)!;
        var secondary = BlendWithWhite(primary, 0.82f);
        var tertiary = BlendWithBlack(primary, 0.36f);
        var primaryDark = BlendWithWhite(primary, 0.55f);

        RunOnMain(() =>
        {
            var res = Application.Current!.Resources;
            res["Primary"] = primary;
            res["PrimaryDark"] = primaryDark;
            res["Secondary"] = secondary;
            res["Tertiary"] = tertiary;
            res["PrimaryBrush"] = new SolidColorBrush(primary);
            res["SecondaryBrush"] = new SolidColorBrush(secondary);
            res["TertiaryBrush"] = new SolidColorBrush(tertiary);
        });
    }

    public void LoadCached(string serverId)
    {
        var stored = Preferences.Get($"theme_{serverId}", string.Empty);
        Apply(string.IsNullOrEmpty(stored) ? null : stored);
    }

    public void Reset(string? serverId = null)
    {
        if (serverId is not null)
            Preferences.Remove($"theme_{serverId}");
        Apply(null);
    }

    private static Color? ParseColor(string? hex) =>
        string.IsNullOrWhiteSpace(hex) ? null :
        Color.TryParse(hex, out var c) ? c : null;

    private static Color BlendWithWhite(Color c, float factor) =>
        new(c.Red + (1f - c.Red) * factor,
            c.Green + (1f - c.Green) * factor,
            c.Blue + (1f - c.Blue) * factor,
            c.Alpha);

    private static Color BlendWithBlack(Color c, float factor) =>
        new(c.Red * (1f - factor),
            c.Green * (1f - factor),
            c.Blue * (1f - factor),
            c.Alpha);

    private static void RunOnMain(Action action)
    {
        if (Application.Current?.Dispatcher?.IsDispatchRequired == true)
            Application.Current.Dispatcher.Dispatch(action);
        else
            action();
    }
}
