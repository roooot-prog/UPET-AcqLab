namespace Spider8DAQ.Core.Simulation;

/// <summary>
/// Physically motivated compression-cycle waveforms for Contur / generic lab demo.
/// Emits engineering values (before Scale/Tare); caller converts to raw.
/// </summary>
public sealed class LabSignalModel
{
    public const double DefaultCycleSeconds = 36.0;
    public const double DefaultPeakForceN = 85_000.0;
    public const double DefaultPeakStrokeMm = 3.2;
    public const double DefaultPeakBulgeMm = 0.55;

    private double[] _radialGain;
    private double[] _radialPhase;
    private double[] _driftSlope;
    private double[] _bias;
    private double _strokePeakMm;
    private double _peakLoadSeen;
    private long _lastCycleIndex = -1;
    private int _glitchCountdown;
    private int _glitchChannel = -1;
    private double _glitchAmp;
    private readonly int _seed;

    public LabSignalModel(int channelCount, int seed = 42)
    {
        _seed = seed;
        channelCount = Math.Max(1, channelCount);
        _radialGain = new double[channelCount];
        _radialPhase = new double[channelCount];
        _driftSlope = new double[channelCount];
        _bias = new double[channelCount];
        SeedChannelParams(0, channelCount, new Random(seed));
    }

    /// <summary>Reset plastic memory / glitch state when streaming restarts.</summary>
    public void ResetCycle()
    {
        _strokePeakMm = 0;
        _peakLoadSeen = 0;
        _lastCycleIndex = -1;
        _glitchCountdown = 0;
        _glitchChannel = -1;
        _glitchAmp = 0;
    }

    /// <summary>
    /// Smooth load fraction 0…1: rise → brief Fmax hold → unload.
    /// </summary>
    public static double CompressionLoadFraction(double tSeconds, double cycleSeconds = DefaultCycleSeconds)
    {
        if (cycleSeconds <= 1e-9) cycleSeconds = DefaultCycleSeconds;
        var p = tSeconds / cycleSeconds;
        p -= Math.Floor(p);
        // 0–52% load, 52–60% hold near peak, 60–100% unload
        if (p < 0.52)
            return SmoothStep(p / 0.52);
        if (p < 0.60)
            return 1.0;
        return 1.0 - SmoothStep((p - 0.60) / 0.40);
    }

    public static double SmoothStep(double x)
    {
        x = Math.Clamp(x, 0, 1);
        return x * x * (3 - 2 * x);
    }

