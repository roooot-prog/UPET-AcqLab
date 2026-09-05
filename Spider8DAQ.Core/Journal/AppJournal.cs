namespace Spider8DAQ.Core.Journal;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Setup
}

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; }
    public string Message { get; init; } = "";
}

public sealed class AppJournal
{
    private readonly object _sync = new();
    private readonly List<LogEntry> _entries = new();
    private readonly string? _filePath;

    public AppJournal(string? filePath = null)
    {
        _filePath = filePath;
        if (!string.IsNullOrWhiteSpace(_filePath))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_filePath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }

    public event EventHandler<LogEntry>? EntryAdded;

    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_sync) return _entries.ToList(); }
    }

    public IReadOnlyList<LogEntry> TakeLast(int count)
    {
        if (count <= 0) return Array.Empty<LogEntry>();
        lock (_sync)
        {
            if (_entries.Count == 0) return Array.Empty<LogEntry>();
            var start = Math.Max(0, _entries.Count - count);
            return _entries.GetRange(start, _entries.Count - start);
        }
    }

    public LogEntry? LastErrorOrWarning()
    {
        lock (_sync)
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Level == LogLevel.Error)
                    return _entries[i];
            }
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Level == LogLevel.Warning)
                    return _entries[i];
            }
            return _entries.Count > 0 ? _entries[^1] : null;
        }
    }

    /// <summary>Tail of spider8daq.log when MainWindow is not up yet (pending dialog).</summary>
    public static IReadOnlyList<LogEntry> ReadTailFromFile(string? path, int count)
    {
        if (count <= 0 || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Array.Empty<LogEntry>();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var take = (int)Math.Min(fs.Length, 48_000);
            fs.Seek(-take, SeekOrigin.End);
            using var reader = new StreamReader(fs);
            var text = reader.ReadToEnd();
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var start = Math.Max(0, lines.Length - count);
            var list = new List<LogEntry>(Math.Min(count, lines.Length));
            for (var i = start; i < lines.Length; i++)
            {
                var parsed = ParseLine(lines[i]);
                if (parsed is not null)
                    list.Add(parsed);
            }
            return list;
        }
        catch
        {
            return Array.Empty<LogEntry>();
        }
    }

    private static LogEntry? ParseLine(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length >= 3
            && DateTime.TryParse(parts[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var ts)
            && Enum.TryParse<LogLevel>(parts[1], ignoreCase: true, out var level))
        {
            return new LogEntry { Timestamp = ts, Level = level, Message = string.Join("\t", parts.Skip(2)) };
        }

        var trimmed = line.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? null
            : new LogEntry { Timestamp = DateTime.Now, Level = LogLevel.Info, Message = trimmed };
    }

    public void Info(string message) => Add(LogLevel.Info, message);
    public void Warn(string message) => Add(LogLevel.Warning, message);
    public void Error(string message) => Add(LogLevel.Error, message);
    public void Setup(string message) => Add(LogLevel.Setup, message);

    public void Add(LogLevel level, string message)
    {
        var entry = new LogEntry { Level = level, Message = message };
        lock (_sync)
        {
            _entries.Add(entry);
            if (_entries.Count > 5000) _entries.RemoveRange(0, _entries.Count - 4000);
            if (!string.IsNullOrWhiteSpace(_filePath))
            {
                try
                {
                    File.AppendAllText(_filePath!,
                        $"{entry.Timestamp:O}\t{entry.Level}\t{entry.Message}{Environment.NewLine}");
                }
                catch { /* ignore IO */ }
            }
        }
        EntryAdded?.Invoke(this, entry);
    }
}
