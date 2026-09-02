using System.Globalization;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;

namespace Spider8DAQ.Core.DataViewer;

public static class RecordingIndex
{
    public static IReadOnlyList<RecordingEntry> Scan(string folder, string? search = null)
    {
        if (!Directory.Exists(folder)) return Array.Empty<RecordingEntry>();
        var q = (search ?? "").Trim();
        var list = new List<RecordingEntry>();
        foreach (var path in EnumerateRecordingFiles(folder).OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var name = Path.GetFileName(path);
            // Stats companion files are not primary recordings.
            if (name.EndsWith("_stats.csv", StringComparison.OrdinalIgnoreCase))
                continue;
            if (q.Length > 0 && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            try
            {
                list.Add(Probe(path));
            }
            catch
            {
                list.Add(new RecordingEntry
                {
                    FilePath = path,
                    FileName = name,
                    ModifiedLocal = File.GetLastWriteTime(path),
                    SizeBytes = new FileInfo(path).Length,
                    SampleCount = 0,
                    ChannelCount = 0,
                    ChannelSummary = "(citire eșuată)",
                    Duration = TimeSpan.Zero
                });
            }
        }
        return list;
    }

    private static IEnumerable<string> EnumerateRecordingFiles(string folder)
    {
        foreach (var path in Directory.GetFiles(folder, "*.csv"))
            yield return path;
        foreach (var path in Directory.GetFiles(folder, "*" + UpetReportFile.Extension))
            yield return path;
        foreach (var path in Directory.GetFiles(folder, "*" + UpetReportFile.LegacyExtension))
            yield return path;
    }

    public static RecordingEntry Probe(string path)
    {
        var fi = new FileInfo(path);
        if (UpetReportFile.HasReportExtension(path) || UpetReportFile.LooksLikeUpetReport(path))
            return ProbeUpet(fi);

        // Single shared pass — works while CsvRecordingWriter still holds the file.
        string? header = null;
        string? firstData = null;
        string? lastData = null;
        var sampleCount = 0;
        foreach (var line in CsvSharedIO.EnumerateDataLines(path))
        {
            if (header is null)
            {
                header = line;
                continue;
            }

            sampleCount++;
            firstData ??= line;
            lastData = line;
        }

        if (header is null)
            throw new InvalidDataException("CSV gol.");

        var headers = header.Split(',');
        // Align with CsvReplaySession: Timestamp, Sequence, optional t_s, then channels.
        var hasT = headers.Length >= 3 && SamplingRateInfo.IsRelativeTimeHeader(headers[2]);
        var channelStart = hasT ? 3 : 2;
        var channelNames = headers.Length > channelStart
            ? headers.Skip(channelStart).ToArray()
            : Array.Empty<string>();

        DateTime? first = null, last = null;
        if (firstData is not null)
        {
            var cols = firstData.Split(',');
            if (cols.Length > 0 &&
                DateTime.TryParse(cols[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t0))
                first = t0;
        }

        if (lastData is not null)
        {
            var cols = lastData.Split(',');
            if (cols.Length > 0 &&
                DateTime.TryParse(cols[0], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t1))
                last = t1;
        }

        var duration = first is { } a && last is { } b && b >= a ? b - a : TimeSpan.Zero;
        return new RecordingEntry
        {
            FilePath = path,
            FileName = fi.Name,
            ModifiedLocal = fi.LastWriteTime,
            SizeBytes = fi.Length,
            SampleCount = sampleCount,
            ChannelCount = channelNames.Length,
            ChannelSummary = channelNames.Length == 0
                ? "—"
                : string.Join(", ", channelNames.Take(6)) + (channelNames.Length > 6 ? "…" : ""),
            Duration = duration
        };
    }

    /// <summary>Lightweight listing for .upet — full decrypt only on Load.</summary>
    private static RecordingEntry ProbeUpet(FileInfo fi)
    {
        return new RecordingEntry
        {
            FilePath = fi.FullName,
            FileName = fi.Name,
            ModifiedLocal = fi.LastWriteTime,
            SizeBytes = fi.Length,
            SampleCount = 0,
            ChannelCount = 0,
            ChannelSummary = "Raport UPET (.upet)",
            Duration = TimeSpan.Zero
        };
    }

    public static OfflineSession Load(string path)
    {
        if (UpetReportFile.HasReportExtension(path) || UpetReportFile.LooksLikeUpetReport(path))
            return UpetReportFile.Load(path);
        return OfflineSession.FromCsv(path);
    }
}
