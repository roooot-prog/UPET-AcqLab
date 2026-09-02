namespace Spider8DAQ.Core.Defects;

public enum DefectSeverity
{
    Info,
    Warning,
    Critical
}

/// <summary>Catalog entry for the lab defect library (browsable + example waveform).</summary>
public sealed class LabDefectDefinition
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Category { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Symptoms { get; init; } = "";
    public string LikelyCauses { get; init; } = "";
    public string FixSteps { get; init; } = "";
    public string ExampleCaption { get; init; } = "";
    public DefectSeverity Severity { get; init; } = DefectSeverity.Warning;
}

/// <summary>Live / offline finding with a clear Romanian message (not a raw alarm).</summary>
public sealed class TypicalMistakeFinding
{
    public string DefectId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string ChannelHint { get; init; } = "";
    /// <summary>HW channel index when known (−1 if offline/name-only).</summary>
    public int ChannelIndex { get; init; } = -1;
    public DefectSeverity Severity { get; init; } = DefectSeverity.Warning;
    public DateTime DetectedLocal { get; init; } = DateTime.Now;
}

public sealed class ChannelSnapshot
{
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string Unit { get; init; } = "";
    public bool Enabled { get; init; }
    public bool RecordEnabled { get; init; }
    public double Scale { get; init; } = 1;
    public double Capacity { get; init; }
    public double Value { get; init; }
    public double[]? RecentSamples { get; init; }
}
