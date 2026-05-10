using System.Drawing;

namespace LEGO_Inventory;

public static class ColorHelper
{
    public static bool IsDark(string hex)
    {
        var c = ColorTranslator.FromHtml($"#{hex}");
        return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B < 128;
    }

    public static string ColorChipStyle(string hex)
    {
        var fg = IsDark(hex) ? "color: white;" : "color: #202020;";
        return $"background-color: #{hex}; {fg} width: -moz-available;";
    }
}
