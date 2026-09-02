using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Core.Metrology;

/// <summary>
/// Live metrology pipeline: digital LPF, rolling windows for official step readings,
/// zero hysteresis, scale-suspect diagnostics. Applied on the same path as display/record.
/// </summary>
public sealed class MetrologyPipeline
{
    private readonly object _gate = new();
    private RollingSampleWindow[] _windows = Array.Empty<RollingSampleWindow>();
    private int _windowCapacity = -1;
    private double[] _lastPhysical = Array.Empty<double>();
    private long _lastSequence;
    private DateTime _lastStampUtc = DateTime.UtcNow;
    private double? _hysteresisZeroBefore;
    private double _hysteresisPeakAbs;
    private int _hysteresisChannel = -1;
    private bool _highLoadAcknowledged;
    private string? _lastScaleSuspectMessage;
    private DateTime _lastScaleSuspectUtc = DateTime.MinValue;

    public LiveFilterBank Filter { get; } = new();
    public List<LoadStepReading> Steps { get; } = new();
    public StepAggregateKind AggregateKind { get; set; } = StepAggregateKind.Median;
    public double StepWindowSeconds { get; set; } = MetrologyConstants.DefaultStepWindowSeconds;
    public LinearityReport? LastLinearity { get; private set; }
    public ZeroHysteresisResult? LastHysteresis { get; private set; }
    public string SyncBehaviorNote { get; } =
        "Canalele din același SampleFrame partajează Timestamp + Sequence (sincron forță+strain). " +
        "Backend-urile asincrone pe sloturi cascadorate sunt aliniate la Sequence comun în CascadedSpider8.";

    public bool HighLoadAcknowledged
    {
        get { lock (_gate) return _highLoadAcknowledged; }
        set { lock (_gate) _highLoadAcknowledged = value; }
    }

    public string? LastScaleSuspectMessage
    {
        get { lock (_gate) return _lastScaleSuspectMessage; }
    }

    public void ConfigureFilters(int sampleRateHz, IReadOnlyList<ChannelConfig> channels, DigitalFilterKind kind)
    {
        lock (_gate)
        {
            Filter.Kind = kind;
            var cutoffs = channels.Select(c => c.Enabled ? Math.Max(0, c.FilterHz) : 0).ToArray();
            Filter.Configure(sampleRateHz, cutoffs, kind);
            EnsureWindows(channels.Count, sampleRateHz);
        }
    }

    public void ResetFilters()
    {
        lock (_gate)
        {
            Filter.ResetAll();
            foreach (var w in _windows) w.Clear();
        }
    }

    public void SettleFilters(IReadOnlyList<double> physical)
    {
        lock (_gate) Filter.SettleAll(physical);
    }

    /// <summary>
    /// Call after Scale/Offset (ChannelConfig.Apply). Mutates <paramref name="physical"/> with LPF,
    /// updates rolling windows for Citire treaptă.
    /// </summary>
    public void ProcessPhysicalFrame(SampleFrame frame, double[] physical, IReadOnlyList<ChannelConfig> channels)
    {
        lock (_gate)
        {
            EnsureWindows(physical.Length, Filter.SampleRateHz > 0 ? Filter.SampleRateHz : 50);
            Filter.ApplyInPlace(physical);
            for (var i = 0; i < physical.Length && i < _windows.Length; i++)
            {
                if (i < channels.Count && !channels[i].Enabled) continue;
                _windows[i].Push(physical[i]);
            }

            if (_lastPhysical.Length != physical.Length)
                _lastPhysical = new double[physical.Length];
            Array.Copy(physical, _lastPhysical, physical.Length);
            _lastSequence = frame.Sequence;
            _lastStampUtc = frame.Timestamp.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(frame.Timestamp, DateTimeKind.Utc)
                : frame.Timestamp.ToUniversalTime();

            if (_hysteresisZeroBefore is not null && _hysteresisChannel >= 0
                && _hysteresisChannel < physical.Length
                && !double.IsNaN(physical[_hysteresisChannel]))
            {
                var abs = Math.Abs(physical[_hysteresisChannel] - _hysteresisZeroBefore.Value);
                if (abs > _hysteresisPeakAbs)
                    _hysteresisPeakAbs = abs;
            }
        }
    }

    public LoadStepReading CaptureStep(
        IReadOnlyList<string> channelNames,
        string label,
        double massKg,
        double referenceForceN)
    {
        lock (_gate)
        {
            var values = new double[_windows.Length];
            for (var i = 0; i < _windows.Length; i++)
                values[i] = _windows[i].Aggregate(AggregateKind);

            var step = new LoadStepReading
            {
                StepIndex = Steps.Count + 1,
                Utc = DateTime.UtcNow,
                Sequence = _lastSequence,
                Label = string.IsNullOrWhiteSpace(label) ? $"Treaptă {Steps.Count + 1}" : label,
                MassKg = massKg,
                ReferenceForceN = referenceForceN > 0
                    ? referenceForceN
                    : (massKg > 0 ? MetrologyMath.ForceFromMassKg(massKg) : 0),
                ChannelValues = values,
                AggregateNote =
                    $"{AggregateKind} pe ~{StepWindowSeconds:0.##}s ({_windows.FirstOrDefault()?.Count ?? 0} eșantioane filtrate)"
            };
            Steps.Add(step);
            return step;
        }
    }

