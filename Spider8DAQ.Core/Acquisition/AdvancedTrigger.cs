using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Core.Acquisition;

public enum TriggerLogic
{
    Single,
    And,
    Or
}

public sealed class AdvancedTriggerSettings
{
    public bool Enabled { get; set; }
    public TriggerLogic Logic { get; set; } = TriggerLogic.Single;
    public int ChannelA { get; set; }
    public int ChannelB { get; set; } = 1;
    public double ThresholdA { get; set; }
    public double ThresholdB { get; set; }
    public bool RisingA { get; set; } = true;
    public bool RisingB { get; set; } = true;
    public bool WindowMode { get; set; }
    public double WindowLow { get; set; }
    public double WindowHigh { get; set; } = 1;
    public int PreTriggerMs { get; set; } = 500;
    public int PostTriggerMs { get; set; }
}

public sealed class AdvancedTrigger
{
    private double? _prevA;
    private double? _prevB;
    private bool _fired;

    public AdvancedTriggerSettings Settings { get; set; } = new();

    public void Reset()
    {
        _prevA = null;
        _prevB = null;
        _fired = false;
    }

    public bool ShouldStart(IReadOnlyList<double> physical)
    {
        if (!Settings.Enabled || _fired) return Settings.Enabled && _fired;

        bool edge(int ch, double thr, bool rising, ref double? prev)
        {
            if (ch < 0 || ch >= physical.Count) return false;
            var v = physical[ch];
            var p = prev;
            prev = v;
            if (p is null || double.IsNaN(v) || double.IsNaN(p.Value)) return false;
            return rising ? p < thr && v >= thr : p > thr && v <= thr;
        }

        bool a;
        if (Settings.WindowMode)
        {
            var v = Settings.ChannelA < physical.Count ? physical[Settings.ChannelA] : double.NaN;
            a = !double.IsNaN(v) && v >= Settings.WindowLow && v <= Settings.WindowHigh;
            // require entering window
            var p = _prevA;
            _prevA = v;
            a = a && p is double pv && (pv < Settings.WindowLow || pv > Settings.WindowHigh);
        }
        else
        {
            a = edge(Settings.ChannelA, Settings.ThresholdA, Settings.RisingA, ref _prevA);
        }

        var b = edge(Settings.ChannelB, Settings.ThresholdB, Settings.RisingB, ref _prevB);

        var hit = Settings.Logic switch
        {
            TriggerLogic.And => a && b,
            TriggerLogic.Or => a || b,
            _ => a
        };

        if (hit) _fired = true;
        return _fired;
    }
}
