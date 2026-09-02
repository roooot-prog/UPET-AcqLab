using System.Globalization;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Detailed multi-series deformation curves for Contur exports:
/// u₁…uₙ vs time / stroke / force, optional mean u_radial, Fmax marker.
/// </summary>
public static class CylinderDeformationCurveRenderer
{
    public const int DefaultWidth = 1600;
    public const int DefaultHeight = 900;
    public const int MaxPlotPoints = 16_000;

    private static readonly ScottPlot.Color[] Palette =
    {
        ScottPlot.Color.FromHex("#1E6BB8"),
        ScottPlot.Color.FromHex("#C45C26"),
        ScottPlot.Color.FromHex("#2E7D32"),
        ScottPlot.Color.FromHex("#7B1FA2"),
        ScottPlot.Color.FromHex("#00838F"),
        ScottPlot.Color.FromHex("#C62828"),
        ScottPlot.Color.FromHex("#F9A825"),
        ScottPlot.Color.FromHex("#455A64"),
    };

    /// <summary>Same S# colors as Contur schema markers (0-based sensor order).</summary>
    public static ScottPlot.Color SensorSeriesColor(int zeroBasedIndex)
        => Palette[Math.Abs(zeroBasedIndex) % Palette.Length];

    private static readonly ScottPlot.Color MarkerLine = ScottPlot.Color.FromHex("#B71C1C");
    private static readonly ScottPlot.Color MeanColor = ScottPlot.Color.FromHex("#1B2430");

    /// <summary>Saved deformation curve PNGs for Excel / HTML / sibling folder.</summary>
    public sealed class CurvePack
    {
        public string? VsTimePng { get; init; }
        public string? VsStrokePng { get; init; }
        public string? VsForcePng { get; init; }
        public string? MeanVsStrokePng { get; init; }
        public string? MeanVsForcePng { get; init; }

        public IReadOnlyList<(string Title, string Path)> AllPngs
        {
            get
            {
                var list = new List<(string, string)>(5);
                if (!string.IsNullOrWhiteSpace(VsTimePng) && File.Exists(VsTimePng))
                    list.Add(("u₁…uₙ vs timp / index", VsTimePng));
                if (!string.IsNullOrWhiteSpace(VsStrokePng) && File.Exists(VsStrokePng))
                    list.Add(("u₁…uₙ vs cursă presa", VsStrokePng));
                if (!string.IsNullOrWhiteSpace(VsForcePng) && File.Exists(VsForcePng))
                    list.Add(("u₁…uₙ vs forță", VsForcePng));
                if (!string.IsNullOrWhiteSpace(MeanVsStrokePng) && File.Exists(MeanVsStrokePng))
                    list.Add(("u_med vs cursă", MeanVsStrokePng));
                if (!string.IsNullOrWhiteSpace(MeanVsForcePng) && File.Exists(MeanVsForcePng))
                    list.Add(("u_med vs forță", MeanVsForcePng));
                return list;
            }
        }
    }

    /// <summary>
    /// Build PNG set next to Contur when config + result are valid.
    /// Returns null when sensors cannot be resolved.
    /// </summary>
    public static CurvePack? TryBuildPngs(
        OfflineSession session,
        ProjectMeta meta,
        CylinderContourResult result,
        string outDir,
        int width = DefaultWidth,
        int height = DefaultHeight)
    {
        if (session is null || result is null || !result.IsValid || result.Sensors.Count == 0)
            return null;

        CylinderContourExport.EnsureConfig(meta, session.Columns.Count, session);
        var cfg = meta.CylinderContour;
        if (cfg is null || !cfg.IsConfigured)
            return null;

        Directory.CreateDirectory(outDir);
        string? vsTime = null, vsStroke = null, vsForce = null, meanStroke = null, meanForce = null;

        try
        {
            var p = Path.Combine(outDir, "u_vs_time.png");
            BuildVsTimeOrIndex(session, cfg, result).SavePng(p, width, height);
            if (File.Exists(p) && new FileInfo(p).Length > 0) vsTime = p;
        }
        catch { /* optional panel */ }

        IReadOnlyList<double>? strokeCol = null;
        if (cfg.StrokeChannelIndex >= 0 && cfg.StrokeChannelIndex < session.Columns.Count)
            strokeCol = session.Columns[cfg.StrokeChannelIndex];

        IReadOnlyList<double>? forceCol = null;
        if (cfg.ForceChannelIndex is int fi && fi >= 0 && fi < session.Columns.Count)
            forceCol = session.Columns[fi];

        if (strokeCol is not null)
        {
            try
            {
                var p = Path.Combine(outDir, "u_vs_stroke.png");
                BuildVsDriver(session, cfg, result, strokeCol, "Cursă presa [mm]",
                    "Deformație radială u_i vs cursă presa").SavePng(p, width, height);
                if (File.Exists(p) && new FileInfo(p).Length > 0) vsStroke = p;
            }
            catch { /* optional */ }

            try
            {
                var p = Path.Combine(outDir, "umean_vs_stroke.png");
                BuildMeanVsDriver(session, cfg, result, strokeCol, "Cursă presa [mm]",
                    "u_med (media u_i) vs cursă presa").SavePng(p, width, height);
                if (File.Exists(p) && new FileInfo(p).Length > 0) meanStroke = p;
            }
            catch { /* optional */ }
        }

        if (forceCol is not null)
        {
            try
            {
                var p = Path.Combine(outDir, "u_vs_force.png");
                BuildVsDriver(session, cfg, result, forceCol, "Forță [N]",
                    "Deformație radială u_i vs forță").SavePng(p, width, height);
                if (File.Exists(p) && new FileInfo(p).Length > 0) vsForce = p;
            }
            catch { /* optional */ }

            try
            {
                var p = Path.Combine(outDir, "umean_vs_force.png");
                BuildMeanVsDriver(session, cfg, result, forceCol, "Forță [N]",
                    "u_med (media u_i) vs forță").SavePng(p, width, height);
                if (File.Exists(p) && new FileInfo(p).Length > 0) meanForce = p;
            }
            catch { /* optional */ }
        }

        var pack = new CurvePack
        {
            VsTimePng = vsTime,
            VsStrokePng = vsStroke,
            VsForcePng = vsForce,
            MeanVsStrokePng = meanStroke,
            MeanVsForcePng = meanForce
        };
        return pack.AllPngs.Count > 0 ? pack : null;
    }

