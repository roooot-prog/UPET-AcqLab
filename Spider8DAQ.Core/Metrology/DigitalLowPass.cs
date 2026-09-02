namespace Spider8DAQ.Core.Metrology;

/// <summary>Software anti-alias LPF kind (Easy-style). Hardware FI may still be programmed separately.</summary>
public enum DigitalFilterKind
{
    Off = 0,
    /// <summary>2nd-order Bessel-like (Q≈0.577) — default for step plateaus.</summary>
    Bessel = 1,
    /// <summary>2nd-order Butterworth (Q=1/√2).</summary>
    Butterworth = 2
}

/// <summary>Stateful biquad LPF (RBJ cookbook) for one channel.</summary>
public sealed class BiquadLowPass
{
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _z1, _z2;
    private bool _bypass = true;

    public void Configure(double sampleRateHz, double cutoffHz, DigitalFilterKind kind)
    {
        if (kind == DigitalFilterKind.Off || cutoffHz <= 0 || sampleRateHz < 2)
        {
            _bypass = true;
            Reset();
            return;
        }

        // Nyquist guard — SoftSetup FI often uses Rate/10; keep software safe.
        var fs = Math.Max(2.0, sampleRateHz);
        var fc = Math.Clamp(cutoffHz, 0.05, fs * 0.45);
        var q = kind == DigitalFilterKind.Butterworth ? (1.0 / Math.Sqrt(2.0)) : 0.57735026919;

        var w0 = 2.0 * Math.PI * fc / fs;
        var cosw0 = Math.Cos(w0);
        var sinw0 = Math.Sin(w0);
        var alpha = sinw0 / (2.0 * q);

        var b0 = (1.0 - cosw0) * 0.5;
        var b1 = 1.0 - cosw0;
        var b2 = (1.0 - cosw0) * 0.5;
        var a0 = 1.0 + alpha;
        var a1 = -2.0 * cosw0;
        var a2 = 1.0 - alpha;

        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
        _bypass = false;
        // Keep state when retuning gently; hard Reset on Start.
    }

    public void Reset()
    {
        _z1 = 0;
        _z2 = 0;
    }

    /// <summary>Prime filter to <paramref name="value"/> (avoids step transient after Zero).</summary>
    public void SettleTo(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        if (_bypass)
        {
            _z1 = 0;
            _z2 = 0;
            return;
        }

        // Direct Form II Transposed steady-state for DC (gain ≈ 1):
        _z1 = value * (1.0 - _b0);
        _z2 = value * (_b2 - _a2);
    }

    public double Process(double x)
    {
        if (_bypass || double.IsNaN(x) || double.IsInfinity(x))
            return x;

        var y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }
}

/// <summary>
/// Per-channel software LPF applied on the live/record path after Scale/Offset.
/// Official step readings (median/mean) use these filtered values.
/// </summary>
public sealed class LiveFilterBank
{
    private BiquadLowPass[] _filters = Array.Empty<BiquadLowPass>();
    private double[] _lastCutoff = Array.Empty<double>();
    private int _sampleRateHz = 50;
    private DigitalFilterKind _kind = DigitalFilterKind.Bessel;
    private string _configKey = "";

    public DigitalFilterKind Kind
    {
        get => _kind;
        set
        {
            if (_kind == value) return;
            _kind = value;
            _configKey = ""; // force reconfigure
        }
    }

    public int SampleRateHz => _sampleRateHz;

    public void EnsureSize(int channelCount)
    {
        if (channelCount < 0) channelCount = 0;
        if (_filters.Length == channelCount) return;
        _filters = Enumerable.Range(0, channelCount).Select(_ => new BiquadLowPass()).ToArray();
        _lastCutoff = new double[channelCount];
        _configKey = "";
    }

    public void Configure(int sampleRateHz, IReadOnlyList<double> cutoffHzPerChannel, DigitalFilterKind? kind = null)
    {
        if (kind is { } k) _kind = k;
        _sampleRateHz = Math.Max(1, sampleRateHz);
        EnsureSize(cutoffHzPerChannel.Count);
        var key = $"{_kind}|{_sampleRateHz}|{string.Join(",", cutoffHzPerChannel.Select(c => c.ToString("G6")))}";
        if (key == _configKey) return;
        _configKey = key;
        for (var i = 0; i < _filters.Length; i++)
        {
            var fc = i < cutoffHzPerChannel.Count ? cutoffHzPerChannel[i] : 0;
            _lastCutoff[i] = fc;
            _filters[i].Configure(_sampleRateHz, fc, _kind);
        }
    }

    public void ResetAll()
    {
        foreach (var f in _filters)
            f.Reset();
    }

    public void SettleAll(IReadOnlyList<double> physical)
    {
        for (var i = 0; i < _filters.Length && i < physical.Count; i++)
            _filters[i].SettleTo(physical[i]);
    }

    /// <summary>Apply LPF in-place on physical engineering values.</summary>
    public void ApplyInPlace(double[] physical)
    {
        var n = Math.Min(physical.Length, _filters.Length);
        for (var i = 0; i < n; i++)
            physical[i] = _filters[i].Process(physical[i]);
    }

    /// <summary>Default Easy hint: ≈ SampleRate/10 (min 1 Hz).</summary>
    public static double DefaultCutoffHz(int sampleRateHz)
        => Math.Max(1.0, sampleRateHz / 10.0);
}
