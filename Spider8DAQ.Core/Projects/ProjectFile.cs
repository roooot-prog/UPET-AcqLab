using Spider8DAQ.Core.Acquisition;
using Spider8DAQ.Core.Compute;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Display;
using Spider8DAQ.Core.Macros;
using Spider8DAQ.Core.MathChannels;

namespace Spider8DAQ.Core.Projects;

public sealed class ProjectFile
{
    public string Name { get; set; } = "Untitled";
    public string DeviceMode { get; set; } = "Simulator";
    public string? ComPort { get; set; }
    public int BaudRate { get; set; } = 9600;
    public int SampleRateHz { get; set; } = 50;
    public List<ChannelConfig> Channels { get; set; } = new();
    public List<MathChannelDefinition> MathChannels { get; set; } = new();
    public List<ComputeDefinition> ComputeChannels { get; set; } = new();
    public TriggerSettings Trigger { get; set; } = new();
    public AdvancedTriggerSettings AdvancedTrigger { get; set; } = new();
    public List<MacroDefinition> Macros { get; set; } = new();
    public ProjectMeta Meta { get; set; } = new();
    public RecordingSettings Recording { get; set; } = new();
    public List<DeviceSlot> Devices { get; set; } = new() { new() { Index = 0, Name = "Spider8 #1", Enabled = true } };
    public bool PanelMode { get; set; }
    public string PlotMode { get; set; } = "YT";
    public int XyXChannel { get; set; }
    public int XyYChannel { get; set; } = 1;
    public string? LastRecordingPath { get; set; }
    public string? LastProjectPath { get; set; }
    public DisplayTemplate? DisplayTemplate { get; set; }
    public double AlarmHysteresis { get; set; } = 0.05;
    public bool AlarmLatch { get; set; } = true;
}

