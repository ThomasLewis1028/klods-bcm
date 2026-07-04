using MudBlazor;

namespace Klods.Services;

public class ThemeService
{
    public const string DefaultPrimaryColor = "#DA291C";
    public const double DefaultFontScale = 1.0;

    public bool IsDarkMode { get; private set; } = true;
    public double FontScale { get; private set; } = DefaultFontScale;

    public MudTheme Theme { get; } = new MudTheme()
    {
        PaletteDark = new PaletteDark()
        {
            Primary = "#DA291C",
            Secondary = "#FFD700",
            AppbarBackground = "#1C1C1E",
            Background = "#121212",
            Surface = "#1E1E1E",
            DrawerBackground = "#1C1C1E",
            DrawerText = "rgba(255,255,255,0.87)",
            DrawerIcon = "rgba(255,255,255,0.87)",
            Success = "#4CAF50",
            Info = "#2196F3",
        },
        PaletteLight = new PaletteLight()
        {
            Primary = "#DA291C",
            // Darker gold/amber than the dark-mode accent — the bright gold is unreadable as text on light.
            Secondary = "#8A6D00",
            Warning = "#A15C00",
            AppbarBackground = "#DA291C",
            AppbarText = "rgba(255,255,255,0.87)",
        }
    };

    public event Action? OnChange;

    public void ToggleDarkMode()
    {
        IsDarkMode = !IsDarkMode;
        OnChange?.Invoke();
    }

    public void SetDarkMode(bool isDark)
    {
        IsDarkMode = isDark;
        OnChange?.Invoke();
    }

    public void SetPrimaryColor(string hex)
    {
        Theme.PaletteDark.Primary = hex;
        Theme.PaletteLight.Primary = hex;
        Theme.PaletteLight.AppbarBackground = hex;
        OnChange?.Invoke();
    }

    public void SetFontScale(double scale)
    {
        // Clamp to the supported range; a stray 0 would make everything invisible.
        FontScale = scale is >= 0.5 and <= 2.0 ? scale : DefaultFontScale;
        OnChange?.Invoke();
    }
}
