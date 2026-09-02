using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Acquisition;

public sealed class LevelTrigger
{
    private double? _previous;
    private bool _fired;

    public TriggerSettings Settings { get; set; } = new();

    public void Reset()
    {
        _previous = null;
        _fired = false;
    }

    public bool ShouldStart(SampleFrame frame, IReadOnlyList<double> physical)
    {
        if (!Settings.Enabled || _fired)
            return Settings.Enabled ? _fired : true;

        if (Settings.ChannelIndex < 0 || Settings.ChannelIndex >= physical.Count)
            return false;

        var value = physical[Settings.ChannelIndex];
        var prev = _previous;
        _previous = value;

        if (prev is null)
            return false;

        var crossed = Settings.RisingEdge
            ? prev < Settings.Threshold && value >= Settings.Threshold
            : prev > Settings.Threshold && value <= Settings.Threshold;

        if (crossed)
            _fired = true;

        return _fired;
    }
}
