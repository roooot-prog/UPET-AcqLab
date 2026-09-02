using Spider8DAQ.Core.Analysis;

namespace Spider8DAQ.Core.Export;

/// <summary>Renders CWT scalograms (time × frequency × |W|) as PNG/JPEG for Excel/PDF reports and live UI helpers.</summary>
public static class CwtPlotRenderer
{
    /// <summary>High-res report PNG (Full HD) — Sharp when Smooth + dense ScaleCount.</summary>
    public const int DefaultWidth = 1920;
    public const int DefaultHeight = 1080;

    /// <summary>
    /// One PNG per active channel. Uses session timestamps for sample rate when possible.
    /// Uses the full recording by default. Pass <paramref name="windowSeconds"/> &gt; 0 to limit
    /// to the last N seconds (legacy). Long records are then uniformly downsampled inside CWT
    /// (MaxSamples) so the time axis still spans the whole selected window.
    /// Per-channel failures are skipped (does not throw).
    /// </summary>
    public static IReadOnlyList<(string Name, string Path)> SavePerChannelPngs(
        OfflineSession session,
        string tempDirectory,
        double fallbackSampleRateHz = 50,
        double windowSeconds = 0,
        int width = DefaultWidth,
        int height = DefaultHeight,
        CwtOptions? options = null)
    {
        Directory.CreateDirectory(tempDirectory);
        var results = new List<(string, string)>();
        var fs = EstimateSampleRate(session, fallbackSampleRateHz);
        options ??= CwtOptions.ForExport();

        for (var c = 0; c < session.Columns.Count; c++)
        {
            try
            {
                var col = session.Columns[c];
                if (!ExportLabels.IsActiveColumn(col)) continue;
                var name = c < session.ChannelNames.Count ? session.ChannelNames[c] : $"CH{c + 1}";
                var slice = TakeWindow(col, fs, windowSeconds);
                var cwt = ContinuousWaveletTransform.Compute(slice, fs, options, name);
                if (cwt.AnalyzedSamples < 16) continue;

                var path = Path.Combine(tempDirectory, $"cwt_{c}_{Guid.NewGuid():N}.png");
                SavePng(cwt, path, width, height);
                results.Add((name, path));
            }
            catch
            {
                // One bad channel must not abort Excel / batch export.
            }
        }

        return results;
    }

    /// <summary>
    /// One JPEG per active channel for PDF reports (offline CWT, ScaleCount≈96).
    /// Full recording by default (<paramref name="windowSeconds"/> ≤ 0).
    /// Per-channel failures are skipped.
    /// </summary>
    public static IReadOnlyList<(string Name, string Path)> SavePerChannelJpegs(
        OfflineSession session,
        string tempDirectory,
        double fallbackSampleRateHz = 50,
        double windowSeconds = 0,
        int width = DefaultWidth,
        int height = DefaultHeight,
        int quality = 92,
        CwtOptions? options = null)
    {
        Directory.CreateDirectory(tempDirectory);
        var results = new List<(string, string)>();
        var fs = EstimateSampleRate(session, fallbackSampleRateHz);
        options ??= CwtOptions.ForExport();

        for (var c = 0; c < session.Columns.Count; c++)
        {
            try
            {
                var col = session.Columns[c];
                if (!ExportLabels.IsActiveColumn(col)) continue;
                var name = c < session.ChannelNames.Count ? session.ChannelNames[c] : $"CH{c + 1}";
                var slice = TakeWindow(col, fs, windowSeconds);
                var cwt = ContinuousWaveletTransform.Compute(slice, fs, options, name);
                if (cwt.AnalyzedSamples < 16) continue;

                var path = Path.Combine(tempDirectory, $"cwt_{c}_{Guid.NewGuid():N}.jpg");
                SaveJpeg(cwt, path, width, height, quality);
                if (File.Exists(path) && new FileInfo(path).Length > 0)
                    results.Add((name, path));
            }
            catch
            {
                // Skip channel — PDF still gets other channels + rest of report.
            }
        }

        return results;
    }

    public static string? SaveJpeg(
        OfflineSession session,
        int channelIndex,
        string path,
        double fallbackSampleRateHz = 50,
        double windowSeconds = 0,
        int width = DefaultWidth,
        int height = DefaultHeight,
        int quality = 95)
    {
        if (channelIndex < 0 || channelIndex >= session.Columns.Count) return null;
        var col = session.Columns[channelIndex];
        if (!ExportLabels.IsActiveColumn(col)) return null;
        var name = channelIndex < session.ChannelNames.Count
            ? session.ChannelNames[channelIndex]
            : $"CH{channelIndex + 1}";
        var fs = EstimateSampleRate(session, fallbackSampleRateHz);
        var slice = TakeWindow(col, fs, windowSeconds);
        var cwt = ContinuousWaveletTransform.Compute(slice, fs, CwtOptions.ForExport(), name);
        if (cwt.AnalyzedSamples < 16) return null;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        SaveJpeg(cwt, path, width, height, quality);
        return path;
    }

    public static void SavePng(CwtResult cwt, string path, int width = DefaultWidth, int height = DefaultHeight)
    {
        var plot = BuildPlot(cwt);
        plot.SavePng(path, width, height);
    }

    public static void SaveJpeg(CwtResult cwt, string path, int width = DefaultWidth, int height = DefaultHeight, int quality = 95)
    {
        var plot = BuildPlot(cwt);
        plot.SaveJpeg(path, width, height, quality);
    }

    public static ScottPlot.Plot BuildPlot(CwtResult cwt)
    {
        var plot = new ScottPlot.Plot();
        if (cwt.AnalyzedSamples < 2 || cwt.FrequenciesHz.Length == 0)
        {
            plot.Title("CWT - date insuficiente");
            return plot;
        }

        CwtHeatmapRendering.AddHeatmap(
            plot, cwt,
            CwtHeatmapRendering.ExportRowUpsample,
            CwtHeatmapRendering.ExportColUpsample);

        var ch = string.IsNullOrWhiteSpace(cwt.ChannelName) ? "" : $" - {cwt.ChannelName}";
        plot.Title($"Analiză timp-frecvență (CWT{ch}, {cwt.WaveletName})");
        plot.Axes.Bottom.Label.Text = "Timp [s]";
        plot.Axes.Left.Label.Text = "Frecvență [Hz]";
        plot.Font.Set("Segoe UI");
        return plot;
    }

    public static double EstimateSampleRate(OfflineSession session, double fallback)
        => SamplingRateInfo.EstimateEffectiveHz(session.Timestamps, fallback);

    /// <summary>
    /// <paramref name="windowSeconds"/> ≤ 0 → entire column (full recording).
    /// &gt; 0 → last N seconds only.
    /// </summary>
    private static double[] TakeWindow(double[] col, double fs, double windowSeconds)
    {
        if (col.Length == 0) return col;
        if (windowSeconds <= 0)
            return col;

        var maxN = Math.Max(16, (int)Math.Ceiling(Math.Max(0.5, windowSeconds) * fs));
        if (col.Length <= maxN) return col;
        var start = col.Length - maxN;
        var slice = new double[maxN];
        Array.Copy(col, start, slice, 0, maxN);
        return slice;
    }
}
