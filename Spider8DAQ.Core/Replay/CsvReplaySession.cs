using System.Globalization;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Export;

namespace Spider8DAQ.Core.Replay;

public sealed class CsvReplaySession
{
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<SampleFrame> Frames { get; }
    public IReadOnlyList<double[]> Values { get; }

    private CsvReplaySession(IReadOnlyList<string> headers, IReadOnlyList<SampleFrame> frames, IReadOnlyList<double[]> values)
    {
        Headers = headers;
        Frames = frames;
        Values = values;
    }

    public static CsvReplaySession Load(string path)
    {
        var lines = CsvSharedIO.EnumerateDataLines(path).ToArray();
        if (lines.Length < 2)
            throw new InvalidDataException("CSV has no data rows.");

        var headers = lines[0].Split(',');
        // Layout A (legacy): Timestamp,Sequence,CH...
        // Layout B (3.3.19+): Timestamp,Sequence,t_s,CH...
        var hasT = headers.Length >= 3 && SamplingRateInfo.IsRelativeTimeHeader(headers[2]);
        var channelStart = hasT ? 3 : 2;
        var channelNames = headers.Skip(channelStart).ToList();
        var frames = new List<SampleFrame>();
        var values = new List<double[]>();

        for (var i = 1; i < lines.Length; i++)
        {
            var cols = lines[i].Split(',');
            if (cols.Length < 2) continue;

            DateTime.TryParse(cols[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts);
            long.TryParse(cols[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq);
            var vals = new double[Math.Max(0, cols.Length - channelStart)];
            for (var c = channelStart; c < cols.Length; c++)
            {
                if (!double.TryParse(cols[c], NumberStyles.Float, CultureInfo.InvariantCulture, out vals[c - channelStart]))
                    vals[c - channelStart] = double.NaN;
            }

            frames.Add(new SampleFrame { Timestamp = ts, Sequence = seq, Values = vals });
            values.Add(vals);
        }

        return new CsvReplaySession(channelNames, frames, values);
    }
}