    public static ScottPlot.Plot BuildVsTimeOrIndex(
        OfflineSession session,
        CylinderContourConfig cfg,
        CylinderContourResult result)
    {
        var plot = new ScottPlot.Plot();
        var n = session.Timestamps.Count;
        var useIndex = !HasMeaningfulTime(session);
        ApplyStyle(plot,
            "Deformație radială u_i vs " + (useIndex ? "index eșantion" : "timp"),
            useIndex ? "index eșantion" : "t [s]",
            "u_i [mm]");

        if (n == 0 || result.Sensors.Count == 0)
        {
            plot.Add.Text("Fără date", 0.5, 0.5);
            return plot;
        }

        var stride = Math.Max(1, n / MaxPlotPoints);
        var xs = useIndex ? BuildIndexAxis(n, stride) : BuildTimeAxis(session, stride);
        AddSensorSeries(plot, session, cfg, result, xs, stride, xAtMarker: MarkerX(session, result, useIndex));
        return plot;
    }

    public static ScottPlot.Plot BuildVsDriver(
        OfflineSession session,
        CylinderContourConfig cfg,
        CylinderContourResult result,
        IReadOnlyList<double> driverCol,
        string xLabel,
        string title)
    {
        var plot = new ScottPlot.Plot();
        ApplyStyle(plot, title, xLabel, "u_i [mm]");
        var n = Math.Min(session.Timestamps.Count, driverCol.Count);
        if (n == 0 || result.Sensors.Count == 0)
        {
            plot.Add.Text("Fără date", 0.5, 0.5);
            return plot;
        }

        var stride = Math.Max(1, n / MaxPlotPoints);
        var xs = SampleColumn(driverCol, n, stride);
        AddSensorSeries(plot, session, cfg, result, xs, stride,
            xAtMarker: MarkerDriverX(driverCol, result.SampleIndex, n));
        return plot;
    }

    public static ScottPlot.Plot BuildMeanVsDriver(
        OfflineSession session,
        CylinderContourConfig cfg,
        CylinderContourResult result,
        IReadOnlyList<double> driverCol,
        string xLabel,
        string title)
    {
        var plot = new ScottPlot.Plot();
        ApplyStyle(plot, title, xLabel, "u_med [mm]");
        var n = Math.Min(session.Timestamps.Count, driverCol.Count);
        if (n == 0 || result.Sensors.Count == 0)
        {
            plot.Add.Text("Fără date", 0.5, 0.5);
            return plot;
        }

        var stride = Math.Max(1, n / MaxPlotPoints);
        var xs = SampleColumn(driverCol, n, stride);
        var mean = BuildMeanRadial(session, cfg, n, stride);
        var sig = plot.Add.Scatter(xs, mean);
        sig.LegendText = "u_med";
        sig.Color = MeanColor;
        sig.MarkerSize = 0;
        sig.LineWidth = 2.4f;

        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        Accumulate(mean, ref yMin, ref yMax);
        SessionPlotRenderer.ApplyDataAwareLimits(plot, xs, mean);
        if (double.IsInfinity(yMin)) { yMin = 0; yMax = 1; }
        MarkVertical(plot, MarkerDriverX(driverCol, result.SampleIndex, n), result, yMin, yMax);
        return plot;
    }

