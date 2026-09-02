namespace Spider8DAQ.Core.DataViewer;

/// <summary>Sample-index playback cursor over an offline session (UI drives the timer).</summary>
public sealed class PlaybackCursor
{
    public int Index { get; private set; }
    public int Length { get; private set; }
    public bool IsPlaying { get; private set; }
    public double Speed { get; set; } = 1.0;

    public void Load(int length)
    {
        Length = Math.Max(0, length);
        Index = 0;
        IsPlaying = false;
    }

    public void Play() => IsPlaying = Length > 0;
    public void Pause() => IsPlaying = false;
    public void Stop()
    {
        IsPlaying = false;
        Index = 0;
    }

    public void Seek(int index)
    {
        if (Length <= 0) { Index = 0; return; }
        Index = Math.Clamp(index, 0, Length - 1);
    }

    /// <summary>Advances by one visual step; returns false at end (auto-pause).</summary>
    public bool Tick()
    {
        if (!IsPlaying || Length <= 0) return false;
        var step = Math.Max(1, (int)Math.Round(Speed));
        if (Index + step >= Length)
        {
            Index = Length - 1;
            IsPlaying = false;
            return false;
        }
        Index += step;
        return true;
    }

    public string StatusText =>
        Length <= 0 ? "Fără date"
            : IsPlaying ? $"▶ {Index + 1}/{Length} ×{Speed:0.##}"
            : $"❚❚ {Index + 1}/{Length}";
}
