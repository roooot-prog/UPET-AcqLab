using Spider8DAQ.Core.Analysis;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Shared scalogram → ScottPlot heatmap: frequency flip + optional bilinear densify + Smooth.
/// </summary>
public static class CwtHeatmapRendering
{
    /// <summary>Export / offline: densify before Smooth for print-sharp scalograms.</summary>
    public const int ExportRowUpsample = 3;
    public const int ExportColUpsample = 2;

    /// <summary>Live UI: lighter densify (Smooth still on) to keep ≥10 Hz.</summary>
    public const int LiveRowUpsample = 2;
    public const int LiveColUpsample = 2;

    public static double[,] BuildIntensities(CwtResult cwt, int rowUpsample = 1, int colUpsample = 1)
    {
        var rows = cwt.FrequenciesHz.Length;
        var cols = cwt.TimesSec.Length;
        if (rows == 0 || cols == 0)
            return new double[0, 0];

        // Flip so low frequency is at bottom (array row 0)
        var flipped = new double[rows, cols];
        for (var r = 0; r < rows; r++)
        {
            var src = rows - 1 - r;
            for (var c = 0; c < cols; c++)
                flipped[r, c] = cwt.Magnitudes[src, c];
        }

        var ru = Math.Max(1, rowUpsample);
        var cu = Math.Max(1, colUpsample);
        if (ru == 1 && cu == 1)
            return flipped;

        return UpsampleBilinear(flipped, rows * ru, cols * cu);
    }

    public static ScottPlot.Plottables.Heatmap AddHeatmap(
        ScottPlot.Plot plot,
        CwtResult cwt,
        int rowUpsample = 1,
        int colUpsample = 1)
    {
        var intensities = BuildIntensities(cwt, rowUpsample, colUpsample);
        var hm = plot.Add.Heatmap(intensities);
        hm.Smooth = true;

        var t0 = cwt.TimesSec[0];
        var t1 = cwt.TimesSec[^1];
        if (Math.Abs(t1 - t0) < 1e-12) t1 = t0 + 1e-3;
        var fLo = cwt.FrequenciesHz[0];
        var fHi = cwt.FrequenciesHz[^1];
        hm.Extent = new ScottPlot.CoordinateRect(t0, t1, fLo, fHi);
        try { hm.Colormap = new ScottPlot.Colormaps.Viridis(); } catch { /* default */ }
        plot.Axes.SetLimits(t0, t1, fLo, fHi);
        return hm;
    }

    /// <summary>Bilinear upsample of a 2D intensity grid (smooths blocky CWT cells).</summary>
    public static double[,] UpsampleBilinear(double[,] src, int outRows, int outCols)
    {
        var inRows = src.GetLength(0);
        var inCols = src.GetLength(1);
        if (inRows == 0 || inCols == 0 || outRows < 1 || outCols < 1)
            return new double[Math.Max(0, outRows), Math.Max(0, outCols)];

        var dst = new double[outRows, outCols];
        var yScale = inRows == 1 ? 0 : (inRows - 1) / (double)(outRows - 1);
        var xScale = inCols == 1 ? 0 : (inCols - 1) / (double)(outCols - 1);

        for (var r = 0; r < outRows; r++)
        {
            var y = r * yScale;
            var y0 = (int)Math.Floor(y);
            var y1 = Math.Min(inRows - 1, y0 + 1);
            var fy = y - y0;
            for (var c = 0; c < outCols; c++)
            {
                var x = c * xScale;
                var x0 = (int)Math.Floor(x);
                var x1 = Math.Min(inCols - 1, x0 + 1);
                var fx = x - x0;
                var v00 = src[y0, x0];
                var v10 = src[y0, x1];
                var v01 = src[y1, x0];
                var v11 = src[y1, x1];
                var v0 = v00 + (v10 - v00) * fx;
                var v1 = v01 + (v11 - v01) * fx;
                dst[r, c] = v0 + (v1 - v0) * fy;
            }
        }

        return dst;
    }
}
