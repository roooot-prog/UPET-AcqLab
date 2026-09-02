using System.Globalization;
using System.Text;

namespace Spider8DAQ.Core.Acquisition;

/// <summary>Jurnal statistici pe interval (min/max/mean/RMS) în timpul înregistrării.</summary>
public sealed class StatisticsJournal
{
    private readonly List<string> _names;
    private readonly double[] _min;
    private readonly double[] _max;
    private readonly double[] _sum;
    private readonly double[] _sumSq;
    private readonly int[] _count;
    private readonly List<string> _lines = new();
    private DateTime _windowStart = DateTime.UtcNow;
    private int _windowIndex;

    public StatisticsJournal(IReadOnlyList<string> channelNames, double intervalSeconds = 1.0)
    {
        _names = channelNames.ToList();
        IntervalSeconds = Math.Max(0.1, intervalSeconds);
        var n = _names.Count;
        _min = new double[n];
        _max = new double[n];
        _sum = new double[n];
        _sumSq = new double[n];
        _count = new int[n];
        ResetWindow();
        _lines.Add("Window,StartUtc,EndUtc," + string.Join(",",
            _names.SelectMany(name => new[] { $"{name}_Min", $"{name}_Max", $"{name}_Mean", $"{name}_Rms" })));
    }

    public double IntervalSeconds { get; }
    public IReadOnlyList<string> Lines => _lines;
    public string? LastSummary { get; set; }

    public void Add(DateTime utc, IReadOnlyList<double> values)
    {
        var n = Math.Min(_names.Count, values.Count);
        for (var i = 0; i < n; i++)
        {
            var v = values[i];
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            if (_count[i] == 0)
            {
                _min[i] = v;
                _max[i] = v;
            }
            else
            {
                if (v < _min[i]) _min[i] = v;
                if (v > _max[i]) _max[i] = v;
            }
            _sum[i] += v;
            _sumSq[i] += v * v;
            _count[i]++;
        }

        if ((utc - _windowStart).TotalSeconds >= IntervalSeconds)
            FlushWindow(utc);
    }

    public void FlushFinal(DateTime utc)
    {
        if (_count.Any(c => c > 0))
            FlushWindow(utc);
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllLines(path, _lines, Encoding.UTF8);
    }

    private void FlushWindow(DateTime endUtc)
    {
        _windowIndex++;
        var sb = new StringBuilder();
        sb.Append(_windowIndex.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(_windowStart.ToString("O", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(endUtc.ToString("O", CultureInfo.InvariantCulture));

        var summaryParts = new List<string>();
        for (var i = 0; i < _names.Count; i++)
        {
            double min = double.NaN, max = double.NaN, mean = double.NaN, rms = double.NaN;
            if (_count[i] > 0)
            {
                min = _min[i];
                max = _max[i];
                mean = _sum[i] / _count[i];
                rms = Math.Sqrt(_sumSq[i] / _count[i]);
                summaryParts.Add($"{_names[i]}:[{min:0.###}/{mean:0.###}/{max:0.###}]");
            }
            sb.Append(',');
            sb.Append(Fmt(min));
            sb.Append(',');
            sb.Append(Fmt(max));
            sb.Append(',');
            sb.Append(Fmt(mean));
            sb.Append(',');
            sb.Append(Fmt(rms));
        }

        _lines.Add(sb.ToString());
        LastSummary = string.Join(" ", summaryParts.Take(4));
        ResetWindow();
        _windowStart = endUtc;
    }

    private void ResetWindow()
    {
        Array.Fill(_min, double.NaN);
        Array.Fill(_max, double.NaN);
        Array.Clear(_sum);
        Array.Clear(_sumSq);
        Array.Clear(_count);
    }

    private static string Fmt(double v) =>
        double.IsNaN(v) ? "" : v.ToString("G9", CultureInfo.InvariantCulture);
}

public sealed class ChannelPreflightResult
{
    public int ChannelIndex { get; init; }
    public string Name { get; init; } = "";
    public bool TareOk { get; init; }
    public bool ShuntOk { get; init; }
    public double ShuntReading { get; init; } = double.NaN;
    public string Detail { get; init; } = "";
    public bool Passed => TareOk && ShuntOk;
}
