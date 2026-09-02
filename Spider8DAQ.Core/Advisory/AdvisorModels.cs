namespace Spider8DAQ.Core.Advisory;

public enum AdviceSeverity
{
    Info,
    Warning,
    Error
}

public enum AdvisorExportStatus
{
    None,
    InProgress,
    Succeeded,
    Failed
}

/// <summary>Cheap same-type recording summary for peer comparison (not a full session).</summary>
public sealed record ExperimentSummary
{
    public string FilePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public DateTime ModifiedLocal { get; init; }
    public string ExperimentType { get; init; } = "";
    public int SampleCount { get; init; }
    public TimeSpan Duration { get; init; }
    public double? Fmax { get; init; }
    public double? EpsMax { get; init; }
    public double? OvalityMm { get; init; }
    public double? BarrelingIndex { get; init; }
    public double? UMeanMm { get; init; }
    public double? UMaxMm { get; init; }
}

/// <summary>One live/offline signal issue the advisor may surface as the next step.</summary>
public sealed class AdviceSignalFinding
{
    public string Kind { get; init; } = "";
    public string ChannelHint { get; init; } = "";
    public string Message { get; init; } = "";
}

/// <summary>Live DAQ + experiment snapshot passed into <see cref="LabAdvisor.Advise"/>.</summary>
public sealed class AdviceContext
{
    public bool IsConnected { get; init; }
    public bool IsStreaming { get; init; }
    public bool IsRecording { get; init; }
    public bool IsZeroed { get; init; }
    public bool HasAppliedSensor { get; init; }
    public int ChannelsOn { get; init; }
    public int SampleRateHz { get; init; }
    public string? SelectedSensor { get; init; }
    public string? ExperimentType { get; init; }

    public AdvisorExportStatus ExportStatus { get; init; }
    public string? LastExportError { get; init; }

    public bool HasClipping { get; init; }
    public bool HasFlatChannel { get; init; }
    public bool HasNan { get; init; }
    public bool HasOverload { get; init; }
    public string? FlatChannelHint { get; init; }
    public string? ClipChannelHint { get; init; }
    public IReadOnlyList<AdviceSignalFinding> Findings { get; init; } = Array.Empty<AdviceSignalFinding>();

    public double SampleDiameterMm { get; init; }
    public double SampleLengthMm { get; init; }
    public bool ContourMapped { get; init; }
    public bool ContourAnglesSet { get; init; }
    public double? OvalityMm { get; init; }
    public double? BarrelingIndex { get; init; }
    public double? UMaxMm { get; init; }
    public double? UMeanMm { get; init; }
    public string? FlatRadialWarning { get; init; }

    public bool HasGaugeFactorScale { get; init; }
    public double? PoissonNu { get; init; }
    public bool HasUnloadMarkers { get; init; }
    public bool HasOfflineSession { get; init; }
    public int OfflineSampleCount { get; init; }

    public ExperimentSummary? Current { get; init; }
    public IReadOnlyList<ExperimentSummary> Peers { get; init; } = Array.Empty<ExperimentSummary>();
    /// <summary>True when a same-type folder scan already ran (so «no peers» can be said once).</summary>
    public bool PeerScanComplete { get; init; }

    /// <summary>Compression specimen library (empty when type is not compression).</summary>
    public string? SpecimenName { get; init; }
    public string? SpecimenClass { get; init; }
    public string? SpecimenFormulaPack { get; init; }

    public static AdviceContext Empty { get; } = new();
}

public sealed class AdviceItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Suggestion { get; init; }
    public AdviceSeverity Severity { get; init; } = AdviceSeverity.Warning;
    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();
    public double Score { get; set; }
    /// <summary>Quick-action id: connect, preflight, db15, first-measure, recordings, start, shunt, invert, new-experiment, zero.</summary>
    public string? ActionId { get; init; }
    public string? ActionLabel { get; init; }
}

public sealed class AdvisorFeedbackStore
{
    public Dictionary<string, AdviceStats> Stats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<AdviceEvent> Recent { get; set; } = new();
}

public sealed class AdviceStats
{
    public int Helpful { get; set; }
    public int NotHelpful { get; set; }
    public int Shown { get; set; }
}

public sealed class AdviceEvent
{
    public string Utc { get; set; } = "";
    public string RuleId { get; set; } = "";
    public string StatusSnippet { get; set; } = "";
    public string? Feedback { get; set; }
}

/// <summary>UI preference for advisor panel visibility.</summary>
public sealed class AdvisorUiPrefs
{
    public bool ShowPanel { get; set; } = true;
}
