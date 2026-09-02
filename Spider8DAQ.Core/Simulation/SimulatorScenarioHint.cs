namespace Spider8DAQ.Core.Simulation;

/// <summary>Optional experiment / contour hint pushed from the App into the Simulator backend.</summary>
public enum SimulatorScenarioKind
{
    /// <summary>Infer from experiment type + channel units/names.</summary>
    Auto = 0,
    /// <summary>Generic lab waveforms (slow, noisy).</summary>
    Generic = 1,
    /// <summary>Cylinder compression with force / stroke / radial barreling.</summary>
    CylinderContour = 2
}

/// <summary>
/// Hint for <see cref="ISimulatorScenarioSink"/>. Indices are 0-based hardware channel indices.
/// </summary>
public sealed class SimulatorScenarioHint
{
    public SimulatorScenarioKind Kind { get; set; } = SimulatorScenarioKind.Auto;

    /// <summary>Raw experiment-type label (e.g. Compresiune cilindru – contur).</summary>
    public string? ExperimentType { get; set; }

    public int? ForceChannelIndex { get; set; }
    public int StrokeChannelIndex { get; set; } = -1;
    public IReadOnlyList<int>? RadialChannelIndices { get; set; }
    public IReadOnlyList<double>? RadialAnglesDeg { get; set; }

    /// <summary>Compression cycle length [s]. ≤0 → model default.</summary>
    public double CycleSeconds { get; set; }

    /// <summary>Peak force in engineering units (typically N). ≤0 → model default.</summary>
    public double PeakForce { get; set; }

    /// <summary>Peak mean radial bulge [mm]. ≤0 → model default.</summary>
    public double PeakRadialBulgeMm { get; set; }

    /// <summary>Peak press stroke [mm]. ≤0 → model default.</summary>
    public double PeakStrokeMm { get; set; }

    /// <summary>
    /// Per-sensor target radial u [mm] in S1…Sn order (parallel to <see cref="RadialChannelIndices"/>).
    /// When set with <see cref="OneShotRamp"/>, each radial ramps 0→target over <see cref="CycleSeconds"/> then holds.
    /// </summary>
    public IReadOnlyList<double>? RadialTargetMm { get; set; }

    /// <summary>
    /// True: linear ramp 0→target over CycleSeconds, then hold (no unload cycle).
    /// Used by the 8-sensor Contur simulator demo.
    /// </summary>
    public bool OneShotRamp { get; set; }
}
