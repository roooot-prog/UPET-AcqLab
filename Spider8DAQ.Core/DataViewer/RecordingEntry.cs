namespace Spider8DAQ.Core.DataViewer;

/// <summary>Index entry for a recorded CSV under the local recordings folder.</summary>
public sealed class RecordingEntry
{
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public DateTime ModifiedLocal { get; init; }
    public long SizeBytes { get; init; }
    public int SampleCount { get; init; }
    public int ChannelCount { get; init; }
    public string ChannelSummary { get; init; } = "";
    public TimeSpan Duration { get; init; }
    public string SizeDisplay => SizeBytes < 1024 * 1024
        ? $"{SizeBytes / 1024.0:0.#} KB"
        : $"{SizeBytes / (1024.0 * 1024.0):0.##} MB";
    public string DurationDisplay => Duration.TotalSeconds < 1
        ? $"{Duration.TotalMilliseconds:0} ms"
        : Duration.TotalMinutes < 1
            ? $"{Duration.TotalSeconds:0.##} s"
            : $"{Duration.TotalMinutes:0.##} min";
}