    /// <summary>
    /// Scale factor so max|u| appears as ~targetFraction of R₀ on the spatial plot.
    /// Minimum 1 (true scale). Hard-capped by <paramref name="maxFactor"/> (industrial schematics).
    /// </summary>
    public static double ComputeExaggerationFactor(
        double r0Mm,
        IEnumerable<double> radialDisplacementsMm,
        double targetFractionOfR0 = 0.20,
        double maxFactor = 50.0)
    {
        if (r0Mm <= 0 || targetFractionOfR0 <= 0)
            return 1.0;
        var maxAbs = 0.0;
        foreach (var u in radialDisplacementsMm)
        {
            if (double.IsNaN(u) || double.IsInfinity(u)) continue;
            var a = Math.Abs(u);
            if (a > maxAbs) maxAbs = a;
        }
        if (maxAbs < 1e-12)
            return 1.0;
        var k = (targetFractionOfR0 * r0Mm) / maxAbs;
        if (maxFactor > 1.0 && k > maxFactor)
            k = maxFactor;
        // Only exaggerate when visually meaningful (> ~8%).
        if (k < 1.08)
            return 1.0;
        if (k >= 20)
            return Math.Round(k);
        if (k >= 10)
            return Math.Round(k);
        return Math.Round(k, 1);
    }

    private static void AddSensorSeries(
        ScottPlot.Plot plot,
        OfflineSession session,
        CylinderContourConfig cfg,
        CylinderContourResult result,
        double[] xs,
        int stride,
        double xAtMarker)
    {
        var n = session.Timestamps.Count;
        double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
        for (var i = 0; i < result.Sensors.Count; i++)
        {
            var s = result.Sensors[i];
            var ch = s.ChannelIndex;
            if (ch < 0 || ch >= session.Columns.Count) continue;
            var ys = SampleColumn(session.Columns[ch], n, stride);
            Accumulate(ys, ref yMin, ref yMax);
            var sig = plot.Add.Scatter(xs, ys);
            sig.LegendText = $"u{s.SensorIndex} (CH{ch}, {s.AngleDeg.ToString("0.#", CultureInfo.InvariantCulture)} deg)";
            sig.Color = Palette[i % Palette.Length];
            sig.MarkerSize = 0;
            sig.LineWidth = 1.85f;
        }

        if (!double.IsInfinity(yMin))
        {
            SessionPlotRenderer.ApplyDataAwareLimits(plot, xs,
                new[] { yMin, yMax });
        }
        else
        {
            plot.Axes.AutoScale();
            yMin = 0;
            yMax = 1;
        }

        MarkVertical(plot, xAtMarker, result, yMin, yMax);
    }

    private static void MarkVertical(
        ScottPlot.Plot plot,
        double x,
        CylinderContourResult result,
        double yMin = double.NaN,
        double yMax = double.NaN)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return;
        var inv = CultureInfo.InvariantCulture;
        var label = result.IndexRule == ContourIndexRule.ForceAbsMax
            ? $"Fmax (idx={result.SampleIndex})"
            : result.IndexRule == ContourIndexRule.StrokeAbsMax
                ? $"index = max cursa (idx={result.SampleIndex})"
                : $"Contur idx={result.SampleIndex}";
        if (result.ForceAtIndex is double f)
            label += $"  F={f.ToString("0.#", inv)}";
        if (result.StrokeAtIndex is double st)
            label += $"  cursa={st.ToString("0.###", inv)}";

        var line = plot.Add.VerticalLine(x);
        line.Color = MarkerLine;
        line.LineWidth = 3.6f;
        line.LinePattern = ScottPlot.LinePattern.Solid;
        line.LegendText = label;