    public void ClearSteps()
    {
        lock (_gate)
        {
            Steps.Clear();
            LastLinearity = null;
        }
    }

    public LinearityReport ComputeLinearity(int forceChannel, int strainChannel)
    {
        lock (_gate)
        {
            var f = new List<double>();
            var e = new List<double>();
            foreach (var s in Steps)
            {
                if (forceChannel < 0 || forceChannel >= s.ChannelValues.Length) continue;
                if (strainChannel < 0 || strainChannel >= s.ChannelValues.Length) continue;
                f.Add(s.ChannelValues[forceChannel]);
                e.Add(s.ChannelValues[strainChannel]);
            }

            LastLinearity = MetrologyMath.FitStrainVsForce(f, e);
            return LastLinearity;
        }
    }

    public void MarkHysteresisZero(int channelIndex)
    {
        lock (_gate)
        {
            _hysteresisChannel = channelIndex;
            _hysteresisPeakAbs = 0;
            _hysteresisZeroBefore = channelIndex >= 0 && channelIndex < _lastPhysical.Length
                ? _lastPhysical[channelIndex]
                : double.NaN;
            LastHysteresis = null;
        }
    }

    public ZeroHysteresisResult MeasureHysteresisResidual(string channelName)
    {
        lock (_gate)
        {
            var ch = _hysteresisChannel;
            var before = _hysteresisZeroBefore ?? double.NaN;
            var after = ch >= 0 && ch < _lastPhysical.Length ? _lastPhysical[ch] : double.NaN;
            var drift = after - before;
            var peak = Math.Max(_hysteresisPeakAbs, Math.Abs(drift));
            var result = new ZeroHysteresisResult
            {
                ChannelName = channelName,
                ZeroBefore = before,
                ResidualAfterUnload = after,
                ResidualPeakAbs = peak,
                Drift = drift,
                Message = double.IsNaN(before) || double.IsNaN(after)
                    ? "Histereză Zero: lipsește Zero înainte de încărcare sau semnal live."
                    : $"Histereză Zero ({channelName}): residual={after:G6}, Δ={drift:G6}, |peak|={peak:G6}"
            };
            LastHysteresis = result;
            return result;
        }
    }

    /// <summary>
    /// Scale suspect: |F| &gt; Capacity×1.2 when not acknowledged as high load, or idle after zero.
    /// Does NOT clamp values.
    /// </summary>
    public ScaleSuspectAlert? CheckScaleSuspect(
        IReadOnlyList<ChannelConfig> channels,
        bool idleAfterZero,
        double suppressDuplicateSeconds = 8)
    {
        lock (_gate)
        {
            if (_highLoadAcknowledged) return null;
            for (var i = 0; i < channels.Count && i < _lastPhysical.Length; i++)
            {
                var ch = channels[i];
                if (!ch.Enabled || ch.Capacity <= 0) continue;
                var v = _lastPhysical[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                var limit = ch.Capacity * MetrologyConstants.ScaleSuspectCapacityFactor;
                var over = Math.Abs(v) > limit;
                var idleSpike = idleAfterZero && Math.Abs(v) > Math.Max(ch.Capacity * 0.05, limit * 0.15);
                if (!over && !idleSpike) continue;

                var msg = over
                    ? $"Scale suspect: {ch.Name}={v:G5} {ch.Unit} > Capacity×{MetrologyConstants.ScaleSuspectCapacityFactor:0.#} ({limit:G5})"
                    : $"Scale suspect (idle): {ch.Name}={v:G5} {ch.Unit} după Zero — verificați Scale/tare";

                if (_lastScaleSuspectMessage == msg
                    && (DateTime.UtcNow - _lastScaleSuspectUtc).TotalSeconds < suppressDuplicateSeconds)
                    return null;

                _lastScaleSuspectMessage = msg;
                _lastScaleSuspectUtc = DateTime.UtcNow;
                return new ScaleSuspectAlert
                {
                    ChannelIndex = i,
                    ChannelName = ch.Name,
                    Value = v,
                    Capacity = ch.Capacity,
                    Limit = limit,
                    Message = msg
                };
            }

            return null;
        }
    }

    public IReadOnlyList<double> SnapshotLastPhysical()
    {
        lock (_gate) return (double[])_lastPhysical.Clone();
    }

    public long LastSequence
    {
        get { lock (_gate) return _lastSequence; }
    }

    private void EnsureWindows(int channelCount, int sampleRateHz)
    {
        var n = Math.Max(1, (int)Math.Round(Math.Max(0.2, StepWindowSeconds) * Math.Max(1, sampleRateHz)));
        n = Math.Clamp(n, 5, 500);
        if (_windows.Length == channelCount && _windowCapacity == n)
            return;

        _windowCapacity = n;
        _windows = Enumerable.Range(0, Math.Max(0, channelCount))
            .Select(_ => new RollingSampleWindow(n))
            .ToArray();
    }
}
