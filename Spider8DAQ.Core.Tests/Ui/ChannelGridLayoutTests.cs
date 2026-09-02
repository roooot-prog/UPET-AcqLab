using System.Text.RegularExpressions;
using Xunit;

namespace Spider8DAQ.Core.Tests.Ui;

/// <summary>
/// Operator Canale DAQ grid: all-star columns fill the card; 1px dividers; Senzor 2*.
/// Expert extras live in CANAL SELECTAT.
/// </summary>
public class ChannelGridLayoutTests
{
    private static readonly string[] OperatorHeaders =
        ["On", "LED", "Nume", "Citire", "Unit", "Semnal", "Punte", "Senzor"];

    private static readonly string[] ExpertGridHeadersForbidden =
        ["Graf", "Rec", "Scale", "Offset", "Hz", "Filtru", "Exc V", "Rsh kΩ", "Alm", "Lo", "Hi", "Zero", "Shunt"];

    [Fact]
    public void Operator_grid_is_all_star_columns_with_gridlines()
    {
        var card = ReadCardChannelsXaml();
        var columns = ExtractColumnsBlock(card);
        var headers = Regex.Matches(columns, @"Header=""([^""]*)""")
            .Select(m => m.Groups[1].Value)
            .ToArray();

        Assert.Equal(OperatorHeaders, headers);

        foreach (var forbidden in ExpertGridHeadersForbidden)
            Assert.DoesNotContain(forbidden, headers);

        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Disabled\"", card);
        Assert.Contains("HorizontalAlignment=\"Stretch\"", card);
        Assert.Contains("GridLinesVisibility=\"All\"", card);
        Assert.Contains("HorizontalGridLinesBrush=\"{DynamicResource GridLineBrush}\"", card);
        Assert.Contains("VerticalGridLinesBrush=\"{DynamicResource GridLineBrush}\"", card);
        Assert.Contains("Value=\"0,0,1,1\"", card);
        Assert.Contains("Value=\"0,0,1,2\"", card);
        Assert.DoesNotContain("Width=\"1.85*\"", columns);
        Assert.DoesNotContain("Width=\"0.45*\"", columns);
        Assert.DoesNotContain("Width=\"Auto\"", columns);
        Assert.DoesNotContain("Width=\"200\"", columns);
        Assert.Equal(8, Regex.Matches(columns, @"Width=""[\d.]*\*""").Count);
        Assert.Contains("MinWidth=\"40\"", columns);
        Assert.Contains("MinWidth=\"32\"", columns);
        Assert.Contains("MinWidth=\"72\"", columns);
        Assert.Contains("MinWidth=\"100\"", columns);
        Assert.Contains("MinWidth=\"56\"", columns);
        Assert.Contains("MinWidth=\"64\"", columns);
        Assert.Contains("MinWidth=\"120\"", columns);
        Assert.Matches(@"Header=""Citire""[\s\S]*?Width=""1\.4\*""", columns);
        Assert.Matches(@"Header=""Senzor""[\s\S]*?Width=""2\*""", columns);
        Assert.Contains("CharacterEllipsis", columns);
        Assert.DoesNotContain("{x:Reference RootWin}", card);
        Assert.Contains("FontSize=\"15\"", columns);
    }

    [Fact]
    public void Empty_senzor_display_uses_citire_placeholder()
    {
        var src = File.ReadAllText(FindUiModels());
        Assert.Contains("SensorFunctionDisplay", src);
        Assert.Contains("LiveCitire.Placeholder", src);
        Assert.Contains("string.IsNullOrWhiteSpace(SensorName)", src);
        Assert.DoesNotContain(
            "string.IsNullOrWhiteSpace(SensorName) ? Bridge",
            src);
    }

    [Fact]
    public void Expert_editors_live_in_canal_selectat_not_grid()
    {
        var card = ReadCardChannelsXaml();
        var columns = ExtractColumnsBlock(card);
        Assert.DoesNotContain("SelectedChannelRow.Scale", columns);
        Assert.Contains("CANAL SELECTAT", card);
        Assert.Contains("SelectedChannelRow.Scale", card);
        Assert.Contains("SelectedChannelRow.ShowOnPlot", card);
        Assert.Contains("SelectedChannelRow.RecordEnabled", card);
        Assert.Contains("SelectedChannelRow.ShuntKohm", card);
        Assert.Contains("SelectedChannelRow.ChannelSampleRateHz", card);
        Assert.Contains("SelectedChannelRow.AlarmEnabled", card);
        Assert.Contains("BindingProxy", File.ReadAllText(FindMainWindowXaml()));
    }

    [Fact]
    public void Canal_selectat_is_two_column_expert_without_vscroll()
    {
        var card = ReadCardChannelsXaml();
        var canalStart = card.IndexOf("Text=\"CANAL SELECTAT\"", StringComparison.Ordinal);
        Assert.True(canalStart >= 0, "CANAL SELECTAT header missing");
        var canal = card[canalStart..];

        Assert.Contains("Height=\"520\"", card);
        Assert.Contains("MinHeight=\"400\"", card);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Disabled\"", card);
        Assert.DoesNotContain("<ScrollViewer", canal);
        Assert.DoesNotContain("VerticalScrollBarVisibility=\"Auto\"", canal);
        Assert.Contains("StringFormat=0.0", canal);
        Assert.Contains("Grid.Column=\"2\"", canal);
        Assert.Contains("Detalii expert", canal);
        Assert.Contains("IsExpertMode, Converter={StaticResource BoolToVis}", canal);
        Assert.DoesNotContain("{x:Reference RootWin}", card);
        Assert.DoesNotContain("ZeroDisplay", canal);
        Assert.DoesNotContain("LastShuntReading", canal);
    }

    [Fact]
    public void Channels_card_default_height_is_520_in_layout_store()
    {
        var store = File.ReadAllText(FindUiLayoutStore());
        Assert.Contains("CurrentVersion = 2", store);
        Assert.Contains("Height = 520", store);
        Assert.Contains("MigrateChannelsCardSize", store);
        Assert.Contains("Top = 528", store);
    }

    private static string ExtractColumnsBlock(string card)
    {
        const string start = "<DataGrid.Columns>";
        const string end = "</DataGrid.Columns>";
        var i = card.IndexOf(start, StringComparison.Ordinal);
        var j = card.IndexOf(end, StringComparison.Ordinal);
        Assert.True(i >= 0 && j > i, "Channels DataGrid.Columns block missing");
        return card.Substring(i, j - i + end.Length);
    }

    private static string ReadCardChannelsXaml()
    {
        var xaml = File.ReadAllText(FindMainWindowXaml());
        var start = xaml.IndexOf("CardId=\"measure.channels\"", StringComparison.Ordinal);
        var end = xaml.IndexOf("CardId=\"measure.sensors\"", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "Canale DAQ card not found in MainWindow.xaml");
        return xaml.Substring(start, end - start);
    }

    private static string FindMainWindowXaml() => FindAppFile("MainWindow.xaml");

    private static string FindUiLayoutStore() => FindAppFile(Path.Combine("Controls", "UiLayoutStore.cs"));

    private static string FindUiModels() => FindAppFile(Path.Combine("ViewModels", "UiModels.cs"));

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
