using Spider8DAQ.Core.Analysis;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Builds Y(t) ScottPlot images from an <see cref="OfflineSession"/> for Excel/PDF/HTML reports
/// and optional sibling PNGs next to CSV/TXT/MAT/DIAdem exports.
/// Chart points are downsampled for display; underlying CSV/Excel Date sheet keeps every sample.
/// </summary>
public static class SessionPlotRenderer
{
    /// <summary>Max points drawn per series (full duration, uniformly decimated).</summary>
    public const int MaxPlotPoints = 16_000;

    /// <summary>Default size for per-channel Excel charts (readable standalone).</summary>
    public const int PerChannelWidth = 1600;
    public const int PerChannelHeight = 800;

    /// <summary>Fractional padding around data extents (5%).</summary>
    public const double AxisPadFraction = 0.05;

    private static readonly ScottPlot.Color[] Palette =
    {
        ScottPlot.Colors.SteelBlue, ScottPlot.Colors.Orange, ScottPlot.Colors.SeaGreen,
        ScottPlot.Colors.Crimson, ScottPlot.Colors.Purple, ScottPlot.Colors.Teal,
        ScottPlot.Colors.DarkGoldenRod, ScottPlot.Colors.DodgerBlue,
        ScottPlot.Colors.Chocolate, ScottPlot.Colors.MediumSeaGreen, ScottPlot.Colors.Indigo,
        ScottPlot.Colors.DarkOrange, ScottPlot.Colors.SlateBlue, ScottPlot.Colors.DarkCyan,
        ScottPlot.Colors.Maroon, ScottPlot.Colors.OliveDrab
    };

