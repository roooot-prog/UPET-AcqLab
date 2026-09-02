using Xunit;

namespace Spider8DAQ.Core.Tests.Ui;

/// <summary>
/// Top instrument chrome: short LIVE band, centered On-channel chips, grouped Conn/Start row.
/// </summary>
public class InstrumentChromeTests
{
    [Fact]
    public void Citire_strip_is_compact_centered_chips_not_hero_slab()
    {
        var chrome = ReadInstrumentChrome();
        Assert.Contains("LiveStripChip", chrome);
        Assert.Contains("HorizontalAlignment=\"Center\"", chrome);
        Assert.Contains("Orientation=\"Horizontal\"", chrome);
        Assert.DoesNotContain("MinWidth=\"280\"", chrome);
        Assert.DoesNotContain("FontSize=\"40\"", chrome);
        Assert.DoesNotContain("UniformGrid", chrome);
        Assert.DoesNotContain("{x:Reference RootWin}", chrome);
        Assert.Contains("Binding Enabled", chrome);
        Assert.Contains("Collapsed", chrome);
    }

    [Fact]
    public void Action_row_is_centered_conexiune_plus_measure_clusters()
    {
        var chrome = ReadInstrumentChrome();
        Assert.Contains("HorizontalAlignment=\"Center\"", chrome);
        Assert.Contains("Text=\"Conn\"", chrome);
        Assert.Contains("Text=\"Deconectează\"", chrome);
        Assert.Contains("Text=\"Start\"", chrome);
        Assert.Contains("Text=\"Zero\"", chrome);
        Assert.Contains("IsExpertMode, Converter={StaticResource BoolToVis}", chrome);
        Assert.Contains("ItemsSource=\"{Binding Backends}\"", chrome);
        Assert.Contains("ItemsSource=\"{Binding Ports}\"", chrome);
        Assert.DoesNotContain("{x:Reference RootWin}", chrome);
    }

    [Fact]
    public void Status_band_stays_full_width_but_short()
    {
        var chrome = ReadInstrumentChrome();
        var app = File.ReadAllText(FindAppFile("App.xaml"));
        Assert.Contains("Style=\"{StaticResource StatusBand}\"", chrome);
        Assert.Contains("MachineStateHeadline", chrome);
        Assert.Contains("<Setter Property=\"Padding\" Value=\"12,4\"/>", app);
        Assert.DoesNotContain("Padding\" Value=\"16,10\"", app);
        Assert.Contains("FontSize=\"14\"", chrome);
        Assert.DoesNotContain("FontSize=\"18\"", chrome);
        Assert.Contains("x:Key=\"LiveStripChip\"", app);
        Assert.Contains("Compact instrument tabs", app);
        Assert.Contains("TabFillBrush", app);
    }

    private static string ReadInstrumentChrome()
    {
        var xaml = File.ReadAllText(FindAppFile("MainWindow.xaml"));
        var start = xaml.IndexOf("Full-width machine state", StringComparison.Ordinal);
        var end = xaml.IndexOf("Unelte — Experiment", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Instrument chrome block not found in MainWindow.xaml");
        return xaml.Substring(start, end - start);
    }

    private static string FindAppFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "Spider8DAQ.App", relative);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Spider8DAQ.App/" + relative + " not found from " + AppContext.BaseDirectory);
    }
}
