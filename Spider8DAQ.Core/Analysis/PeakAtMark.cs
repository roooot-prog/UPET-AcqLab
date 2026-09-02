using System.Globalization;

namespace Spider8DAQ.Core.Analysis;

/// <summary>One MARK event with values sampled at (or nearest to) the mark timestamp.</summary>
public sealed class PeakAtMarkRow
{
    public DateTime MarkUtc { get; init; }
    public string Label { get; init; } = "";
    public int SampleIndex { get; init; }
    public double[] Values { get; init; } = Array.Empty<double>();
    public double[] PeakAbsInWindow { get; init; } = Array.Empty<double>();
}

/// <summary>
/// Extract MARK timestamps from recording CSV comment lines and resolve nearest-sample / peak-at-MARK stats.
/// CSV format: <c># MARK {ISO-UTC} {text}</c>
/// </summary>
public static class PeakAtMark
{
    public static IReadOnlyList<(DateTime Utc, string Label)> ParseMarksFromCsvLines(IEnumerable<string> lines)
    {
        var list = new List<(DateTime, string)>();
        foreach (var line in lines)
        {
            var t = line.TrimStart().TrimStart('\uFEFF');
            if (!t.StartsWith("# MARK ", StringComparison.OrdinalIgnoreCase)) continue;
            var rest = t[7..].Trim();
            var space = rest.IndexOf(' ');
            string iso, label;
            if (space > 0)
            {
                iso = rest[..space];
                label = rest[(space + 1)..].Trim();
            }
            else
            {
                iso = rest;
                label = "mark";
            }

            if (!DateTime.TryParse(iso, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind | DateTimeStyles.AllowWhiteSpaces, out var utc))
                continue;
            if (utc.Kind == DateTimeKind.Unspecified)
                utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            list.Add((utc.ToUniversalTime(), string.IsNullOrWhiteSpace(label) ? "mark" : label));
        }
        return list;
    }

    public static IReadOnlyList<PeakAtMarkRow> Compute(
        OfflineSession session,
        IReadOnlyList<(DateTime Utc, string Label)> marks,
        int windowSamples = 5)
    {
        if (session.Timestamps.Count == 0 || marks.Count == 0)
            return Array.Empty<PeakAtMarkRow>();

        var rows = new List<PeakAtMarkRow>(marks.Count);
        var chCount = session.Columns.Count;
        var win = Math.Max(0, windowSamples);

        foreach (var (utc, label) in marks)
        {
            var idx = NearestIndex(session.Timestamps, utc);
            var values = new double[chCount];
            var peaks = new double[chCount];
            for (var c = 0; c < chCount; c++)
            {
                var col = session.Columns[c];
                values[c] = idx < col.Length ? col[idx] : double.NaN;
                var a = Math.Max(0, idx - win);
                var b = Math.Min(col.Length - 1, idx + win);
                var peak = double.NaN;
                for (var i = a; i <= b; i++)
                {
                    var v = col[i];
                    if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                    if (double.IsNaN(peak) || Math.Abs(v) > Math.Abs(peak))
                        peak = v;
                }
                peaks[c] = peak;
            }

            rows.Add(new PeakAtMarkRow
            {
                MarkUtc = utc,
                Label = label,
                SampleIndex = idx,
                Values = values,
                PeakAbsInWindow = peaks
            });
        }

        return rows;
    }

    public static int NearestIndex(IReadOnlyList<DateTime> timestamps, DateTime utc)
    {
        if (timestamps.Count == 0) return 0;
        var target = utc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            : utc.ToUniversalTime();

        var best = 0;
        var bestDt = double.PositiveInfinity;
        for (var i = 0; i < timestamps.Count; i++)
        {
            var t = timestamps[i];
            var tu = t.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(t, DateTimeKind.Utc)
                : t.ToUniversalTime();
            var dt = Math.Abs((tu - target).TotalSeconds);
            if (dt < bestDt)
            {
                bestDt = dt;
                best = i;
            }
        }
        return best;
    }

    public static IEnumerable<string> FormatLines(
        IReadOnlyList<PeakAtMarkRow> rows,
        IReadOnlyList<string> channelNames)
    {
        foreach (var r in rows)
        {
            var parts = new List<string> { $"MARK@{r.MarkUtc:O}", r.Label, $"idx={r.SampleIndex}" };
            for (var c = 0; c < r.Values.Length; c++)
            {
                var name = c < channelNames.Count ? channelNames[c] : $"CH{c}";
                var v = r.Values[c];
                var p = c < r.PeakAbsInWindow.Length ? r.PeakAbsInWindow[c] : double.NaN;
                parts.Add($"{name}={Fmt(v)} (peak={Fmt(p)})");
            }
            yield return string.Join(" · ", parts);
        }
    }

    private static string Fmt(double v) =>
        double.IsNaN(v) ? "—" : v.ToString("G6", CultureInfo.InvariantCulture);
}