    public static string SavePng(OfflineSession session, string path, int width = 1400, int height = 750)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var plot = BuildPlot(session);
        plot.SavePng(path, width, height);
        return path;
    }

    public static string SaveJpeg(OfflineSession session, string path, int width = 1000, int height = 560, int quality = 90)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var plot = BuildPlot(session);
        plot.SaveJpeg(path, width, height, quality);
        return path;
    }

    /// <summary>
    /// Overview PNGs grouped by engineering unit so force (N) and pressure (bar) never share a Y scale.
    /// When all active channels share one unit (or have no unit), returns a single combined plot.
    /// </summary>
    public static IReadOnlyList<(string UnitKey, string Title, string Path)> SaveUnitGroupedPngs(
        OfflineSession session,
        string tempDirectory,
        int width = 1400,
        int height = 750)
    {
        Directory.CreateDirectory(tempDirectory);
        var groups = GroupActiveChannelsByUnit(session);
        var results = new List<(string, string, string)>();

        if (groups.Count == 0)
        {
            var emptyPath = Path.Combine(tempDirectory, $"overview_{Guid.NewGuid():N}.png");
            var empty = BuildPlot(session);
            empty.SavePng(emptyPath, width, height);
            results.Add(("", "Grafic măsurătoare", emptyPath));
            return results;
        }

        foreach (var (unitKey, indices) in groups)
        {
            var path = Path.Combine(tempDirectory, $"overview_{SanitizeFileToken(unitKey)}_{Guid.NewGuid():N}.png");
            var plot = BuildPlotForChannels(session, indices, unitKey);
            plot.SavePng(path, width, height);
            var title = string.IsNullOrEmpty(unitKey)
                ? $"Canale fără unitate ({indices.Count})"
                : $"Canale [{unitKey}] ({indices.Count})";
            results.Add((unitKey, title, path));
        }

        return results;
    }

    /// <summary>One PNG per active channel (full time range, decimated for display), auto-scaled to that channel only.</summary>
    public static IReadOnlyList<(string Name, string Path)> SavePerChannelPngs(
        OfflineSession session,
        string tempDirectory,
        int width = PerChannelWidth,
        int height = PerChannelHeight)
    {
        Directory.CreateDirectory(tempDirectory);
        var results = new List<(string, string)>();
        for (var c = 0; c < session.Columns.Count; c++)
        {
            var col = session.Columns[c];
            if (!ExportLabels.IsActiveColumn(col)) continue;

            var name = c < session.ChannelNames.Count ? session.ChannelNames[c] : $"CH{c + 1}";
            var path = Path.Combine(tempDirectory, $"ch_{c}_{Guid.NewGuid():N}.png");
            var plot = BuildSingleChannelPlot(session, c, name);
            plot.SavePng(path, width, height);
            results.Add((name, path));
        }
        return results;
    }

    /// <summary>Sibling plot path: report.xlsx → report_plot.png</summary>
    public static string SiblingPlotPath(string exportPath, string extension = ".png")
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(exportPath)) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(exportPath);
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return Path.Combine(dir, stem + "_plot" + ext);
    }

    public static string? TrySaveSiblingPng(OfflineSession session, string exportPath)
    {
        try
        {
            var png = SiblingPlotPath(exportPath, ".png");
            SavePng(session, png);
            return png;
        }
        catch
        {
            return null;
        }
    }

    public static ScottPlot.Plot BuildPlot(OfflineSession session)
    {
        var indices = new List<int>();
        for (var c = 0; c < session.Columns.Count; c++)
        {
            if (ExportLabels.IsActiveColumn(session.Columns[c]))
                indices.Add(c);
        }
        return BuildPlotForChannels(session, indices, unitKeyHint: null);
    }

    public static IReadOnlyList<string> GetActiveChannelNames(OfflineSession session)
    {
        var names = new List<string>();
        for (var c = 0; c < session.Columns.Count; c++)
        {
            if (!ExportLabels.IsActiveColumn(session.Columns[c])) continue;
            names.Add(c < session.ChannelNames.Count ? session.ChannelNames[c] : $"CH{c + 1}");
        }
        return names;
    }

    private static void ApplyEngineeringStyle(ScottPlot.Plot plot, string title, string xLabel, string yLabel)
    {
        plot.Title(title);
        plot.Axes.Bottom.Label.Text = xLabel;
        plot.Axes.Left.Label.Text = yLabel;
        plot.Axes.Bottom.Label.FontSize = 12;
        plot.Axes.Left.Label.FontSize = 12;
        plot.Axes.Title.Label.FontSize = 13;
        plot.Axes.Bottom.TickLabelStyle.FontSize = 11;
        plot.Axes.Left.TickLabelStyle.FontSize = 11;
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#C5CED8");
        plot.Grid.MinorLineColor = ScottPlot.Color.FromHex("#E8EEF4");
        plot.FigureBackground.Color = ScottPlot.Colors.White;
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#FBFCFD");
        plot.Legend.FontSize = 11;
        plot.Legend.OutlineWidth = 1;
        plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#F7FAFC");
    }

    public static ScottPlot.Plot BuildSingleChannelPlot(OfflineSession session, int channelIndex, string legendName)
    {
        var plot = new ScottPlot.Plot();
        var n = session.Timestamps.Count;
        var unit = ExportLabels.ParseUnit(legendName);
        var useIndex = !HasMeaningfulTimeAxis(session);
        var yLabel = string.IsNullOrWhiteSpace(unit) ? "Y" : $"Y [{unit}]";
        var title = string.IsNullOrWhiteSpace(unit)
            ? $"{legendName} · {n:N0} eșantioane"
            : $"{legendName} · {n:N0} eșantioane";
        ApplyEngineeringStyle(
            plot,
            title,
            useIndex ? "index eșantion" : "t [s]",
            yLabel);
        plot.Legend.IsVisible = false;

        if (channelIndex < 0 || channelIndex >= session.Columns.Count || n == 0)
        {
            plot.Add.Text("Fără date", 0.5, 0.5);
            return plot;
        }

        var stride = Math.Max(1, n / MaxPlotPoints);
        var xs = useIndex
            ? BuildIndexAxis(n, stride)
            : BuildTimeAxis(session, session.Timestamps[0], stride);
        var ys = SampleColumn(session.Columns[channelIndex], stride);
        var sig = plot.Add.Scatter(xs, ys);
        sig.LegendText = legendName;
        sig.Color = Palette[channelIndex % Palette.Length];
        sig.MarkerSize = 0;
        sig.LineWidth = 1.75f;

        // Scale strictly to this channel's data (never a shared force/pressure range).
        ApplyDataAwareLimits(plot, xs, ys);
        return plot;
    }

    /// <summary>
    /// Sets X/Y limits from the plotted series only, with ~5% padding.
    /// Constant / near-zero signals get a readable absolute pad (avoids flat ±huge shared scales).
    /// </summary>
    public static void ApplyDataAwareLimits(ScottPlot.Plot plot, double[] xs, double[] ys, double padFraction = AxisPadFraction)
    {
        if (xs.Length == 0 || ys.Length == 0)
        {
            plot.Axes.AutoScale();
            return;
        }

        var xMin = double.PositiveInfinity;
        var xMax = double.NegativeInfinity;
        var yMin = double.PositiveInfinity;
        var yMax = double.NegativeInfinity;
        var len = Math.Min(xs.Length, ys.Length);
        for (var i = 0; i < len; i++)
        {
            var x = xs[i];
            var y = ys[i];
            if (double.IsNaN(x) || double.IsInfinity(x) || double.IsNaN(y) || double.IsInfinity(y))
                continue;
            if (x < xMin) xMin = x;
            if (x > xMax) xMax = x;
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
        }

        if (double.IsInfinity(xMin) || double.IsInfinity(yMin))
        {
            plot.Axes.AutoScale();
            return;
        }

        var (xLo, xHi) = PadRange(xMin, xMax, padFraction, absoluteFloor: 1e-6);
        var (yLo, yHi) = PadRange(yMin, yMax, padFraction, absoluteFloor: 1e-6);
        plot.Axes.SetLimits(xLo, xHi, yLo, yHi);
    }

    /// <summary>Pad [min,max] by fraction of span; constant signals get pad from |value| or absoluteFloor.</summary>
    public static (double Lo, double Hi) PadRange(double min, double max, double padFraction, double absoluteFloor)
    {
        if (double.IsNaN(min) || double.IsNaN(max) || double.IsInfinity(min) || double.IsInfinity(max))
            return (0, 1);

        if (max < min) (min, max) = (max, min);

        var span = max - min;
        if (span <= Math.Max(absoluteFloor * 1e-3, Math.Abs(min) * 1e-12))
        {
            // Constant / near-constant: pad relative to magnitude, never collapse to a hairline.
            var center = 0.5 * (min + max);
            var abs = Math.Max(Math.Abs(center), absoluteFloor);
            var pad = Math.Max(abs * padFraction, absoluteFloor);
            // Near-zero constants → symmetric ±pad for readability.
            if (Math.Abs(center) <= absoluteFloor)
                return (-pad, pad);
            return (center - pad, center + pad);
        }

        var edge = span * padFraction;
        return (min - edge, max + edge);
    }

    private static ScottPlot.Plot BuildPlotForChannels(
        OfflineSession session,
        IReadOnlyList<int> channelIndices,
        string? unitKeyHint)
    {
        var plot = new ScottPlot.Plot();
        var n = session.Timestamps.Count;
        var names = channelIndices
            .Select(c => c < session.ChannelNames.Count ? session.ChannelNames[c] : $"CH{c + 1}")
            .ToList();
        var durationSec = n >= 2
            ? (session.Timestamps[^1] - session.Timestamps[0]).TotalSeconds
            : 0;

        var unitLabel = unitKeyHint;
        if (unitLabel is null)
        {
            var units = names.Select(ExportLabels.ParseUnit)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            unitLabel = units.Count == 1 ? units[0]! : null;
        }

        var yLabel = string.IsNullOrWhiteSpace(unitLabel)
            ? (names.Count > 0 && names.Any(nm => !string.IsNullOrWhiteSpace(ExportLabels.ParseUnit(nm)))
                ? ExportLabels.BuildYAxisLabel(names)
                : "Y")
            : $"Y [{unitLabel}]";

        // Never claim a single Y scale when units are mixed inside this plot.
        if (yLabel.Contains("mixte", StringComparison.OrdinalIgnoreCase) && channelIndices.Count > 1)
            yLabel = "Y (vezi legenda — unități diferite; preferați foile per canal)";

        var title = n > MaxPlotPoints
            ? $"Grafic · {n:N0} eșantioane · {durationSec:0.##} s (decimat)"
            : $"Grafic · {n:N0} eșantioane · {durationSec:0.##} s";
        if (!string.IsNullOrWhiteSpace(unitLabel))
            title += $" · [{unitLabel}]";

        var useIndex = !HasMeaningfulTimeAxis(session);
        ApplyEngineeringStyle(plot, title, useIndex ? "index eșantion" : "t [s]", yLabel);
        plot.Legend.IsVisible = channelIndices.Count > 1;

        if (n == 0 || channelIndices.Count == 0)
        {
            plot.Add.Text(n == 0 ? "Fără date" : "Fără canale active (On)", 0.5, 0.5);
            return plot;
        }

        var stride = Math.Max(1, n / MaxPlotPoints);
        var xs = useIndex
            ? BuildIndexAxis(n, stride)
            : BuildTimeAxis(session, session.Timestamps[0], stride);

        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        var colorIndex = 0;
        foreach (var c in channelIndices)
        {
            var ys = SampleColumn(session.Columns[c], stride);
            AccumulateYExtents(ys, ref yMin, ref yMax);

            var sig = plot.Add.Scatter(xs, ys);
            sig.LegendText = c < session.ChannelNames.Count ? session.ChannelNames[c] : $"CH{c + 1}";
            sig.Color = Palette[colorIndex % Palette.Length];
            sig.MarkerSize = 0;
            sig.LineWidth = 1.5f;
            colorIndex++;
        }

        if (!double.IsInfinity(yMin))
        {
            var (xLo, xHi) = PadRange(
                xs.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).DefaultIfEmpty(0).Min(),
                xs.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).DefaultIfEmpty(1).Max(),
                AxisPadFraction,
                absoluteFloor: 1e-6);
            var (yLo, yHi) = PadRange(yMin, yMax, AxisPadFraction, absoluteFloor: 1e-6);
            plot.Axes.SetLimits(xLo, xHi, yLo, yHi);
        }
        else
        {
            plot.Axes.AutoScale();
        }

        return plot;
    }

    private static List<(string UnitKey, List<int> Indices)> GroupActiveChannelsByUnit(OfflineSession session)
    {
        var map = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var c = 0; c < session.Columns.Count; c++)
        {
            if (!ExportLabels.IsActiveColumn(session.Columns[c])) continue;
            var name = c < session.ChannelNames.Count ? session.ChannelNames[c] : $"CH{c + 1}";
            var unit = ExportLabels.ParseUnit(name) ?? "";
            if (!map.TryGetValue(unit, out var list))
            {
                list = new List<int>();
                map[unit] = list;
            }
            list.Add(c);
        }

        // Stable order: named units first (alphabetical), then empty unit.
        return map
            .OrderBy(kv => string.IsNullOrEmpty(kv.Key) ? 1 : 0)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => (kv.Key, kv.Value))
            .ToList();
    }

    private static void AccumulateYExtents(double[] ys, ref double yMin, ref double yMax)
    {
        foreach (var y in ys)
        {
            if (double.IsNaN(y) || double.IsInfinity(y)) continue;
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
        }
    }

    private static string SanitizeFileToken(string unitKey)
    {
        if (string.IsNullOrWhiteSpace(unitKey)) return "none";
        var chars = unitKey.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
        var s = new string(chars);
        return s.Length > 24 ? s[..24] : s;
    }

    private static bool HasMeaningfulTimeAxis(OfflineSession session)
    {
        var n = session.Timestamps.Count;
        if (n < 2) return false;
        return (session.Timestamps[^1] - session.Timestamps[0]).TotalSeconds > 1e-9;
    }

    private static double[] BuildTimeAxis(OfflineSession session, DateTime t0, int stride)
    {
        var n = session.Timestamps.Count;
        var len = (n + stride - 1) / stride;
        var xs = new double[len];
        for (var i = 0; i < len; i++)
        {
            var idx = Math.Min(n - 1, i * stride);
            xs[i] = (session.Timestamps[idx] - t0).TotalSeconds;
        }
        return xs;
    }

    private static double[] BuildIndexAxis(int n, int stride)
    {
        var len = (n + stride - 1) / stride;
        var xs = new double[len];
        for (var i = 0; i < len; i++)
            xs[i] = Math.Min(n - 1, i * stride);
        return xs;
    }

    private static double[] SampleColumn(double[] col, int stride)
    {
        var len = (col.Length + stride - 1) / stride;
        var ys = new double[len];
        for (var i = 0; i < len; i++)
        {
            var idx = Math.Min(col.Length - 1, i * stride);
            ys[i] = idx < col.Length ? col[idx] : double.NaN;
        }
        return ys;
    }
}