    /// <summary>
    /// Fill <paramref name="engineering"/> with physical values for the current sample.
    /// </summary>
    public void Evaluate(
        double tSeconds,
        SimChannelRole[] roles,
        IReadOnlyList<double>? radialAnglesDeg,
        SimulatorScenarioHint? hint,
        SimulatorScenarioKind kind,
        double[] engineering,
        Random noise)
    {
        if (engineering.Length == 0) return;
        EnsureCapacity(engineering.Length);

        if (kind == SimulatorScenarioKind.CylinderContour
            && hint is { OneShotRamp: true, RadialTargetMm.Count: > 0 })
        {
            EvaluateOneShotRamp(tSeconds, roles, hint, engineering);
            return;
        }

        var cycle = hint is { CycleSeconds: > 0 } ? hint.CycleSeconds : DefaultCycleSeconds;
        var peakF = hint is { PeakForce: > 0 } ? hint.PeakForce : DefaultPeakForceN;
        var peakStroke = hint is { PeakStrokeMm: > 0 } ? hint.PeakStrokeMm : DefaultPeakStrokeMm;
        var peakBulge = hint is { PeakRadialBulgeMm: > 0 } ? hint.PeakRadialBulgeMm : DefaultPeakBulgeMm;

        var cycleIndex = (long)Math.Floor(Math.Max(0, tSeconds) / cycle);
        if (cycleIndex != _lastCycleIndex)
        {
            _lastCycleIndex = cycleIndex;
            // New compression cycle: clear peak tracking; keep a small plastic residual on stroke.
            _peakLoadSeen = 0;
            _strokePeakMm *= 0.35;
        }

        var load = kind == SimulatorScenarioKind.CylinderContour
            ? CompressionLoadFraction(tSeconds, cycle)
            : 0.5 + 0.5 * Math.Sin(2 * Math.PI * tSeconds / Math.Max(8.0, cycle * 0.5));

        if (load + 1e-9 >= _peakLoadSeen)
            _peakLoadSeen = load;

        var force = peakF * load;

        // Stroke: rises with load; unload recovers only ~55% (plasticity / friction).
        var strokeLoading = peakStroke * (0.75 * load + 0.25 * load * load);
        double stroke;
        if (load + 1e-6 >= _peakLoadSeen)
        {
            _strokePeakMm = Math.Max(_strokePeakMm, strokeLoading);
            stroke = strokeLoading;
        }
        else
        {
            var unload = (_peakLoadSeen - load) / Math.Max(_peakLoadSeen, 1e-9);
            stroke = _strokePeakMm * (1.0 - 0.55 * SmoothStep(unload));
        }

        // Mean barreling ~ load^0.85 (slightly softer than force).
        var meanBulge = peakBulge * Math.Pow(Math.Max(load, 0), 0.85);

        MaybeArmGlitch(noise, engineering.Length, roles);

        var radialOrdinal = 0;
        for (var i = 0; i < engineering.Length; i++)
        {
            var role = i < roles.Length ? roles[i] : SimChannelRole.Generic;
            double v;
            if (role == SimChannelRole.RadialDisplacement)
            {
                v = RadialAt(i, radialOrdinal, meanBulge, radialAnglesDeg);
                radialOrdinal++;
            }
            else
            {
                v = role switch
                {
                    SimChannelRole.Force => force,
                    SimChannelRole.Stroke => stroke,
                    SimChannelRole.Strain => 180.0 * load + 12.0 * Math.Sin(2 * Math.PI * 0.35 * tSeconds + i),
                    SimChannelRole.Voltage => 0.15 * load + 0.02 * Math.Sin(2 * Math.PI * (0.2 + 0.05 * i) * tSeconds),
                    _ => GenericWave(i, tSeconds, load)
                };
            }

            v += _bias[i] + _driftSlope[i] * tSeconds;
            v += QuantizationNoise(noise, role);
            if (_glitchCountdown > 0 && i == _glitchChannel)
                v += _glitchAmp;

            engineering[i] = v;
        }

        if (_glitchCountdown > 0)
            _glitchCountdown--;
    }

    /// <summary>
    /// Linear ramp 0→assigned u [mm] over CycleSeconds (default 10 s), then hold.
    /// Targets are in S1…Sn order, mapped onto hardware channels via RadialChannelIndices.
    /// Force / stroke (if not overlapping a radial) follow the same envelope. No ovality / unload.
    /// </summary>
    private void EvaluateOneShotRamp(
        double tSeconds,
        SimChannelRole[] roles,
        SimulatorScenarioHint hint,
        double[] engineering)
    {
        var duration = hint.CycleSeconds > 1e-9 ? hint.CycleSeconds : ContourSimAssignment.DurationSeconds;
        var frac = duration <= 1e-12 ? 1.0 : Math.Clamp(tSeconds / duration, 0, 1);
        var peakF = hint.PeakForce > 0 ? hint.PeakForce : DefaultPeakForceN;
        var peakStroke = hint.PeakStrokeMm > 0 ? hint.PeakStrokeMm : DefaultPeakStrokeMm;
        var indices = hint.RadialChannelIndices;
        var targets = hint.RadialTargetMm!;

        for (var i = 0; i < engineering.Length; i++)
        {
            var role = i < roles.Length ? roles[i] : SimChannelRole.Generic;
            if (role == SimChannelRole.RadialDisplacement)
            {
                engineering[i] = LookupRadialTarget(i, indices, targets) * frac;
                continue;
            }

            engineering[i] = role switch
            {
                SimChannelRole.Force => peakF * frac,
                SimChannelRole.Stroke => peakStroke * frac,
                SimChannelRole.Strain => 180.0 * frac,
                SimChannelRole.Voltage => 0.15 * frac,
                _ => 0
            };
        }
    }

