using System.Windows;
using System.Windows.Media;

namespace Spider8DAQ.App;

/// <summary>
/// Light lab chrome vs optional Dark laborator (anthracite + amber numerals).
/// Mutates existing brush instances so StaticResource chrome updates.
/// </summary>
public static class LabTheme
{
    public static void Apply(bool dark)
    {
        var app = Application.Current;
        if (app?.Resources is null) return;
        if (dark) ApplyDark(app.Resources);
        else ApplyLight(app.Resources);
    }

    private static void ApplyLight(ResourceDictionary r)
    {
        // One lab surface — cards use hairlines, not stacked grey slabs. 3.3.87: near-black ink.
        Set(r, "BgColor", "BgBrush", Color.FromRgb(0xDC, 0xE0, 0xE6));
        Set(r, "PanelColor", "PanelBrush", Color.FromRgb(0xE8, 0xEC, 0xF0));
        Set(r, "PanelAltColor", "PanelAltBrush", Color.FromRgb(0xDE, 0xE3, 0xE9));
        Set(r, "BorderColor", "BorderBrushUi", Color.FromRgb(0x6E, 0x77, 0x80));
        Set(r, "TextColor", "TextBrush", Color.FromRgb(0x1A, 0x1A, 0x1A));
        Set(r, "MutedColor", "MutedBrush", Color.FromRgb(0x55, 0x55, 0x55));
        Set(r, "RibbonColor", "RibbonBrush", Color.FromRgb(0xDC, 0xE0, 0xE6));
        Set(r, "HairlineColor", "HairlineBrush", Color.FromRgb(0x9A, 0xA3, 0xAD));
        Set(r, "GridLineColor", "GridLineBrush", Color.FromRgb(0x9A, 0xA3, 0xAD));
        Set(r, "StatusBarColor", "StatusBarBrush", Color.FromRgb(0xD4, 0xD8, 0xDE));
        Set(r, "TabIdleColor", "TabIdleBrush", Color.FromRgb(0x44, 0x44, 0x44));
        Set(r, "TabFillColor", "TabFillBrush", Color.FromRgb(0xE8, 0xEC, 0xF0));
        Set(r, "TabSelectedFillColor", "TabSelectedFillBrush", Color.FromRgb(0xD0, 0xD8, 0xE4));
        Set(r, "TabSelectedBorderColor", "TabSelectedBorderBrush", Color.FromRgb(0x4A, 0x55, 0x60));
        SetBrush(r, "MeasureValueBrush", Color.FromRgb(0x1A, 0x1A, 0x1A));
        SetBrush(r, "UnitBrush", Color.FromRgb(0x55, 0x55, 0x55));
        SetBrush(r, "CardHeaderBrush", Color.FromRgb(0xE8, 0xEC, 0xF0));
        SetBrush(r, "HoverBrush", Color.FromRgb(0xC5, 0xD8, 0xEC));
        SetBrush(r, "SelectionBrush", Color.FromRgb(0xB4, 0xD0, 0xEC));
        SetBrush(r, "SelectionTextBrush", Color.FromRgb(0x10, 0x14, 0x1A));
        SetSystemSelection(r, Color.FromRgb(0xB4, 0xD0, 0xEC), Color.FromRgb(0x10, 0x14, 0x1A));
    }

    private static void ApplyDark(ResourceDictionary r)
    {
        // Anthracite instrument chrome — bright amber numerals, off-white panel text.
        Set(r, "BgColor", "BgBrush", Color.FromRgb(0x1E, 0x22, 0x28));
        Set(r, "PanelColor", "PanelBrush", Color.FromRgb(0x2A, 0x2F, 0x36));
        Set(r, "PanelAltColor", "PanelAltBrush", Color.FromRgb(0x34, 0x3A, 0x42));
        Set(r, "BorderColor", "BorderBrushUi", Color.FromRgb(0x7A, 0x84, 0x90));
        Set(r, "TextColor", "TextBrush", Color.FromRgb(0xF4, 0xF0, 0xE8));
        Set(r, "MutedColor", "MutedBrush", Color.FromRgb(0xC4, 0xBB, 0xA8));
        Set(r, "RibbonColor", "RibbonBrush", Color.FromRgb(0x1E, 0x22, 0x28));
        Set(r, "HairlineColor", "HairlineBrush", Color.FromRgb(0x5A, 0x64, 0x70));
        Set(r, "GridLineColor", "GridLineBrush", Color.FromRgb(0x8A, 0x94, 0xA0));
        Set(r, "StatusBarColor", "StatusBarBrush", Color.FromRgb(0x24, 0x28, 0x2E));
        Set(r, "TabIdleColor", "TabIdleBrush", Color.FromRgb(0xC8, 0xC0, 0xB0));
        Set(r, "TabFillColor", "TabFillBrush", Color.FromRgb(0x32, 0x38, 0x40));
        Set(r, "TabSelectedFillColor", "TabSelectedFillBrush", Color.FromRgb(0x44, 0x50, 0x60));
        Set(r, "TabSelectedBorderColor", "TabSelectedBorderBrush", Color.FromRgb(0xD0, 0xC8, 0xB8));
        SetBrush(r, "MeasureValueBrush", Color.FromRgb(0xFF, 0xC4, 0x4A)); // bright amber, not brown-mud
        SetBrush(r, "UnitBrush", Color.FromRgb(0xD8, 0xCD, 0xB8));
        SetBrush(r, "CardHeaderBrush", Color.FromRgb(0x2A, 0x2F, 0x36));
        SetBrush(r, "HoverBrush", Color.FromRgb(0x3A, 0x52, 0x72));
        SetBrush(r, "SelectionBrush", Color.FromRgb(0x2C, 0x4A, 0x6E));
        SetBrush(r, "SelectionTextBrush", Color.FromRgb(0xF4, 0xF0, 0xE8));
        SetSystemSelection(r, Color.FromRgb(0x2C, 0x4A, 0x6E), Color.FromRgb(0xF4, 0xF0, 0xE8));
    }

    private static void Set(ResourceDictionary r, string colorKey, string brushKey, Color c)
    {
        r[colorKey] = c;
        SetBrush(r, brushKey, c);
    }

    private static void SetBrush(ResourceDictionary r, string brushKey, Color c)
    {
        if (r[brushKey] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = c;
            return;
        }

        var brush = new SolidColorBrush(c);
        r[brushKey] = brush;
    }

    /// <summary>Keep DataGrid hover/selection blue (never the system beige/yellow highlight).</summary>
    private static void SetSystemSelection(ResourceDictionary r, Color fill, Color text)
    {
        SetKeyedBrush(r, SystemColors.HighlightBrushKey, fill);
        SetKeyedBrush(r, SystemColors.InactiveSelectionHighlightBrushKey, fill);
        SetKeyedBrush(r, SystemColors.HighlightTextBrushKey, text);
        SetKeyedBrush(r, SystemColors.InactiveSelectionHighlightTextBrushKey, text);
    }

    private static void SetKeyedBrush(ResourceDictionary r, object key, Color c)
    {
        if (r.Contains(key) && r[key] is SolidColorBrush existing && !existing.IsFrozen)
        {
            existing.Color = c;
            return;
        }

        r[key] = new SolidColorBrush(c);
    }
}