        // Annotate uᵢ at Fmax — visible callout next to the marker
        var uLines = new List<string> { "u_i la Fmax:" };
        foreach (var s in result.Sensors)
        {
            uLines.Add(
                $"u{s.SensorIndex}={s.RadialDisplacementMm.ToString("0.####", inv)} mm");
        }
        if (double.IsFinite(result.UMeanMm))
            uLines.Add($"u_med={result.UMeanMm.ToString("0.####", inv)}");

        double annY;
        if (double.IsFinite(yMin) && double.IsFinite(yMax) && yMax > yMin)
            annY = yMax - (yMax - yMin) * 0.04;
        else
            annY = double.IsFinite(result.UMaxMm) ? result.UMaxMm : 0;

        // Nudge text slightly right of the marker so it stays readable.
        var xSpanGuess = Math.Abs(x) > 1e-9 ? Math.Abs(x) * 0.02 : 0.5;
        var ann = plot.Add.Text(string.Join("\n", uLines), x + xSpanGuess, annY);
        ann.LabelFontName = "Segoe UI";
        ann.LabelFontColor = MarkerLine;
        ann.LabelFontSize = 11;
        ann.LabelBold = true;
        ann.LabelAlignment = ScottPlot.Alignment.UpperLeft;
        ann.LabelBackgroundColor = ScottPlot.Color.FromHex("#FFEBEE");
        ann.LabelBorderColor = MarkerLine;
        ann.LabelBorderWidth = 1.5f;
        ann.LabelPadding = 5;
    }

    private static double[] BuildMeanRadial(
        OfflineSession session,
        CylinderContourConfig cfg,
        int n,
        int stride)
    {
        var len = (n + stride - 1) / stride;
        var mean = new double[len];
        var channels = new List<int>();
        for (var i = 0; i < cfg.SensorCount; i++)
        {
            var ch = i < cfg.SensorChannelIndices.Count ? cfg.SensorChannelIndices[i] : -1;
            if (ch >= 0 && ch < session.Columns.Count)
                channels.Add(ch);
        }

        for (var i = 0; i < len; i++)
        {
            var idx = Math.Min(n - 1, i * stride);
            double sum = 0;
            var k = 0;
            foreach (var ch in channels)
            {
                if (idx >= session.Columns[ch].Length) continue;
                var v = session.Columns[ch][idx];
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                sum += v;
                k++;
            }
            mean[i] = k > 0 ? sum / k : double.NaN;
        }
        return mean;
    }

    private static double MarkerX(OfflineSession session, CylinderContourResult result, bool useIndex)
    {
        var idx = Math.Clamp(result.SampleIndex, 0, Math.Max(0, session.Timestamps.Count - 1));
        if (useIndex || session.Timestamps.Count == 0)
            return idx;
        return (session.Timestamps[idx] - session.Timestamps[0]).TotalSeconds;
    }

    private static double MarkerDriverX(IReadOnlyList<double> driver, int sampleIndex, int n)
    {
        var idx = Math.Clamp(sampleIndex, 0, Math.Max(0, n - 1));
        if (idx >= driver.Count) return double.NaN;
        var v = driver[idx];
        return double.IsNaN(v) || double.IsInfinity(v) ? double.NaN : v;
    }

    private static void ApplyStyle(ScottPlot.Plot plot, string title, string xLabel, string yLabel)
    {
        plot.Title(title);
        plot.Axes.Bottom.Label.Text = xLabel;
        plot.Axes.Left.Label.Text = yLabel;
        const string font = "Segoe UI";
        plot.Axes.Bottom.Label.FontName = font;
        plot.Axes.Left.Label.FontName = font;
        plot.Axes.Title.Label.FontName = font;
        plot.Axes.Bottom.TickLabelStyle.FontName = font;
        plot.Axes.Left.TickLabelStyle.FontName = font;
        plot.Axes.Bottom.Label.FontSize = 13;
        plot.Axes.Left.Label.FontSize = 13;
        plot.Axes.Title.Label.FontSize = 14;
        plot.Axes.Title.Label.Bold = true;
        plot.Axes.Bottom.TickLabelStyle.FontSize = 11;
        plot.Axes.Left.TickLabelStyle.FontSize = 11;
        plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#C5CED8");
        plot.FigureBackground.Color = ScottPlot.Colors.White;
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#FBFCFD");
        plot.Legend.FontSize = 11;
        plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#F7FAFC");
        plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#1B2430");
        plot.Legend.IsVisible = true;
    }

    private static bool HasMeaningfulTime(OfflineSession session)
    {
        var n = session.Timestamps.Count;
        if (n < 2) return false;
        return (session.Timestamps[^1] - session.Timestamps[0]).TotalSeconds > 1e-9;
    }

    private static double[] BuildTimeAxis(OfflineSession session, int stride)
    {
        var n = session.Timestamps.Count;
        var t0 = session.Timestamps[0];
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

    private static double[] SampleColumn(IReadOnlyList<double> col, int n, int stride)
    {
        var len = (n + stride - 1) / stride;
        var ys = new double[len];
        for (var i = 0; i < len; i++)
        {
            var idx = Math.Min(n - 1, i * stride);
            ys[i] = idx < col.Count ? col[idx] : double.NaN;
        }
        return ys;
    }

    private static void Accumulate(double[] ys, ref double yMin, ref double yMax)
    {
        foreach (var y in ys)
        {
            if (double.IsNaN(y) || double.IsInfinity(y)) continue;
            if (y < yMin) yMin = y;
            if (y > yMax) yMax = y;
        }
    }
}
