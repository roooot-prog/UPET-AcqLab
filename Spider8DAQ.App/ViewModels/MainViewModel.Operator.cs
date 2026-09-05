using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Licensing;

namespace Spider8DAQ.App.ViewModels;

/// <summary>
/// Industrial operator mode: status strip, guided workflows (tensometrie / presă), About panel.
/// </summary>
public partial class MainViewModel
{
    public sealed class WorkflowStepItem
    {
        public int StepNumber { get; init; }
        public string Title { get; init; } = "";
        public string Detail { get; init; } = "";
        public bool IsComplete { get; set; }
        public bool IsCurrent { get; set; }
        public string Glyph => IsComplete ? "✓" : IsCurrent ? "▶" : "○";
    }

    public sealed class OperatorWorkflowTemplate
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Description { get; init; } = "";
    }

    private static readonly OperatorWorkflowTemplate[] WorkflowCatalog =
    [
        new()
        {
            Id = "strain",
            Name = "Tensometrie / deformații",
            Description = "Y(t) µm/m — conectare, Apply senzor tensiune, Zero, Start, Record, Export Excel."
        },
        new()
        {
            Id = "hydraulic-press",
            Name = "Presă hidraulică UPET",
            Description = "Y(t) bar + cadran kg — P15, aria piston 201,06 cm², Zero la aer, Record, raport."
        },
        new()
        {
            Id = "generic",
            Name = "Măsurătoare generală",
            Description = "Flux standard: meta → senzori → Connect → Zero → Live → Record → Export."
        }
    ];

    private string _selectedWorkflowId = "strain";
    private bool _sessionZeroed;
    private bool _showAboutPanel;
    private string _deviceErrorText = "EST: —";
    private Brush _deviceErrorBrush = OperatorBrushOk;
    private string _recordingInfoText = "";
    private string _operatorStatusSummary = "";
    private string _statusLineCompact = "EST · — · — · —";
    private string _statusDetailTooltip = "";
    private int _recordingSampleCount;

    private static readonly Brush OperatorBrushOk = FreezeOpBrush(0x07, 0x6B, 0x35);
    private static readonly Brush OperatorBrushWarn = FreezeOpBrush(0xC4, 0x8A, 0x00);
    private static readonly Brush OperatorBrushErr = FreezeOpBrush(0xB0, 0x0C, 0x28);
    private static readonly Brush OperatorBrushOff = FreezeOpBrush(0x6E, 0x78, 0x84);
    private static readonly Brush OperatorBrushLive = FreezeOpBrush(0x00, 0x66, 0xB3);
    private static readonly Brush OperatorBrushRec = FreezeOpBrush(0xB0, 0x0C, 0x28);

    private static Brush FreezeOpBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public ObservableCollection<WorkflowStepItem> WorkflowSteps { get; } = new();
    public ObservableCollection<OperatorWorkflowTemplate> WorkflowTemplates { get; } =
        new(WorkflowCatalog);

    public ICommand ApplyWorkflowPresetCommand { get; private set; } = null!;
    public ICommand SyncWorkflowCommand { get; private set; } = null!;
    public ICommand ToggleAboutPanelCommand { get; private set; } = null!;
    public ICommand OpenRecordingsFromAboutCommand { get; private set; } = null!;

    public string SelectedWorkflowId
    {
        get => _selectedWorkflowId;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == _selectedWorkflowId) return;
            _selectedWorkflowId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedWorkflow));
            RefreshWorkflowStepsOnly();
        }
    }

    public OperatorWorkflowTemplate? SelectedWorkflow =>
        WorkflowTemplates.FirstOrDefault(w => w.Id == SelectedWorkflowId);

    public bool ShowAboutPanel
    {
        get => _showAboutPanel;
        set { _showAboutPanel = value; OnPropertyChanged(); }
    }

    public string AppVersion { get; } =
        ApplicationKeyIdentity.LocalFileVersion() is { Length: > 0 } v ? v : "3.3.106";

    public string AppInfoText { get; } =
        $"{ReportHeaderHelper.ProductName} — laborator achiziție date HBM Spider8\n" +
        $"{ReportHeaderHelper.UniversityName}\n" +
        $"{ReportHeaderHelper.FacultyName}\n" +
        $"{ReportHeaderHelper.DepartmentName}\n" +
        $"{ReportHeaderHelper.ExpertLine}\n" +
        $"{ReportHeaderHelper.AuthorLine}";

    public string DriverHint { get; } =
        "Drivere: USBHBM (VID_10D1) + Spider32.dll / Intfac32 (HBM DEST).\n" +
        "Backend recomandat hardware: Spider32.dll sau HBM USB.\n" +
        "Zero = software-only (fără TAR hardware — păstrează stream P15/DcVoltage).";

    public string DeviceErrorText
    {
        get => _deviceErrorText;
        set { _deviceErrorText = value; OnPropertyChanged(); }
    }

    public Brush DeviceErrorBrush
    {
        get => _deviceErrorBrush;
        set { _deviceErrorBrush = value; OnPropertyChanged(); }
    }

    public string RecordingInfoText
    {
        get => _recordingInfoText;
        set { _recordingInfoText = value; OnPropertyChanged(); }
    }

    public int RecordingSampleCount
    {
        get => _recordingSampleCount;
        set { _recordingSampleCount = value; OnPropertyChanged(); }
    }

    public string OperatorStatusSummary
    {
        get => _operatorStatusSummary;
        set { _operatorStatusSummary = value; OnPropertyChanged(); }
    }

    /// <summary>One-line instrument status: EST · Rate · Filter · Scale (+ short alerts / Δ).</summary>
    public string StatusLineCompact
    {
        get => _statusLineCompact;
        private set { _statusLineCompact = value; OnPropertyChanged(); }
    }

    /// <summary>Full help + status prose for status-bar tooltip (not shown inline).</summary>
    public string StatusDetailTooltip
    {
        get => _statusDetailTooltip;
        private set { _statusDetailTooltip = value; OnPropertyChanged(); }
    }

    /// <summary>Short cursor Δ snippet for the status strip (empty if none).</summary>
    public string CursorStatusShort
    {
        get
        {
            var t = CursorText;
            if (string.IsNullOrWhiteSpace(t)) return "";
            var idx = t.IndexOf('Δ');
            if (idx < 0) idx = t.IndexOf("delta", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return "";
            var snippet = t[idx..].Trim();
            return snippet.Length > 28 ? snippet[..25] + "…" : snippet;
        }
    }

    public bool OperatorPillConnected => IsConnected;
    public bool OperatorPillLive => IsStreaming;
    public bool OperatorPillRecording => IsRecording;
    public bool OperatorPillZeroed => _sessionZeroed;
    public bool OperatorPillSensor => Channels.Any(c => c.Enabled && !string.IsNullOrWhiteSpace(c.SensorName));

    public Brush OperatorPillConnectedBrush => IsConnected ? OperatorBrushOk : OperatorBrushOff;
    public Brush OperatorPillLiveBrush => IsStreaming ? OperatorBrushLive : OperatorBrushOff;
    public Brush OperatorPillRecordingBrush => IsRecording ? OperatorBrushRec : OperatorBrushOff;
    public Brush OperatorPillZeroedBrush => _sessionZeroed ? OperatorBrushOk : OperatorBrushWarn;
    public Brush OperatorPillSensorBrush => OperatorPillSensor ? OperatorBrushOk : OperatorBrushWarn;

    private void WireOperatorCommands()
    {
        ApplyWorkflowPresetCommand = new RelayCommand(ApplySelectedWorkflowPreset);
        SyncWorkflowCommand = new RelayCommand(RefreshWorkflowStepsOnly);
        ToggleAboutPanelCommand = new RelayCommand(() =>
        {
            ShowAboutPanel = !ShowAboutPanel;
            if (ShowAboutPanel)
                HelpPanelText = "Despre UPET AcqLab — versiune, drivere, folder înregistrări.";
        });
        OpenRecordingsFromAboutCommand = new RelayCommand(OpenRecordingsFolder);
        RefreshWorkflowStepsOnly();
        RefreshOperatorStatus();
    }

    private void ApplySelectedWorkflowPreset()
    {
        switch (SelectedWorkflowId)
        {
            case "hydraulic-press":
                SelectedExperiment = ExperimentPresets.FirstOrDefault(p => p.Id == "hydraulic-press");
                ApplyHydraulicPressCommand.Execute(null);
                HelpPanelText = SelectedWorkflow?.Description ?? "";
                Status = "Workflow: Presă hidraulică UPET — configurație aplicată.";
                break;
            case "strain":
                SelectedExperiment = ExperimentPresets.FirstOrDefault(p => p.Id == "strain");
                PlotMode = DisplayLayouts.Yt;
                FollowLiveZoom = true;
                HelpPanelText = "Workflow tensometrie: Apply senzor pe CH0 (Half bridge), Zero (F9), Start, Record.";
                Status = "Workflow: Tensometrie — Y(t) µm/m recomandat.";
                break;
            default:
                HelpPanelText = SelectedWorkflow?.Description ?? "";
                Status = "Workflow general — urmați pașii din checklist.";
                break;
        }
        WorkspaceMode = ModeLive;
        RefreshWorkflowStepsOnly();
        RefreshOperatorStatus();
        _journal.Setup(Status);
    }

    public void RefreshWorkflowStepsOnly()
    {
        WorkflowSteps.Clear();
        var steps = BuildWorkflowStepDefinitions();
        var current = ResolveWorkflowCurrentStep();
        for (var i = 0; i < steps.Count; i++)
        {
            WorkflowSteps.Add(new WorkflowStepItem
            {
                StepNumber = i + 1,
                Title = steps[i].Title,
                Detail = steps[i].Detail,
                IsComplete = i < current,
                IsCurrent = i == current
            });
        }
    }

    private void RefreshWorkflowSteps()
    {
        RefreshWorkflowStepsOnly();
        RefreshOperatorStatus();
    }

    private int ResolveWorkflowCurrentStep()
    {
        if (!string.IsNullOrWhiteSpace(LastRecordingPath) && File.Exists(LastRecordingPath))
            return 6;
        if (IsRecording) return 5;
        if (IsStreaming && _sessionZeroed) return 4;
        if (IsStreaming) return 3;
        if (IsConnected && OperatorPillSensor) return 2;
        if (IsConnected) return 1;
        if (!string.IsNullOrWhiteSpace(OperatorName) || !string.IsNullOrWhiteSpace(SampleId))
            return 0;
        return 0;
    }

    private IReadOnlyList<(string Title, string Detail)> BuildWorkflowStepDefinitions()
    {
        if (SelectedWorkflowId == "hydraulic-press")
        {
            return
            [
                ("Meta sesiune", "Operator, probă, comentariu (Instrumente / meta)."),
                ("Conectare Spider8", "Backend Spider32.dll / HBM USB → Conectare."),
                ("Apply P15 + aria piston", "Bibliotecă P15RVA/1/200B → Apply CH; aria 201,06 cm²."),
                ("Zero la aer (F9)", "Software zero — fără TAR hardware."),
                ("Start live (F5)", "Y(t) bar + cadran kg."),
                ("Record (F7)", $"CSV în {GetWritableRecordingsDirectory()}"),
                ("Export Excel", "Meniu Export → raport complet cu grafic.")
            ];
        }

        if (SelectedWorkflowId == "strain")
        {
            return
            [
                ("Meta sesiune", "Operator, probă, comentariu."),
                ("Conectare + rată", $"SampleRateHz={SampleRateHz}, filtru pe CH."),
                ("Apply senzor tensiune", "Half bridge, GF≈2 → µm/m pe CH activ."),
                ("Zero balance (F9)", "Software zero pe canalele ON."),
                ("Start live (F5)", "Y(t) cu urmărire live."),
                ("Record (F7)", "Durată / eșantioane / trigger."),
                ("Export Excel", "Raport + foaia Grafic (toată măsurătoarea).")
            ];
        }

        return
        [
            ("Meta", "Operator / probă / comentariu."),
            ("Configure canale", "On, senzori, alarme."),
            ("Conectare", "Backend + Connect."),
            ("Zero", "F9 — tare software."),
            ("Live", "F5 Start streaming."),
            ("Record", "F7 — salvează CSV."),
            ("Review + Export", "Mod Review, Excel/PDF.")
        ];
    }

    public void RefreshOperatorStatus()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(RefreshOperatorStatus);
            return;
        }

        OnPropertyChanged(nameof(OperatorPillConnected));
        OnPropertyChanged(nameof(OperatorPillLive));
        OnPropertyChanged(nameof(OperatorPillRecording));
        OnPropertyChanged(nameof(OperatorPillZeroed));
        OnPropertyChanged(nameof(OperatorPillSensor));
        OnPropertyChanged(nameof(OperatorPillConnectedBrush));
        OnPropertyChanged(nameof(OperatorPillLiveBrush));
        OnPropertyChanged(nameof(OperatorPillRecordingBrush));
        OnPropertyChanged(nameof(OperatorPillZeroedBrush));
        OnPropertyChanged(nameof(OperatorPillSensorBrush));

        var enabled = Channels.Count(c => c.Enabled);
        OperatorStatusSummary =
            (_communicationLost && !IsConnected
                ? "Comunicare pierdută"
                : (IsConnected ? "Conectat" : "Deconectat")) +
            (IsStreaming ? " · Live" : "") +
            (DisplayFrozen ? " · HOLD" : "") +
            (IsRecording ? " · REC" : "") +
            (_sessionZeroed ? " · Zero" : "") +
            (OperatorPillSensor ? " · Senzor" : "") +
            $" · {enabled} CH · {SampleRateHz} Hz";

        if (IsRecording)
        {
            RecordingSampleCount = (int)_engine.RecordedSamples;
            var recN = Channels.Count(c => c.Enabled && c.RecordEnabled);
            RecordingInfoText =
                $"REC · {RecordingSampleCount:N0} eșantioane · {SampleRateHz} Hz · {recN}/{enabled} Rec · {StorageMode} · {LastRecordingPath}";
        }
        else
            RecordingInfoText = "";

        RefreshStatusLineCompact();
    }

    /// <summary>Rebuild one-line status (visual only — does not change DAQ).</summary>
    public void RefreshStatusLineCompact()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(RefreshStatusLineCompact);
            return;
        }

        var est = CompactEstToken();
        var rate = $"{SampleRateHz} Hz";
        var filter = CompactFilterToken();
        var scale = CompactScaleToken();

        var parts = new List<string> { est, rate, filter, scale };

        var cursor = CursorStatusShort;
        if (!string.IsNullOrEmpty(cursor))
            parts.Add(cursor);

        var alert = CompactCriticalAlertToken();
        if (!string.IsNullOrEmpty(alert))
            parts.Add(alert);

        StatusLineCompact = string.Join(" · ", parts);

        var tipParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(HelpPanelText))
            tipParts.Add(HelpPanelText.Trim());
        if (!string.IsNullOrWhiteSpace(Status) && !string.Equals(Status, HelpPanelText, StringComparison.Ordinal))
            tipParts.Add(Status.Trim());
        if (!string.IsNullOrWhiteSpace(MetrologyDiagText))
            tipParts.Add(MetrologyDiagText.Trim());
        if (!string.IsNullOrWhiteSpace(CursorText))
            tipParts.Add(CursorText.Trim());
        StatusDetailTooltip = tipParts.Count == 0
            ? StatusLineCompact
            : string.Join(Environment.NewLine + Environment.NewLine, tipParts);

        OnPropertyChanged(nameof(CursorStatusShort));
    }

    private string CompactEstToken()
    {
        var t = DeviceErrorText ?? "";
        if (string.IsNullOrWhiteSpace(t) || t is "EST: —" or "EST:—")
            return "EST: —";
        if (t.Contains("LED ERROR", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Power-cycle", StringComparison.OrdinalIgnoreCase))
            return "EST: ERR";
        if (t.Contains("10000", StringComparison.Ordinal) || t.Contains("reset", StringComparison.OrdinalIgnoreCase))
            return "EST: WARN";
        // Prefer short EST code if present
        var m = System.Text.RegularExpressions.Regex.Match(t, @"EST[^0-9A-Za-z]*([0-9A-Za-z\-]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success)
            return "EST:" + m.Groups[1].Value;
        return t.Length <= 18 ? t : "EST: …";
    }

    private string CompactFilterToken()
    {
        var kind = string.IsNullOrWhiteSpace(MetrologyFilterKind) ? "—" : MetrologyFilterKind;
        if (kind.Equals("Off", StringComparison.OrdinalIgnoreCase))
            return "Flt:Off";
        var avg = Channels.Where(c => c.Enabled).Select(c => c.FilterHz).DefaultIfEmpty(0).Average();
        if (avg <= 0)
            return $"Flt:{kind}";
        return $"Flt:{kind} {avg:0.#}";
    }

    private string CompactScaleToken()
    {
        var suspect = _engine.Metrology.LastScaleSuspectMessage;
        if (!string.IsNullOrWhiteSpace(suspect)
            || (!string.IsNullOrWhiteSpace(MetrologyDiagText)
                && MetrologyDiagText.Contains("suspect", StringComparison.OrdinalIgnoreCase)))
            return "Scale!";
        return "Scale OK";
    }

    private string CompactCriticalAlertToken()
    {
        var s = Status ?? "";
        if (string.IsNullOrWhiteSpace(s)) return "";
        if (s.Contains("LED ERROR", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Power-cycle", StringComparison.OrdinalIgnoreCase)
            || s.Contains("eșuat", StringComparison.OrdinalIgnoreCase)
            || s.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Scale suspect", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Scale!", StringComparison.OrdinalIgnoreCase))
        {
            var one = s.Replace("\r\n", " ").Replace('\n', ' ').Trim();
            // Drop long DB15 / cablare essays from the strip
            if (one.Contains("DB15", StringComparison.OrdinalIgnoreCase)
                || one.Contains("cablare", StringComparison.OrdinalIgnoreCase))
                return "Alertă · vezi tip";
            return one.Length > 42 ? one[..39] + "…" : one;
        }
        return "";
    }

    internal void MarkSessionZeroed()
    {
        _sessionZeroed = true;
        RefreshOperatorStatus();
        try { RefreshLabAdvisorFromState(force: true); } catch { /* ignore */ }
    }

    internal void ResetSessionZeroed()
    {
        _sessionZeroed = false;
        RefreshOperatorStatus();
    }

    internal void ParseDeviceEstFromStatus(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return;
        // Shunt/Preflight recovery text mentions EST=10003 / LED Error — not a live EST? reply.
        if (msg.Contains("Sugestie:", StringComparison.Ordinal)
            && (msg.StartsWith("Shunt ", StringComparison.OrdinalIgnoreCase)
                || msg.StartsWith("Shunt all", StringComparison.OrdinalIgnoreCase)
                || msg.StartsWith("Preflight", StringComparison.OrdinalIgnoreCase)))
            return;
        // Avoid false positives: "Experiment" / "estimat" are not device EST lines.
        var looksLikeEst =
            msg.Contains("EST:", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("EST ", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("LED ERROR", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Power-cycle", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeEst) return;

        DeviceErrorText = msg.Length > 120 ? msg[..117] + "…" : msg;
        if (msg.Contains("LED ERROR", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Power-cycle", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("1000", StringComparison.Ordinal) && !msg.Contains("10000"))
            DeviceErrorBrush = OperatorBrushErr;
        else if (msg.Contains("10000", StringComparison.Ordinal) || msg.Contains("reset", StringComparison.OrdinalIgnoreCase))
            DeviceErrorBrush = OperatorBrushWarn;
        else
            DeviceErrorBrush = OperatorBrushOk;

        RefreshStatusLineCompact();
    }
}
