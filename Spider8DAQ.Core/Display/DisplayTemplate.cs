using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spider8DAQ.Core.Display;

public enum PanelWidgetType
{
    YtPlot,
    Numeric,
    Bar,
    XyPlot,
    Fft,
    DualYt
}

public sealed class PanelWidget
{
    public PanelWidgetType Type { get; set; } = PanelWidgetType.Numeric;
    public string Title { get; set; } = "Widget";
    public int ChannelIndex { get; set; }
    public int ChannelIndexB { get; set; } = 1;
    public double Row { get; set; }
    public double Column { get; set; }
    public double RowSpan { get; set; } = 1;
    public double ColumnSpan { get; set; } = 1;
}

public sealed class DisplayTemplate
{
    public string Name { get; set; } = "Default";
    /// <summary>UPET plot mode string: Y(t), Dual Y(t), Y(X), Numeric, Bar, FFT.</summary>
    public string PlotMode { get; set; } = "Y(t)";
    public bool PanelMode { get; set; }
    public bool MultiPanel { get; set; }
    public int XyXChannel { get; set; }
    public int XyYChannel { get; set; } = 1;
    public int FftChannel { get; set; }
    public List<PanelWidget> Widgets { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static DisplayTemplate CreateDefault() => new()
    {
        Name = "YT + Numerics",
        PlotMode = "Y(t)",
        MultiPanel = true,
        Widgets =
        {
            new() { Type = PanelWidgetType.YtPlot, Title = "Main YT", ChannelIndex = 0, Row = 0, Column = 0, ColumnSpan = 2, RowSpan = 2 },
            new() { Type = PanelWidgetType.Numeric, Title = "CH1", ChannelIndex = 0, Row = 0, Column = 2 },
            new() { Type = PanelWidgetType.Numeric, Title = "CH2", ChannelIndex = 1, Row = 1, Column = 2 },
            new() { Type = PanelWidgetType.Bar, Title = "CH1 bar", ChannelIndex = 0, Row = 2, Column = 0, ColumnSpan = 3 }
        }
    };

    public static DisplayTemplate CreateNumericsOnly() => new()
    {
        Name = "Numerics only",
        PlotMode = "Numeric",
        PanelMode = true,
        Widgets = Enumerable.Range(0, 8)
            .Select(i => new PanelWidget { Type = PanelWidgetType.Numeric, Title = $"CH{i + 1}", ChannelIndex = i, Row = i / 4, Column = i % 4 })
            .ToList()
    };

    public static DisplayTemplate CreateBarsFocus() => new()
    {
        Name = "Bars focus",
        PlotMode = "Bar",
        Widgets = Enumerable.Range(0, 4)
            .Select(i => new PanelWidget { Type = PanelWidgetType.Bar, Title = $"CH{i + 1}", ChannelIndex = i, Row = 0, Column = i })
            .ToList()
    };

    public static DisplayTemplate CreateForceDisplacement() => new()
    {
        Name = "Forță–Deplasare Y(X)",
        PlotMode = "Y(X)",
        XyXChannel = 2,
        XyYChannel = 1,
        Widgets =
        {
            new() { Type = PanelWidgetType.XyPlot, Title = "Y(X)", ChannelIndex = 0, ChannelIndexB = 1 }
        }
    };

    public static DisplayTemplate CreateFftFocus() => new()
    {
        Name = "FFT live",
        PlotMode = "FFT",
        FftChannel = 1,
        Widgets =
        {
            new() { Type = PanelWidgetType.Fft, Title = "FFT", ChannelIndex = 0 }
        }
    };

    public static DisplayTemplate CreateYtSingle() => new()
    {
        Name = "Y(t) single",
        PlotMode = "Y(t)",
        Widgets =
        {
            new() { Type = PanelWidgetType.YtPlot, Title = "Y(t)", ChannelIndex = 0, ColumnSpan = 3, RowSpan = 2 }
        }
    };

    public static IReadOnlyList<DisplayTemplate> BuiltIns { get; } =
    [
        CreateDefault(),
        CreateYtSingle(),
        CreateNumericsOnly(),
        CreateBarsFocus(),
        CreateForceDisplacement(),
        CreateFftFocus()
    ];

    public static async Task SaveAsync(string path, DisplayTemplate template)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(template, JsonOptions));
    }

    public static async Task<DisplayTemplate> LoadAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<DisplayTemplate>(json, JsonOptions) ?? CreateDefault();
    }

    public static void Save(string path, DisplayTemplate template)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(template, JsonOptions));
    }

    public static DisplayTemplate Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<DisplayTemplate>(json, JsonOptions) ?? CreateDefault();
    }
}
