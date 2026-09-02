using System.Windows.Media;

namespace Spider8DAQ.App.Controls;

/// <summary>
/// Stable per-channel-index colors for grid accent, Y(t) series, legend, and CWT chrome.
/// Index 0 → CH0, 1 → CH1, … — independent of enable/plot order.
/// </summary>
public static class ChannelPalette
{
    // Distinct lab-friendly hues (no purple glow); readable on gray panels and white plots.
    private static readonly (byte R, byte G, byte B)[] Palette =
    [
        (0x00, 0x72, 0xC6), // CH0 steel blue
        (0xE0, 0x7A, 0x00), // CH1 orange
        (0x0A, 0x7A, 0x3E), // CH2 green
        (0xC8, 0x10, 0x2E), // CH3 crimson
        (0x8B, 0x6B, 0x00), // CH4 gold-brown
        (0x00, 0x7A, 0x7A), // CH5 teal
        (0x5A, 0x3D, 0x8C), // CH6 indigo (muted)
        (0x4E, 0x58, 0x64), // CH7 slate
        (0xC2, 0x18, 0x5B), // CH8 pink
        (0x00, 0x6B, 0x8F), // CH9 cyan-dark
        (0x6D, 0x4C, 0x41), // CH10 brown
        (0x2E, 0x7D, 0x32), // CH11 forest
    ];

    private static readonly Brush[] WpfBrushes = BuildWpfBrushes();

    public static int Count => Palette.Length;

    public static (byte R, byte G, byte B) GetRgb(int channelIndex)
    {
        var i = channelIndex < 0 ? 0 : channelIndex % Palette.Length;
        return Palette[i];
    }

    public static string GetHex(int channelIndex)
    {
        var (r, g, b) = GetRgb(channelIndex);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    public static Brush GetWpfBrush(int channelIndex)
    {
        var i = channelIndex < 0 ? 0 : channelIndex % WpfBrushes.Length;
        return WpfBrushes[i];
    }

    public static Color GetWpfColor(int channelIndex)
    {
        var (r, g, b) = GetRgb(channelIndex);
        return Color.FromRgb(r, g, b);
    }

    public static ScottPlot.Color GetScottPlotColor(int channelIndex)
    {
        var (r, g, b) = GetRgb(channelIndex);
        return new ScottPlot.Color(r, g, b);
    }

    private static Brush[] BuildWpfBrushes()
    {
        var brushes = new Brush[Palette.Length];
        for (var i = 0; i < Palette.Length; i++)
        {
            var (r, g, b) = Palette[i];
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            brushes[i] = brush;
        }
        return brushes;
    }
}
