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
