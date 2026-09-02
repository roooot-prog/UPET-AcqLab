using System.Globalization;
using System.Text;

namespace Spider8DAQ.Core.Analysis;

/// <summary>One Matest graph point: force [kN] vs time [s].</summary>
public sealed class MatestSeriesPoint
{
    public double ForceKn { get; init; }
    public double TimeSec { get; init; }
}

/// <summary>Parsed Matest TXT ([GRAPH] tab-separated Sarcina[kN], Timp[sec]).</summary>
public sealed class MatestSeries
{
    public string SourcePath { get; init; } = "";
    public IReadOnlyList<MatestSeriesPoint> Points { get; init; } = Array.Empty<MatestSeriesPoint>();
    public double PeakForceKn { get; init; }
    public double PeakTimeSec { get; init; }
}

/// <summary>Compare Matest peak vs UPET peak at the same force level (nearest sample).</summary>
public sealed class MatestPeakCompareResult
{
    public double MatestPeakKn { get; init; }
    public double UpetPeakKn { get; init; }
    public double UpetAtMatestLevelKn { get; init; }
    public double DeltaPercent { get; init; }
    public string Summary { get; init; } = "";
}

/// <summary>
/// Parses Matest export TXT files (e.g. "TEST 1KN ….txt") with a [GRAPH] section:
/// tab-separated columns Sarcina[kN] and Timp[sec].
/// </summary>
public static class MatestTxtParser
{
    public static MatestSeries Parse(string path)
    {
        var text = File.ReadAllText(path, Encoding.UTF8);
        return ParseContent(text, path);
    }

    public static MatestSeries ParseContent(string content, string sourcePath = "")
    {
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var inGraph = false;
        var points = new List<MatestSeriesPoint>();
        var headerSeen = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("[GRAPH]", StringComparison.OrdinalIgnoreCase))
            {
                inGraph = true;
                headerSeen = false;
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']') && !line.StartsWith("[GRAPH]", StringComparison.OrdinalIgnoreCase))
            {
                inGraph = false;
                continue;
            }

            if (!inGraph) continue;

            if (!headerSeen)
            {
                if (line.Contains("Sarcina", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("Timp", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("kN", StringComparison.OrdinalIgnoreCase))
                {
                    headerSeen = true;
                    continue;
                }
            }

            var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
                parts = line.Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) continue;

            if (!TryParseDouble(parts[0], out var forceKn)) continue;
            if (!TryParseDouble(parts[1], out var timeSec)) continue;

            headerSeen = true;
            points.Add(new MatestSeriesPoint { ForceKn = forceKn, TimeSec = timeSec });
        }

        double peakKn = double.NaN, peakT = double.NaN;
        foreach (var p in points)
        {
            if (double.IsNaN(peakKn) || Math.Abs(p.ForceKn) > Math.Abs(peakKn))
            {
                peakKn = p.ForceKn;
                peakT = p.TimeSec;
            }
        }

        return new MatestSeries
        {
            SourcePath = sourcePath,
            Points = points,
            PeakForceKn = peakKn,
            PeakTimeSec = peakT
        };
    }

    /// <summary>
    /// Δ% = 100·(UpetAtMatestLevel − MatestPeak) / |MatestPeak|.
    /// UpetAtMatestLevel = UPET sample nearest in force to Matest peak (both in kN).
    /// </summary>
    public static MatestPeakCompareResult ComparePeaks(
        double matestPeakKn,
        IReadOnlyList<double> upetForceKn)
    {
        if (double.IsNaN(matestPeakKn) || upetForceKn.Count == 0)
        {
            return new MatestPeakCompareResult
            {
                MatestPeakKn = matestPeakKn,
                UpetPeakKn = double.NaN,
                UpetAtMatestLevelKn = double.NaN,
                DeltaPercent = double.NaN,
                Summary = "Comparare Matest/UPET: date insuficiente."
            };
        }

        double upetPeak = double.NaN;
        double nearest = double.NaN;
        var bestDist = double.PositiveInfinity;
        foreach (var v in upetForceKn)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            if (double.IsNaN(upetPeak) || Math.Abs(v) > Math.Abs(upetPeak))
                upetPeak = v;
            var d = Math.Abs(v - matestPeakKn);
            if (d < bestDist)
            {
                bestDist = d;
                nearest = v;
            }
        }

        var delta = Math.Abs(matestPeakKn) < 1e-15
            ? double.NaN
            : 100.0 * (nearest - matestPeakKn) / Math.Abs(matestPeakKn);

        var summary = double.IsNaN(delta)
            ? $"Matest peak={matestPeakKn:0.####} kN · UPET peak={upetPeak:0.####} kN"
            : $"Matest peak={matestPeakKn:0.####} kN · UPET@nivel={nearest:0.####} kN · Δ={delta:+0.##;-0.##;0}% · UPET peak={upetPeak:0.####} kN";

        return new MatestPeakCompareResult
        {
            MatestPeakKn = matestPeakKn,
            UpetPeakKn = upetPeak,
            UpetAtMatestLevelKn = nearest,
            DeltaPercent = delta,
            Summary = summary
        };
    }

    /// <summary>Convert UPET force column (N or kN) to kN for comparison.</summary>
    public static double[] ToForceKn(IReadOnlyList<double> values, string? unit)
    {
        var u = unit ?? "";
        double scale = u.Contains("kN", StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.001;

        var result = new double[values.Count];
        for (var i = 0; i < values.Count; i++)
            result[i] = values[i] * scale;
        return result;
    }

    private static bool TryParseDouble(string s, out double v)
    {
        s = s.Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}