public sealed class ProjectMeta
{
    public string Operator { get; set; } = "";
    public string SampleId { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Location { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string Backend { get; set; } = "";
    public int SampleRateHz { get; set; }
    public string ExperimentName { get; set; } = "";
    /// <summary>Semicolon-separated sensor IDs applied on enabled channels.</summary>
    public string SensorSummary { get; set; } = "";
    public string CalibrationNotes { get; set; } = "";
    public string DeviceEstHint { get; set; } = "";
    /// <summary>Number of channels used in the experiment (Enabled+RecordEnabled).</summary>
    public int ActiveChannelCount { get; set; }
    /// <summary>Structured channel + sensor details for reports.</summary>
    public List<ChannelReportInfo> Channels { get; set; } = new();
    /// <summary>Tip experiment (categorie senzori / preset).</summary>
    public string ExperimentType { get; set; } = "";
    /// <summary>
    /// Contur cilindru (plan + elevație + secțiune) — mapping senzori circumferențiali / forță / cursă.
    /// Set when <see cref="ExperimentType"/> is <see cref="ExperimentTypes.CylinderContour"/>.
    /// </summary>
    public CylinderContourConfig? CylinderContour { get; set; }
    /// <summary>Senzori planificați (rezumat text).</summary>
    public string PlannedSensors { get; set; } = "";
    /// <summary>Start experiment (ora locală, ISO).</summary>
    public string ExperimentStartLocal { get; set; } = "";
    /// <summary>Durată estimată [min]; 0 = nespecificat.</summary>
    public int EstimatedDurationMinutes { get; set; }
    /// <summary>Cale absolută la foto/schiță montaj înainte de experiment (JPEG/PNG).</summary>
    public string MontagePhotoPath { get; set; } = "";
    /// <summary>Cale absolută la foto probă după experiment (modificări survenite).</summary>
    public string MontagePhotoAfterPath { get; set; } = "";
    /// <summary>Observații operator — montaj înainte (opțional).</summary>
    public string MontageBeforeNotes { get; set; } = "";
    /// <summary>Observații operator — probă după (opțional).</summary>
    public string MontageAfterNotes { get; set; } = "";
    /// <summary>Moment captură montaj înainte (ora locală, ISO).</summary>
    public string MontageBeforeCapturedLocal { get; set; } = "";
    /// <summary>Moment captură probă după (ora locală, ISO).</summary>
    public string MontageAfterCapturedLocal { get; set; } = "";
    /// <summary>Închidere experiment (ora locală, ISO).</summary>
    public string ExperimentEndLocal { get; set; } = "";
    /// <summary>Lungime probă [mm]; 0 = nespecificat.</summary>
    public double SampleLengthMm { get; set; }
    /// <summary>Lățime probă [mm]; 0 = nespecificat.</summary>
    public double SampleWidthMm { get; set; }
    /// <summary>Grosime / înălțime probă [mm]; 0 = nespecificat.</summary>
    public double SampleThicknessMm { get; set; }
    /// <summary>Diametru probă [mm] (cilindru); 0 = nespecificat.</summary>
    public double SampleDiameterMm { get; set; }
    /// <summary>Diametru interior tub [mm]; 0 = epruvă plină (secțiune hașurată până la axă).</summary>
    public double SampleInnerDiameterMm { get; set; }
    /// <summary>Secțiune transversală [mm²]; 0 = auto din L×l / Ø.</summary>
    public double SampleAreaMm2 { get; set; }
    /// <summary>Greutate probă [g]; 0 = nespecificat (opțional).</summary>
    public double SampleMassG { get; set; }
    /// <summary>Rezumat dimensiuni + greutate (pentru afișare / export).</summary>
    public string SampleDimensionsSummary { get; set; } = "";
    /// <summary>Catalog specimen id (compression experiments only); empty = none selected.</summary>
    public string SpecimenId { get; set; } = "";
    /// <summary>Display name (ro), e.g. Halit (sare gemă).</summary>
    public string SpecimenNameRo { get; set; } = "";
    /// <summary>Metal / Roca / Sare / Beton / Personalizat.</summary>
    public string SpecimenClass { get; set; } = "";
    /// <summary>Formula pack id (SteelMetal, IsrmUcs, SaltCreep, En12390).</summary>
    public string SpecimenFormulaPack { get; set; } = "";
    /// <summary>Pack label for reports, e.g. Sare (fluaj).</summary>
    public string SpecimenFormulaPackLabel { get; set; } = "";
    /// <summary>One-liner: Epruvetă: Halit (sare gemă) · pachet Sare (fluaj).</summary>
    public string SpecimenSummary { get; set; } = "";
    public string SpecimenStandardNote { get; set; } = "";
    public string SpecimenStrengthNotes { get; set; } = "";
    public string SpecimenShape { get; set; } = "";
    /// <summary>Young E [GPa] from specimen card (indicative); 0 = unset.</summary>
    public double SpecimenYoungGPa { get; set; }
    /// <summary>Material Poisson ν from specimen card (indicative); 0 = unset.</summary>
    public double SpecimenPoissonNu { get; set; }
    /// <summary>Density [kg/m³] indicative; 0 = unset.</summary>
    public double SpecimenDensityKgM3 { get; set; }
    /// <summary>Optional notes (T, humidity, operator) — salt creep placeholders live here.</summary>
    public string SpecimenNotes { get; set; } = "";
    /// <summary>ν aparent (Poisson) din ε_t vs ε_l; NaN = nesetat.</summary>
    public double ApparentPoissonNu { get; set; } = double.NaN;
    /// <summary>Detalii fereastră / R² pentru ν aparent (opțional).</summary>
    public string ApparentPoissonSummary { get; set; } = "";
    /// <summary>Recuperare elastică / deformație permanentă (text raport, opțional).</summary>
    public string ElasticRecoverySummary { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Measurement fingerprint code (UPET-XXXX-XXXX-XXXX).</summary>
    public string MeasurementFingerprint { get; set; } = "";
    /// <summary>SHA-256 hex of sealed payload (CSV bytes or .upet sample pack).</summary>
    public string FingerprintPayloadSha256 { get; set; } = "";
    /// <summary>Fingerprint algorithm id (e.g. UPET-FP-v1).</summary>
    public string FingerprintAlgo { get; set; } = "";
}

public sealed class RecordingSettings
{
    public int PreTriggerSamples { get; set; } = 50;
    public bool AppendMode { get; set; }
    public bool AutoFileName { get; set; } = true;
    public string FileNamePattern { get; set; } = "{sample}_{yyyyMMdd}_{HHmmss}";
    /// <summary>Full = every sample; PeakInterval = Min/Max per interval (lab long-term).</summary>
    public string StorageMode { get; set; } = "Full";
    /// <summary>Window length for PeakInterval storage (seconds).</summary>
    public double PeakIntervalSeconds { get; set; } = 1.0;
}

public sealed class DeviceSlot
{
    public int Index { get; set; }
    public string Name { get; set; } = "Spider8";
    public bool Enabled { get; set; } = true;
    public string? ComPort { get; set; }
    public string? Notes { get; set; }
    public bool TedsScanRequested { get; set; }
}

public sealed class TriggerSettings
{
    public bool Enabled { get; set; }
    public int ChannelIndex { get; set; }
    public double Threshold { get; set; }
    public bool RisingEdge { get; set; } = true;
}