    private static double LookupRadialTarget(
        int channelIndex,
        IReadOnlyList<int>? radialChannelIndices,
        IReadOnlyList<double> targets)
    {
        if (radialChannelIndices is { Count: > 0 })
        {
            for (var s = 0; s < radialChannelIndices.Count && s < targets.Count; s++)
            {
                if (radialChannelIndices[s] == channelIndex)
                    return targets[s];
            }
        }

        if (channelIndex >= 0 && channelIndex < targets.Count)
            return targets[channelIndex];
        return ContourSimAssignment.MidMm;
    }

    /// <summary>Convert engineering → raw for Soft Zero / Scale path: raw ≈ engineering / Scale.</summary>
    public static double ToRaw(double engineering, double scale, double lsb)
    {
        var s = Math.Abs(scale) < 1e-12 ? 1.0 : scale;
        var raw = engineering / s;
        if (lsb > 0)
            raw = Math.Round(raw / lsb) * lsb;
        return raw;
    }

    private double RadialAt(int channelIndex, int ordinal, double meanBulge, IReadOnlyList<double>? anglesDeg)
    {
        double angleRad;
        if (anglesDeg is not null && ordinal < anglesDeg.Count)
            angleRad = anglesDeg[ordinal] * Math.PI / 180.0;
        else
            angleRad = ordinal * (Math.PI / 2.0);

        // Ovality (mode-2) + slight phase offset per sensor.
        var ovality = 1.0 + 0.09 * Math.Cos(2.0 * angleRad + _radialPhase[channelIndex] * 0.15);
        return meanBulge * ovality * _radialGain[channelIndex];
    }

    private static double GenericWave(int index, double t, double loadEnvelope)
    {
        var freq = 0.12 + index * 0.04;
        return (0.35 + 0.25 * loadEnvelope) * Math.Sin(2 * Math.PI * freq * t)
               + 0.05 * Math.Sin(2 * Math.PI * (freq * 2.3) * t + index);
    }

    private static double QuantizationNoise(Random noise, SimChannelRole role)
    {
        var sigma = role switch
        {
            SimChannelRole.Force => 4.0,
            SimChannelRole.Stroke => 0.0008,
            SimChannelRole.RadialDisplacement => 0.0006,
            SimChannelRole.Strain => 0.4,
            SimChannelRole.Voltage => 0.00015,
            _ => 0.002
        };
        var u1 = Math.Max(1e-12, noise.NextDouble());
        var u2 = noise.NextDouble();
        var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
        return z * sigma;
    }

    private void MaybeArmGlitch(Random noise, int channelCount, SimChannelRole[] roles)
    {
        if (_glitchCountdown > 0) return;
        // ~1 micro-glitch / 25 s at 50 Hz
        if (noise.NextDouble() > 1.0 / 1250.0) return;
        _glitchCountdown = 1 + noise.Next(0, 3);
        _glitchChannel = noise.Next(0, channelCount);
        var role = _glitchChannel < roles.Length ? roles[_glitchChannel] : SimChannelRole.Generic;
        _glitchAmp = role switch
        {
            SimChannelRole.Force => (noise.NextDouble() - 0.5) * 180.0,
            SimChannelRole.Stroke or SimChannelRole.RadialDisplacement => (noise.NextDouble() - 0.5) * 0.015,
            SimChannelRole.Strain => (noise.NextDouble() - 0.5) * 8.0,
            _ => (noise.NextDouble() - 0.5) * 0.02
        };
    }

    private void EnsureCapacity(int n)
    {
        if (_radialGain.Length >= n) return;
        var old = _radialGain.Length;
        Array.Resize(ref _radialGain, n);
        Array.Resize(ref _radialPhase, n);
        Array.Resize(ref _driftSlope, n);
        Array.Resize(ref _bias, n);
        SeedChannelParams(old, n, new Random(_seed + n));
    }

    private void SeedChannelParams(int from, int to, Random rng)
    {
        for (var i = from; i < to; i++)
        {
            _radialGain[i] = 0.94 + rng.NextDouble() * 0.12;
            _radialPhase[i] = rng.NextDouble() * Math.PI * 2;
            _driftSlope[i] = (rng.NextDouble() - 0.5) * 2e-5;
            _bias[i] = (rng.NextDouble() - 0.5) * 1e-4;
        }
    }
}
