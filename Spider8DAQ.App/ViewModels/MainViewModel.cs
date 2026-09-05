using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Media;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClosedXML.Excel;
using Microsoft.Win32;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Acquisition;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Journal;
using Spider8DAQ.Core.Licensing;
using Spider8DAQ.Core.Macros;
using Spider8DAQ.Core.MathChannels;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Sensors;
using Spider8DAQ.Core.Simulation;
using Spider8DAQ.Core.Time;
using Spider8DAQ.Hardware;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel : INotifyPropertyChanged, IAsyncDisposable, MacroRunner.IMacroHost
{
    private readonly AcquisitionEngine _engine = new();
    private readonly MacroRunner _macroRunner = new();
    private readonly Dispatcher _dispatcher;
    private readonly AppJournal _journal;
    private ISpider8Device? _device;
    private SensorLibrary _sensorLibrary = SensorLibrary.CreateDefault();
    private OfflineSession? _offline;
    private CancellationTokenSource? _macroCts;
    private TaskCompletionSource? _macroOperatorWait;
    private ProjectFile _project = new();

    private string _status = "Gata.";
    private string _cursorText = "Cursor: —";
    private string _pcClockText = "";
    private string _pcClockTooltip = "";
    private DispatcherTimer? _pcClockTimer;
    private string _selectedBackend = "Simulator";
    private string? _selectedPort;
    private string _connectedDeviceLabel = "Deconectat";
    private string _hardwareSummary = "Dispozitive: —";
    private int _sampleRateHz = 50;
    private bool _isConnected;
    private bool _communicationLost;
    private int _handlingConnectionLost;
    private bool _isConnecting;
    private double _connectProgress;
    private string _connectStageText = "";
    private System.Windows.Media.Brush _connectionStatusBrush =
        System.Windows.Media.Brushes.Gray;
    private bool _isStreaming;
    private bool _isRecording;
    private bool _exportBusy;
    private string _projectName = "Untitled";
    private double _triggerThreshold;
    private bool _triggerEnabled;
    private int _triggerChannel = 1;
    private bool _triggerRising = true;
    private int? _maxSamples;
    private int? _maxSeconds;
    private string _lastRecordingPath = "";
    private string? _selectedSensorName;
    private string _selectedSensorCategory = "(Toate)";
    private string _sensorSearchText = "";
    private string _sensorLibrarySummary = "Sensor library";
    private SensorListRow? _selectedSensorRow;
    private string _editSensorName = "";
    private string _editSensorUnit = "mV/V";
    private double _editSensorScale = 1;
    private string _editSensorBridge = "Full";
    private string _editSensorNotes = "";
    private double _editSensorExcitationV = 2.5;
    private double _editSensorShuntKohm;
    private int _selectedChannelForSensor = 2;
    private ChannelRow? _selectedChannelRow;
    private int _smoothWindow = 5;
    private int _cutStart;
    private int _cutEnd = 100;
    private int _analysisChannel;
    private int _macroRecordMs = 5000;
    private int _macroSettleMs = 1000;
    private string _analysisSummary = "Load a CSV in Analysis.";
    private string _measurementFingerprint = "";
    private string _fingerprintStatusText = "";
    private Brush _fingerprintStatusBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x7D, 0x8B));
    private string _deltaText = "Δt=—  Δy=—";
    private bool _panelMode;
    private string _plotMode = DisplayLayouts.Yt;
    private bool _infoMode;
    private bool _fullscreenPanel;
    private string _helpPanelText = "Activează Info (butonul „i”) apoi treci cu mouse-ul peste un buton pentru explicații.";
    private ExperimentPreset? _selectedExperiment;
    private string _plotModeDescription = DisplayLayouts.Describe(DisplayLayouts.Yt);
    private bool _followLiveZoom = true;
    private int _xyXChannel = 1;
    private int _xyYChannel = 2;
    private int _fftChannel = 1;
    private int _preTriggerSamples = 50;
    private bool _appendMode;
    private bool _autoFileName = true;
    private string _operatorName = "";
    private string _sampleId = "";
    private string _comment = "";
    private string _labLocation = "Universitatea din Petroșani — Facultatea de Inginerie Mecanică și Electrică / Departamentul de Inginerie Mecanică, Industrială și Transporturi";
    private string _experimentType = "";
    private Spider8DAQ.Core.Projects.CylinderContourConfig? _cylinderContour;
    private bool _contourSimDemoEnabled;
    private Spider8DAQ.Core.Simulation.ContourSimAssignment? _contourSimAssignment;
    private string _plannedSensors = "";
    private double _sampleLengthMm;
    private double _sampleWidthMm;
    private double _sampleThicknessMm;
    private double _sampleDiameterMm;
    private double _sampleAreaMm2;
    private double _sampleMassG;
    private string _sampleDimensionsSummary = "";
    private string _specimenId = "";
    private string _specimenNameRo = "";
    private string _specimenClass = "";
    private string _specimenFormulaPack = "";
    private string _specimenFormulaPackLabel = "";
    private string _specimenSummary = "";
    private string _specimenStandardNote = "";
    private string _specimenStrengthNotes = "";
    private string _specimenShape = "";
    private double _specimenYoungGPa;
    private double _specimenPoissonNu;
    private double _specimenDensityKgM3;
    private string _specimenNotes = "";
    private DateTime? _experimentStartedAt;
    private DateTime? _experimentEndedAt;
    private int _estimatedDurationMinutes;
    private string _montagePhotoPath = "";
    private string _montagePhotoAfterPath = "";
    private string _montageBeforeNotes = "";
    private string _montageAfterNotes = "";
    private DateTime? _montageBeforeCapturedAt;
    private DateTime? _montageAfterCapturedAt;
    private bool _isExperimentActive;
    private int _cursorA;
    private int _cursorB;
    private DateTime _lastBeep = DateTime.MinValue;

    private ScottPlot.Plottables.Scatter? _xyScatter;
    private readonly List<double> _xyXs = new();
    private readonly List<double> _xyYs = new();
    private readonly Queue<double> _fftBuffer = new();
    private readonly Dictionary<int, double> _barValues = new();
    private const int FftBufferSize = 512;
    private readonly Dictionary<int, ScottPlot.Plottables.DataLogger> _loggersA = new();
    private readonly Dictionary<int, ScottPlot.Plottables.DataLogger> _loggersB = new();
    private readonly Dictionary<int, Queue<double>> _liveYValueByChannel = new();
    private readonly Dictionary<int, double> _channelPlotOffsets = new();
    private bool _plotStackedMode;
    private bool _plotRebuildQueued;
    private bool _suppressPlotRebuild;
    private bool _channelConfigPushQueued;
    private bool _channelScalarsPushQueued;
    private bool _suppressChannelConfigPush;
    private ScottPlot.WPF.WpfPlot? _plotA;
    private ScottPlot.WPF.WpfPlot? _plotB;
    private ScottPlot.WPF.WpfPlot? _plotAnalysis;
    private ScottPlot.Plottables.Crosshair? _crosshairA;
    private ScottPlot.Plottables.Crosshair? _crosshairB;
    private bool _replayMode;

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        try
        {
            AppPaths.EnsureWritable(AppPaths.Logs);
        }
        catch { /* ignore */ }

        AppJournal? journal = null;
        try
        {
            journal = new AppJournal(AppPaths.AppLog);
        }
        catch
        {
            journal = new AppJournal(Path.Combine(Path.GetTempPath(), "spider8daq.log"));
        }
        _journal = journal;

        Backends = new ObservableCollection<string> { "Simulator", "Serial", "Spider32.dll", "HBM USB" };
        try { Ports = new ObservableCollection<string>(HbmUsbDeviceScanner.GetConnectionTargets()); }
        catch
        {
            try { Ports = new ObservableCollection<string>(ComPortScanner.GetPorts()); }
            catch { Ports = new ObservableCollection<string>(); }
        }
        Channels = new ObservableCollection<ChannelRow>();
        MathChannels = new ObservableCollection<MathChannelRow>();
        LiveValues = new ObservableCollection<LiveValueRow>();
        SensorNames = new ObservableCollection<string>();
        SensorRows = new ObservableCollection<SensorListRow>();
        SensorGroups = new ObservableCollection<SensorCategoryGroup>();
        SensorCategories = new ObservableCollection<string>();
        AnalysisStats = new ObservableCollection<StatsRow>();
        PeakIndexes = new ObservableCollection<string>();
        Devices = new ObservableCollection<DeviceRow> { new() { Index = 0, Name = "Spider8_1", Enabled = true } };
        MacroSteps = new ObservableCollection<MacroStepRow>();
        JournalLines = new ObservableCollection<string>();
        BridgeTypes = new ObservableCollection<string>(Enum.GetNames(typeof(BridgeType)));
        MathOps = new ObservableCollection<string>(Enum.GetNames(typeof(MathOp)));
        PlotModes = new ObservableCollection<string>(DisplayLayouts.All);
        ExperimentPresets = new ObservableCollection<ExperimentPreset>(ExperimentCatalog.All);
        SelectedExperiment = ExperimentPresets.FirstOrDefault(p => p.Id == "strain");

        // Keep Simulator as default even when USBHBM is plugged — demo path must stay usable.
        if (Ports.Count > 0 && SelectedPort is null)
            SelectedPort = Ports[0];

        try
        {
            WireCommands();
            WireAdvancedCommands();
            WireProCommands();
            WireEasyCommands();
            WireLabCommands();
            WireStrainAnalysisCommands();
            WireSensorVerifyCommands();
            WireHydraulicPressCommands();
            WireWorkspaceCommands();
            WireOperatorCommands();
            WireUpdateCommands();
            WireHmiPrefs();
            WireV3FeatureCommands();
            WireTimbruCommands();
            WireAdvisorCommands();
            WireMetrologyCommands();
        }
        catch (Exception ex)
        {
            Status = "Init comenzi: " + ex.Message;
        }

        // Example projects after window load (avoid write failures blocking startup).
        _engine.SampleProcessed += OnSampleProcessed;
        _engine.ScaleSuspect += (_, alert) => _dispatcher.BeginInvoke(() => OnMetrologyScaleSuspect(alert));
        _engine.RecordingStopped += (_, _) => _dispatcher.Invoke(async () =>
        {
            IsRecording = false;
            UnlockStrainAutorangeAfterRecord();
            var path = LastRecordingPath;
            var n = _engine.RecordedSamples;
            var exists = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            var bytes = exists ? new FileInfo(path).Length : 0;
            if (!exists)
                Status = $"Înregistrare oprită ({n} eșantioane) — fișier lipsă. Folder: {GetWritableRecordingsDirectory()}";
            else if (bytes == 0 || n == 0)
                Status = $"Înregistrare goală (0 eșantioane). CSV: {path}";
            else
                Status = $"Înregistrare oprită ({n} eșantioane). CSV: {path}";
            _journal.Info(Status);
            if (exists)
                await CatalogRecordingAsync(path, (int)n);
            if (exists && bytes > 0 && n > 0)
            {
                try
                {
                    var meta = CurrentProjectMeta();
                    meta.Comment = Comment ?? "";
                    var seal = Core.Export.MeasurementFingerprint.SealCsvFile(path!, meta);
                    MeasurementFingerprint = seal.Code;
                    FingerprintStatusText = "Amprentă generată · " + seal.Code;
                    SetFingerprintStatusBrush("#2E7D32");
                    Status = $"Înregistrare oprită ({n} eșantioane). Amprentă: {seal.Code} · CSV: {path}";
                    _journal.Info("MeasurementFingerprint=" + seal.Code);
                }
                catch (Exception ex)
                {
                    _journal.Warn("Amprentă CSV: " + ex.Message);
                }
            }
            await OnRecordingStoppedForIntegrationsAsync(path);
            RefreshRecordings();
            RefreshWorkflowStepsOnly();
            RefreshOperatorStatus();
        });
        _engine.AlarmRaised += (_, msg) => _dispatcher.Invoke(() =>
        {
            Status = msg;
            _journal.Warn(msg);
            if ((DateTime.UtcNow - _lastBeep).TotalMilliseconds > 800)
            {
                SystemSounds.Exclamation.Play();
                _lastBeep = DateTime.UtcNow;
            }
        });
        _journal.EntryAdded += (_, e) => _dispatcher.Invoke(() =>
        {
            var t = AppClock.ToPcLocal(e.Timestamp);
            JournalLines.Insert(0, $"{t:HH:mm:ss} [{e.Level}] {e.Message}");
            while (JournalLines.Count > 400) JournalLines.RemoveAt(JournalLines.Count - 1);
            OnPropertyChanged(nameof(LastJournalLine));
            if (e.Level == LogLevel.Error)
                ApplicationKeyHeartbeat.NotifyUrgentError();
        });

        StartPcClockTimer();

        try
        {
            SeedDefaultChannels(8);
            LoadLiveGaugeSettings();
            LoadChannelPlotSettings();
            LoadDefaultMacroSteps();
            _ = LoadSensorsAsync();
            RefreshHardwareSummary();
            RefreshConnectedDeviceLabel();
            _journal.Info("UPET AcqLab pornit.");
        }
        catch (Exception ex)
        {
            Status = "Init: " + ex.Message;
        }
    }

    /// <summary>Used only if the primary constructor path somehow fails before wiring.</summary>
    public static MainViewModel CreateSafeFallback() => new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> Backends { get; }
    public ObservableCollection<string> Ports { get; }
    public ObservableCollection<ChannelRow> Channels { get; }
    public ObservableCollection<MathChannelRow> MathChannels { get; }
    public ObservableCollection<LiveValueRow> LiveValues { get; }
    public ObservableCollection<string> SensorNames { get; }
    public ObservableCollection<SensorListRow> SensorRows { get; }
    public ObservableCollection<SensorCategoryGroup> SensorGroups { get; }
    public ObservableCollection<string> SensorCategories { get; }
    public ObservableCollection<StatsRow> AnalysisStats { get; }
    public ObservableCollection<string> PeakIndexes { get; }
    public ObservableCollection<DeviceRow> Devices { get; }
    public ObservableCollection<MacroStepRow> MacroSteps { get; }
    public ObservableCollection<string> JournalLines { get; }

    public IReadOnlyList<LogEntry> JournalSnapshot(int maxLines = 30) => _journal.TakeLast(maxLines);

    public LogEntry? JournalLastError() => _journal.LastErrorOrWarning();
    public ObservableCollection<string> BridgeTypes { get; }
    public ObservableCollection<string> MathOps { get; }
    public ObservableCollection<string> PlotModes { get; }
    public ObservableCollection<ExperimentPreset> ExperimentPresets { get; }

    public ICommand ConnectCommand { get; private set; } = null!;
    public ICommand DisconnectCommand { get; private set; } = null!;
    public ICommand StartCommand { get; private set; } = null!;
    public ICommand StopCommand { get; private set; } = null!;
    public ICommand TareAllCommand { get; private set; } = null!;
    public ICommand TareSelectedCommand { get; private set; } = null!;
    public ICommand ShuntSelectedCommand { get; private set; } = null!;
    public ICommand ApplyScaleFromShuntCommand { get; private set; } = null!;
    public ICommand RefreshPortsCommand { get; private set; } = null!;
    public ICommand StartRecordCommand { get; private set; } = null!;
    public ICommand StopRecordCommand { get; private set; } = null!;
    public ICommand SaveProjectCommand { get; private set; } = null!;
    public ICommand LoadProjectCommand { get; private set; } = null!;
    public ICommand ReplayCommand { get; private set; } = null!;
    public ICommand ExportExcelCommand { get; private set; } = null!;
    public ICommand ExportTxtCommand { get; private set; } = null!;
    public ICommand ExportMatCommand { get; private set; } = null!;
    public ICommand ExportDiademCommand { get; private set; } = null!;
    public ICommand ExportHtmlReportCommand { get; private set; } = null!;
    public ICommand DownloadCsvCommand { get; private set; } = null!;
    public ICommand OpenCsvFolderCommand { get; private set; } = null!;
    public ICommand AddMathCommand { get; private set; } = null!;
    public ICommand ApplySensorCommand { get; private set; } = null!;
    public ICommand ApplySensorAllEnabledCommand { get; private set; } = null!;
    public ICommand ReloadSensorsCommand { get; private set; } = null!;
    public ICommand SaveSensorsCommand { get; private set; } = null!;
    public ICommand ImportSensorsCommand { get; private set; } = null!;
    public ICommand ExportSensorsCommand { get; private set; } = null!;
    public ICommand AddSensorCommand { get; private set; } = null!;
    public ICommand DuplicateSensorCommand { get; private set; } = null!;
    public ICommand DeleteSensorCommand { get; private set; } = null!;
    public ICommand UpdateSensorCommand { get; private set; } = null!;
    public ICommand EditSensorNavigateCommand { get; private set; } = null!;
    public ICommand CopySensorCodeCommand { get; private set; } = null!;
    public ICommand CopySensorNameCommand { get; private set; } = null!;
    public ICommand ShowSensorDetailsCommand { get; private set; } = null!;
    public ICommand ZeroSensorOffsetCommand { get; private set; } = null!;
    public ICommand ToggleSelectedChannelAlarmCommand { get; private set; } = null!;
    public ICommand ResetSelectedChannelOffsetCommand { get; private set; } = null!;
    public ICommand ResetAllSoftZeroCommand { get; private set; } = null!;
    public ICommand InvertSelectedChannelPolarityCommand { get; private set; } = null!;
    public ICommand LoadAnalysisCommand { get; private set; } = null!;
    public ICommand SmoothAnalysisCommand { get; private set; } = null!;
    public ICommand CutAnalysisCommand { get; private set; } = null!;
    public ICommand FindPeaksCommand { get; private set; } = null!;
    public ICommand SaveAnalysisCsvCommand { get; private set; } = null!;
    public ICommand ExportAnalysisTxtCommand { get; private set; } = null!;
    public ICommand ExportAnalysisMatCommand { get; private set; } = null!;
    public ICommand ExportAnalysisExcelCommand { get; private set; } = null!;
    public ICommand ExportUpetReportCommand { get; private set; } = null!;
    public ICommand OpenUpetReportCommand { get; private set; } = null!;
    public ICommand CopyMeasurementFingerprintCommand { get; private set; } = null!;
    public ICommand ExportLabPackageCommand { get; private set; } = null!;
    public ICommand OpenLabPackageCommand { get; private set; } = null!;
    public ICommand StartExperimentCommand { get; private set; } = null!;
    public ICommand CloseExperimentCommand { get; private set; } = null!;
    public ICommand ExportRegionCommand { get; private set; } = null!;
    public ICommand RunMacroCommand { get; private set; } = null!;
    public ICommand CancelMacroCommand { get; private set; } = null!;
    public ICommand ContinueMacroCommand { get; private set; } = null!;
    public ICommand AddMacroStepCommand { get; private set; } = null!;
    public ICommand ClearMacroCommand { get; private set; } = null!;
    public ICommand LoadDefaultMacroCommand { get; private set; } = null!;
    public ICommand LoadLabMacroCommand { get; private set; } = null!;
    public ICommand SaveMacroFileCommand { get; private set; } = null!;
    public ICommand LoadMacroFileCommand { get; private set; } = null!;
    public ICommand AddDeviceCommand { get; private set; } = null!;
    public ICommand RebuildChannelsFromDevicesCommand { get; private set; } = null!;
    public ICommand TedsScanCommand { get; private set; } = null!;

    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            OnPropertyChanged();
            try { RefreshLabAdvisorFromStatus(value); } catch { /* advisor must never break Status */ }
            try { ParseDeviceEstFromStatus(value); } catch { /* ignore */ }
            try { RefreshStatusLineCompact(); } catch { /* ignore */ }
        }
    }
    public string CursorText
    {
        get => _cursorText;
        set
        {
            _cursorText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CursorStatusShort));
            try { RefreshStatusLineCompact(); } catch { /* ignore */ }
        }
    }

    /// <summary>Live PC local clock (status bar) — follows Windows Date &amp; Time.</summary>
    public string PcClockText
    {
        get => _pcClockText;
        private set { if (_pcClockText == value) return; _pcClockText = value; OnPropertyChanged(); }
    }

    public string PcClockTooltip
    {
        get => _pcClockTooltip;
        private set { if (_pcClockTooltip == value) return; _pcClockTooltip = value; OnPropertyChanged(); }
    }

    private void StartPcClockTimer()
    {
        TickPcClock();
        _pcClockTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _pcClockTimer.Tick += (_, _) => TickPcClock();
        _pcClockTimer.Start();
    }

    private void TickPcClock()
    {
        var now = AppClock.Now;
        var tz = TimeZoneInfo.Local;
        PcClockText = now.ToString("HH:mm:ss");
        PcClockTooltip =
            $"Ora PC (locală): {AppClock.FormatUi(now)}\n" +
            $"Fus orar: {tz.DisplayName}\n" +
            "CSV / MARK / jurnal folosesc automat ora Windows.";
    }

    public string SelectedBackend
    {
        get => _selectedBackend;
        set
        {
            _selectedBackend = value;
            OnPropertyChanged();
            if (!IsConnected) RefreshConnectedDeviceLabel();
        }
    }
    public string? SelectedPort
    {
        get => _selectedPort;
        set
        {
            _selectedPort = value;
            OnPropertyChanged();
            if (!IsConnected)
            {
                // USBHBM is not a COM port — prefer HBM USB / Spider32 routing (DEST), never Serial.
                if (HbmUsbDeviceScanner.IsHbmUsbTarget(value) && SelectedBackend == "Serial")
                    SelectedBackend = "HBM USB";
                RefreshConnectedDeviceLabel();
            }
        }
    }
    /// <summary>Human-readable device strip, e.g. Conectat: Spider8_1 Spider8 [USB USBHBM2186].</summary>
    public string ConnectedDeviceLabel
    {
        get => _connectedDeviceLabel;
        set { _connectedDeviceLabel = value; OnPropertyChanged(); }
    }
    /// <summary>Green when connected, red/gray when disconnected, amber while connecting.</summary>
    public System.Windows.Media.Brush ConnectionStatusBrush
    {
        get => _connectionStatusBrush;
        set { _connectionStatusBrush = value; OnPropertyChanged(); }
    }
    public string HardwareSummary
    {
        get => _hardwareSummary;
        set { _hardwareSummary = value; OnPropertyChanged(); }
    }
    public int SampleRateHz
    {
        get => _sampleRateHz;
        set
        {
            _sampleRateHz = value;
            OnPropertyChanged();
            try { RefreshStatusLineCompact(); } catch { /* ignore */ }
            try { RefreshMachineStateBand(); } catch { /* headline rate only */ }
        }
    }
    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            if (_isConnecting == value) return;
            _isConnecting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConnectProgressVisible));
            RaiseCommands();
            RefreshConnectionHealth();
        }
    }
    public double ConnectProgress
    {
        get => _connectProgress;
        set { _connectProgress = value; OnPropertyChanged(); }
    }
    public string ConnectStageText
    {
        get => _connectStageText;
        set { _connectStageText = value; OnPropertyChanged(); }
    }
    public bool IsConnectProgressVisible => IsConnecting;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            OnPropertyChanged();
            RaiseCommands();
            RefreshConnectionHealth();
            RefreshOperatorStatus();
            if (!value) ResetSessionZeroed();
            try { RefreshLabAdvisorFromState(force: true); } catch { /* ignore */ }
        }
    }
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            _isStreaming = value;
            OnPropertyChanged();
            RaiseCommands();
            RefreshConnectionHealth();
            RefreshOperatorStatus();
            try { RefreshLabAdvisorFromState(force: true); } catch { /* ignore */ }
        }
    }
    public bool IsRecording
    {
        get => _isRecording;
        set
        {
            _isRecording = value;
            OnPropertyChanged();
            RaiseCommands();
            RefreshConnectionHealth();
            RefreshOperatorStatus();
            try
            {
                RefreshLabAdvisorFromState(force: true);
                if (!value) RequestAdvisorPeerRefresh(force: true);
            }
            catch { /* ignore */ }
        }
    }
    public string ProjectName { get => _projectName; set { _projectName = value; OnPropertyChanged(); } }
    public bool TriggerEnabled { get => _triggerEnabled; set { _triggerEnabled = value; OnPropertyChanged(); } }
    public int TriggerChannel { get => _triggerChannel; set { _triggerChannel = value; OnPropertyChanged(); } }
    public double TriggerThreshold { get => _triggerThreshold; set { _triggerThreshold = value; OnPropertyChanged(); } }
    public bool TriggerRising { get => _triggerRising; set { _triggerRising = value; OnPropertyChanged(); } }
    public int? MaxSamples { get => _maxSamples; set { _maxSamples = value; OnPropertyChanged(); } }
    public int? MaxSeconds { get => _maxSeconds; set { _maxSeconds = value; OnPropertyChanged(); } }
    public string LastRecordingPath { get => _lastRecordingPath; set { _lastRecordingPath = value; OnPropertyChanged(); } }
    public string? SelectedSensorName { get => _selectedSensorName; set { _selectedSensorName = value; OnPropertyChanged(); } }
    public string SelectedSensorCategory
    {
        get => _selectedSensorCategory;
        set
        {
            _selectedSensorCategory = value;
            OnPropertyChanged();
            RefreshSensorFilter();
        }
    }
    public string SensorSearchText
    {
        get => _sensorSearchText;
        set
        {
            _sensorSearchText = value;
            OnPropertyChanged();
            RefreshSensorFilter();
        }
    }
    public string SensorLibrarySummary { get => _sensorLibrarySummary; set { _sensorLibrarySummary = value; OnPropertyChanged(); } }
    public string EditSensorName { get => _editSensorName; set { _editSensorName = value; OnPropertyChanged(); } }
    public string EditSensorUnit { get => _editSensorUnit; set { _editSensorUnit = value; OnPropertyChanged(); } }
    public double EditSensorScale { get => _editSensorScale; set { _editSensorScale = value; OnPropertyChanged(); } }
    public string EditSensorBridge { get => _editSensorBridge; set { _editSensorBridge = value; OnPropertyChanged(); } }
    public string EditSensorNotes { get => _editSensorNotes; set { _editSensorNotes = value; OnPropertyChanged(); } }
    public double EditSensorExcitationV { get => _editSensorExcitationV; set { _editSensorExcitationV = value; OnPropertyChanged(); } }
    public double EditSensorShuntKohm { get => _editSensorShuntKohm; set { _editSensorShuntKohm = value; OnPropertyChanged(); } }
    public SensorListRow? SelectedSensorRow
    {
        get => _selectedSensorRow;
        set
        {
            _selectedSensorRow = value;
            if (value is not null)
            {
                SelectedSensorName = value.Name;
                EditSensorName = value.Name;
                EditSensorUnit = value.Unit;
                EditSensorScale = value.Scale;
                EditSensorBridge = value.Bridge;
                EditSensorNotes = value.Notes;
                EditSensorExcitationV = value.ExcitationV;
                EditSensorShuntKohm = value.ShuntKohm;
            }
            OnPropertyChanged();
        }
    }
    /// <summary>TreeView selected item — category group or sensor row.</summary>
    public object? SelectedSensorTreeItem
    {
        get => _selectedSensorRow;
        set
        {
            if (value is SensorListRow row)
            {
                SelectedSensorRow = row;
                SelectedSensorName = row.Name;
            }
            OnPropertyChanged();
        }
    }
    public int SelectedChannelForSensor { get => _selectedChannelForSensor; set { _selectedChannelForSensor = value; OnPropertyChanged(); } }
    public ChannelRow? SelectedChannelRow
    {
        get => _selectedChannelRow;
        set
        {
            _selectedChannelRow = value;
            // Apply CH# box is 1-based (CH1=HW0); grid row Name is 0-based (CH2=HW2).
            if (value is not null)
                SelectedChannelForSensor = value.Index + 1;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedChannel));
            OnPropertyChanged(nameof(DominantLiveChannel));
            OnPropertyChanged(nameof(HasDominantLiveChannel));
        }
    }
    public int SmoothWindow { get => _smoothWindow; set { _smoothWindow = value; OnPropertyChanged(); } }
    public int CutStart { get => _cutStart; set { _cutStart = value; OnPropertyChanged(); } }
    public int CutEnd { get => _cutEnd; set { _cutEnd = value; OnPropertyChanged(); } }
    public int AnalysisChannel { get => _analysisChannel; set { _analysisChannel = value; OnPropertyChanged(); UpdateDeltaText(); } }
    public int MacroRecordMs { get => _macroRecordMs; set { _macroRecordMs = value; OnPropertyChanged(); } }
    public int MacroSettleMs { get => _macroSettleMs; set { _macroSettleMs = value; OnPropertyChanged(); } }
    public string AnalysisSummary { get => _analysisSummary; set { _analysisSummary = value; OnPropertyChanged(); } }
    public string MeasurementFingerprint
    {
        get => _measurementFingerprint;
        set
        {
            _measurementFingerprint = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMeasurementFingerprint));
        }
    }
    public bool HasMeasurementFingerprint => !string.IsNullOrWhiteSpace(_measurementFingerprint);
    public string FingerprintStatusText
    {
        get => _fingerprintStatusText;
        set { _fingerprintStatusText = value ?? ""; OnPropertyChanged(); }
    }
    public string FingerprintStatusBrushHex { get; private set; } = "#607D8B";
    public Brush FingerprintStatusBrush
    {
        get => _fingerprintStatusBrush;
        private set { _fingerprintStatusBrush = value; OnPropertyChanged(); }
    }
    public string DeltaText { get => _deltaText; set { _deltaText = value; OnPropertyChanged(); } }
    public bool PanelMode
    {
        get => _panelMode;
        set
        {
            _panelMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LeftPanelVisible));
            UiLayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public bool InfoMode
    {
        get => _infoMode;
        set
        {
            _infoMode = value;
            OnPropertyChanged();
            HelpPanelText = value
                ? "Info ON — hover pe controale."
                : "Info OFF.";
        }
    }
    public string HelpPanelText
    {
        get => _helpPanelText;
        set
        {
            _helpPanelText = value;
            OnPropertyChanged();
            try { RefreshStatusLineCompact(); } catch { /* ignore */ }
        }
    }
    public string PlotModeDescription { get => _plotModeDescription; set { _plotModeDescription = value; OnPropertyChanged(); } }
    public string PlotMode
    {
        get => _plotMode;
        set
        {
            _plotMode = value;
            PlotModeDescription = DisplayLayouts.Describe(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowPlots));
            OnPropertyChanged(nameof(ShowPlotB));
            OnPropertyChanged(nameof(ShowNumericPanel));
            OnPropertyChanged(nameof(ShowLiveCwt));
            if (value == DisplayLayouts.Cwt)
            {
                ShowLivePlotCard = true;
                SyncLiveCwtPanels();
            }
            RebuildPlotSeries();
            UiLayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public ExperimentPreset? SelectedExperiment
    {
        get => _selectedExperiment;
        set
        {
            _selectedExperiment = value;
            if (value is not null)
            {
                PlotMode = value.PlotMode;
                Status = $"Experiment: {value.Name} — {value.Description}";
                HelpPanelText = value.Description;
                if (string.Equals(value.Id, "sensor-verify", StringComparison.Ordinal))
                    OnSensorVerifyPresetSelected();
                else if (string.Equals(value.Id, "hydraulic-press", StringComparison.Ordinal))
                    OnHydraulicPressPresetSelected();
            }
            OnPropertyChanged();
        }
    }
    public bool FullscreenPanel
    {
        get => _fullscreenPanel;
        set
        {
            _fullscreenPanel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LeftPanelVisible));
            UiLayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? UiLayoutChanged;
    public event EventHandler? RequestAnalysisTab;
    public event EventHandler? RequestSensorLibraryTab;

    public bool LeftPanelVisible => !PanelMode && !FullscreenPanel;
    public bool ShowPlots => (PlotMode is not DisplayLayouts.Numeric || MultiPanelMode) && PlotMode is not DisplayLayouts.Cwt;
    public bool ShowNumericPanel => PlotMode is DisplayLayouts.Numeric || MultiPanelMode;
    public bool ShowPlotB => PlotMode is DisplayLayouts.DualYt or DisplayLayouts.Fft or DisplayLayouts.Poisson
        || (MultiPanelMode && PlotMode is DisplayLayouts.Yt);
    /// <summary>When true, live plots auto-scale; when false, mouse zoom/pan is preserved.</summary>
    public bool FollowLiveZoom
    {
        get => _followLiveZoom;
        set
        {
            _followLiveZoom = value;
            OnPropertyChanged();
            foreach (var panel in ChannelPlots)
                panel.FollowLive = value;
            Status = value
                ? "Urmărire live ON — panouri grafic canal (expand rapid / shrink lent)."
                : "Urmărire live OFF — zoom/pan liber pe panouri grafic (roată / drag).";
        }
    }
    public ICommand AutoscalePlotsCommand { get; private set; } = null!;
    public ICommand ResetPlotZoomCommand { get; private set; } = null!;
    public ICommand ShowAllChannelsOnPlotCommand { get; private set; } = null!;
    public ICommand HideAllChannelsOnPlotCommand { get; private set; } = null!;
    public int XyXChannel { get => _xyXChannel; set { _xyXChannel = value; OnPropertyChanged(); RebuildPlotSeries(); } }
    public int XyYChannel { get => _xyYChannel; set { _xyYChannel = value; OnPropertyChanged(); RebuildPlotSeries(); } }
    public int FftChannel { get => _fftChannel; set { _fftChannel = value; OnPropertyChanged(); RebuildPlotSeries(); } }
    public ICommand ToggleInfoModeCommand { get; private set; } = null!;
    public int PreTriggerSamples { get => _preTriggerSamples; set { _preTriggerSamples = value; OnPropertyChanged(); } }
    public bool AppendMode { get => _appendMode; set { _appendMode = value; OnPropertyChanged(); } }
    public bool AutoFileName { get => _autoFileName; set { _autoFileName = value; OnPropertyChanged(); } }
    public string OperatorName { get => _operatorName; set { _operatorName = value; OnPropertyChanged(); RefreshWorkflowStepsOnly(); } }
    public string SampleId { get => _sampleId; set { _sampleId = value; OnPropertyChanged(); RefreshWorkflowStepsOnly(); } }
    public string Comment { get => _comment; set { _comment = value; OnPropertyChanged(); } }
    public string LabLocation { get => _labLocation; set { _labLocation = value; OnPropertyChanged(); } }
    public string ExperimentType { get => _experimentType; set { _experimentType = value; OnPropertyChanged(); } }
    /// <summary>Contur cilindru mapping (when experiment type is Compresiune cilindru – contur).</summary>
    public Spider8DAQ.Core.Projects.CylinderContourConfig? CylinderContour
    {
        get => _cylinderContour;
        set { _cylinderContour = value; OnPropertyChanged(); }
    }
    public string PlannedSensors { get => _plannedSensors; set { _plannedSensors = value; OnPropertyChanged(); } }
    public double SampleLengthMm { get => _sampleLengthMm; set { _sampleLengthMm = value; OnPropertyChanged(); } }
    public double SampleWidthMm { get => _sampleWidthMm; set { _sampleWidthMm = value; OnPropertyChanged(); } }
    public double SampleThicknessMm { get => _sampleThicknessMm; set { _sampleThicknessMm = value; OnPropertyChanged(); } }
    public double SampleDiameterMm { get => _sampleDiameterMm; set { _sampleDiameterMm = value; OnPropertyChanged(); } }
    public double SampleAreaMm2 { get => _sampleAreaMm2; set { _sampleAreaMm2 = value; OnPropertyChanged(); } }
    public double SampleMassG { get => _sampleMassG; set { _sampleMassG = value; OnPropertyChanged(); } }
    public string SampleDimensionsSummary
    {
        get => _sampleDimensionsSummary;
        set { _sampleDimensionsSummary = value ?? ""; OnPropertyChanged(); }
    }
    public string SpecimenId { get => _specimenId; set { _specimenId = value ?? ""; OnPropertyChanged(); } }
    public string SpecimenNameRo { get => _specimenNameRo; set { _specimenNameRo = value ?? ""; OnPropertyChanged(); } }
    public string SpecimenClass { get => _specimenClass; set { _specimenClass = value ?? ""; OnPropertyChanged(); } }
    public string SpecimenFormulaPack { get => _specimenFormulaPack; set { _specimenFormulaPack = value ?? ""; OnPropertyChanged(); } }
    public string SpecimenFormulaPackLabel { get => _specimenFormulaPackLabel; set { _specimenFormulaPackLabel = value ?? ""; OnPropertyChanged(); } }
    public string SpecimenSummary { get => _specimenSummary; set { _specimenSummary = value ?? ""; OnPropertyChanged(); } }
    public string SpecimenStandardNote { get => _specimenStandardNote; set { _specimenStandardNote = value ?? ""; OnPropertyChanged(); } }
    public string SpecimenStrengthNotes { get => _specimenStrengthNotes; set { _specimenStrengthNotes = value ?? ""; OnPropertyChanged(); } }
    public string SpecimenShape { get => _specimenShape; set { _specimenShape = value ?? ""; OnPropertyChanged(); } }
    public double SpecimenYoungGPa { get => _specimenYoungGPa; set { _specimenYoungGPa = value; OnPropertyChanged(); } }
    public double SpecimenPoissonNu { get => _specimenPoissonNu; set { _specimenPoissonNu = value; OnPropertyChanged(); } }
    public double SpecimenDensityKgM3 { get => _specimenDensityKgM3; set { _specimenDensityKgM3 = value; OnPropertyChanged(); } }
    public string SpecimenNotes { get => _specimenNotes; set { _specimenNotes = value ?? ""; OnPropertyChanged(); } }
    public DateTime? ExperimentStartedAt { get => _experimentStartedAt; set { _experimentStartedAt = value; OnPropertyChanged(); } }
    public DateTime? ExperimentEndedAt { get => _experimentEndedAt; set { _experimentEndedAt = value; OnPropertyChanged(); } }
    public int EstimatedDurationMinutes { get => _estimatedDurationMinutes; set { _estimatedDurationMinutes = value; OnPropertyChanged(); } }
    private ImageSource? _montageBeforeImage;
    private ImageSource? _montageAfterImage;

    public string MontagePhotoPath
    {
        get => _montagePhotoPath;
        set
        {
            _montagePhotoPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMontagePhoto));
            OnPropertyChanged(nameof(HasAnyMontagePhoto));
            OnPropertyChanged(nameof(ExperimentBannerText));
            RefreshMontagePreviewImages();
        }
    }
    public bool HasMontagePhoto => !string.IsNullOrWhiteSpace(MontagePhotoPath) && File.Exists(MontagePhotoPath);
    public string MontagePhotoAfterPath
    {
        get => _montagePhotoAfterPath;
        set
        {
            _montagePhotoAfterPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMontagePhotoAfter));
            OnPropertyChanged(nameof(HasAnyMontagePhoto));
            OnPropertyChanged(nameof(ExperimentBannerText));
            RefreshMontagePreviewImages();
        }
    }
    public bool HasMontagePhotoAfter =>
        !string.IsNullOrWhiteSpace(MontagePhotoAfterPath) && File.Exists(MontagePhotoAfterPath);
    public bool HasAnyMontagePhoto => HasMontagePhoto || HasMontagePhotoAfter;
    public ImageSource? MontageBeforeImage
    {
        get => _montageBeforeImage;
        private set { _montageBeforeImage = value; OnPropertyChanged(); }
    }
    public ImageSource? MontageAfterImage
    {
        get => _montageAfterImage;
        private set { _montageAfterImage = value; OnPropertyChanged(); }
    }
    public string MontageBeforeNotes { get => _montageBeforeNotes; set { _montageBeforeNotes = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMontageBeforeNotes)); } }
    public bool HasMontageBeforeNotes => !string.IsNullOrWhiteSpace(MontageBeforeNotes);
    public string MontageAfterNotes { get => _montageAfterNotes; set { _montageAfterNotes = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasMontageAfterNotes)); } }
    public bool HasMontageAfterNotes => !string.IsNullOrWhiteSpace(MontageAfterNotes);
    public DateTime? MontageBeforeCapturedAt
    {
        get => _montageBeforeCapturedAt;
        set { _montageBeforeCapturedAt = value; OnPropertyChanged(); }
    }
    public DateTime? MontageAfterCapturedAt
    {
        get => _montageAfterCapturedAt;
        set { _montageAfterCapturedAt = value; OnPropertyChanged(); }
    }
    public bool IsExperimentActive
    {
        get => _isExperimentActive;
        set
        {
            _isExperimentActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExperimentBannerText));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public string ExperimentBannerText =>
        !IsExperimentActive
            ? "Niciun experiment activ"
            : $"Experiment activ: {ProjectName} · {OperatorName} · {SampleId} · {ExperimentStartedAt:HH:mm:ss}" +
              (HasMontagePhoto ? " · montaj înainte" : "") +
              (HasMontagePhotoAfter ? " · probă după" : "");
    public int CursorA { get => _cursorA; set { _cursorA = value; OnPropertyChanged(); SyncCursorsToOffline(); } }
    public int CursorB { get => _cursorB; set { _cursorB = value; OnPropertyChanged(); SyncCursorsToOffline(); } }

    private bool _macroWaitingOperator;

    public bool MacroWaitingOperator
    {
        get => _macroWaitingOperator;
        set { _macroWaitingOperator = value; OnPropertyChanged(); }
    }

    public ObservableCollection<string> MacroStepTypes { get; } = new(MacroRunner.StepTypeNames);

    bool MacroRunner.IMacroHost.IsConnected => IsConnected;
    bool MacroRunner.IMacroHost.IsTriggerSatisfied => _engine.IsTriggerSatisfied;
    void MacroRunner.IMacroHost.SetStatus(string message) => _dispatcher.Invoke(() => Status = message);
    Task MacroRunner.IMacroHost.TareAllAsync() => TareAsync(null);
    Task MacroRunner.IMacroHost.StartStreamingAsync() => StartAsync();
    Task MacroRunner.IMacroHost.StopStreamingAsync() => StopAsync();
    Task MacroRunner.IMacroHost.StartRecordingAsync() => StartRecordingAsync(skipExperimentWarning: true);
    Task MacroRunner.IMacroHost.StopRecordingAsync() => StopRecordingAsync(skipConfirm: true);

    Task MacroRunner.IMacroHost.SetSampleRateAsync(int hz)
    {
        _dispatcher.Invoke(() =>
        {
            SampleRateHz = Math.Max(1, hz);
            if (_device is not null) _device.SampleRateHz = SampleRateHz;
            foreach (var ch in Channels)
                ch.ChannelSampleRateHz = SampleRateHz;
            // TimeFromSample = Sequence / Hz — rate change can reverse X on live DataLoggers.
            if (IsStreaming)
            {
                RebuildPlotSeries();
                RebuildAllChannelPlotSeries();
                ResetCatmanLiveAxis();
            }
        });
        return Task.CompletedTask;
    }

    Task MacroRunner.IMacroHost.ApplyFiltersAsync(double filterHz)
    {
        _dispatcher.Invoke(() =>
        {
            foreach (var ch in Channels.Where(c => c.Enabled))
                ch.FilterHz = filterHz;
        });
        return _device is null
            ? Task.CompletedTask
            : _device.ApplyChannelConfigAsync(Channels.Select(ToConfig));
    }

    Task MacroRunner.IMacroHost.WaitOperatorAsync(string message, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _macroOperatorWait = tcs;
        _dispatcher.Invoke(() =>
        {
            Status = message;
            MacroWaitingOperator = true;
            HelpPanelText = "Macro în pauză — apasă «Continuă macro» după verificare.";
        });
        ct.Register(() =>
        {
            tcs.TrySetCanceled(ct);
            _dispatcher.Invoke(() => MacroWaitingOperator = false);
        });
        return tcs.Task;
    }

    void MacroRunner.IMacroHost.Beep()
    {
        try { SystemSounds.Beep.Play(); } catch { /* ignore */ }
    }

    private void WireCommands()
    {
        ConnectCommand = new RelayCommand(async () => await ConnectAsync(), () => !IsConnected && !IsConnecting);
        DisconnectCommand = new RelayCommand(async () => await DisconnectAsync(), () => IsConnected && !IsConnecting);
        StartCommand = new RelayCommand(async () => await StartAsync(), () => IsConnected && !IsStreaming);
        StopCommand = new RelayCommand(async () => await StopAsync(), () => IsStreaming);
        TareAllCommand = new RelayCommand(async () => await TareAsync(null), () => IsConnected);
        TareSelectedCommand = new RelayCommand(async () =>
        {
            var ch = ResolveSelectedHwChannel();
            if (ch is null)
            {
                Status = "Selectați un canal pentru Zero CH.";
                return;
            }
            await TareAsync(ch.Index);
        }, () => IsConnected);
        ShuntSelectedCommand = new RelayCommand(async () => await RunShuntCheckAsync(), () => IsConnected);
        ApplyScaleFromShuntCommand = new RelayCommand(
            ApplyScaleFromShunt,
            () => IsConnected && ResolveSelectedHwChannel()?.LastShuntCanApplyScale == true);
        RefreshPortsCommand = new RelayCommand(RefreshPorts);
        StartRecordCommand = new RelayCommand(async () => await StartRecordingAsync(), () => IsStreaming && !IsRecording);
        StopRecordCommand = new RelayCommand(async () => await StopRecordingAsync(), () => IsRecording);
        SaveProjectCommand = new RelayCommand(async () => await SaveProjectAsync());
        LoadProjectCommand = new RelayCommand(async () => await LoadProjectAsync());
        ReplayCommand = new RelayCommand(ReplayCsv);
        ExportExcelCommand = new RelayCommand(async () => await ExportLastOrPickAsync("xlsx"));
        ExportTxtCommand = new RelayCommand(async () => await ExportLastOrPickAsync("txt"));
        ExportMatCommand = new RelayCommand(async () => await ExportLastOrPickAsync("mat"));
        ExportDiademCommand = new RelayCommand(async () => await ExportLastOrPickAsync("diadem"));
        ExportHtmlReportCommand = new RelayCommand(async () => await ExportHtmlReportAsync());
        DownloadCsvCommand = new RelayCommand(DownloadLastCsv);
        OpenCsvFolderCommand = new RelayCommand(OpenRecordingsFolder);
        AddMathCommand = new RelayCommand(AddMathChannel);
        ApplySensorCommand = new RelayCommand(() => ApplySensor(false));
        ApplySensorAllEnabledCommand = new RelayCommand(() => ApplySensor(true));
        ReloadSensorsCommand = new RelayCommand(async () => await LoadSensorsAsync(forceReload: true));
        SaveSensorsCommand = new RelayCommand(async () => await SaveSensorsAsync());
        ImportSensorsCommand = new RelayCommand(async () => await ImportSensorsAsync());
        ExportSensorsCommand = new RelayCommand(async () => await ExportSensorsAsync());
        AddSensorCommand = new RelayCommand(AddSensor);
        DuplicateSensorCommand = new RelayCommand(DuplicateSensor);
        DeleteSensorCommand = new RelayCommand(DeleteSensor);
        UpdateSensorCommand = new RelayCommand(UpdateSelectedSensor);
        EditSensorNavigateCommand = new RelayCommand(EditSensorNavigate);
        CopySensorCodeCommand = new RelayCommand(CopySensorCode);
        CopySensorNameCommand = new RelayCommand(CopySensorName);
        ShowSensorDetailsCommand = new RelayCommand(ShowSensorDetails);
        ZeroSensorOffsetCommand = new RelayCommand(async () => await ZeroSensorOffsetAsync(), () => SelectedSensorRow is not null);
        ToggleSelectedChannelAlarmCommand = new RelayCommand(() =>
        {
            var ch = ResolveSelectedHwChannel();
            if (ch is null) { Status = "Selectați un canal pentru alarmă."; return; }
            ch.AlarmEnabled = !ch.AlarmEnabled;
            if (ch.AlarmEnabled && ch.AlarmHigh >= 1e8 && ch.AlarmLow <= -1e8)
            {
                ch.AlarmLow = -10;
                ch.AlarmHigh = 10;
            }
            Status = ch.AlarmEnabled
                ? $"Alarmă ON {ch.Name}: [{ch.AlarmLow:0.####} … {ch.AlarmHigh:0.####}] {ch.Unit}"
                : $"Alarmă OFF {ch.Name}";
        });
        ResetSelectedChannelOffsetCommand = new RelayCommand(() =>
        {
            var ch = ResolveSelectedHwChannel();
            if (ch is null) { Status = "Selectați un canal."; return; }
            ch.Offset = 0;
            ch.TareValue = 0;
            Status = $"Offset/Zero resetat pe {ch.Name}.";
        });
        ResetAllSoftZeroCommand = new RelayCommand(() =>
        {
            var n = 0;
            foreach (var ch in Channels)
            {
                if (Math.Abs(ch.TareValue) < 1e-15) continue;
                ch.TareValue = 0;
                n++;
            }
            _sessionZeroed = false;
            RefreshOperatorStatus();
            Status = n == 0 ? "Niciun Soft Zero de resetat." : $"Soft Zero șters pe {n} canale (Offset/Scale păstrate).";
        });
        InvertSelectedChannelPolarityCommand = new RelayCommand(() =>
        {
            var ch = ResolveSelectedHwChannel();
            if (ch is null) { Status = "Selectați un canal pentru inversare polaritate."; return; }
            if (!TryInvertChannelPolarity(ch, out var detail))
            {
                Status = detail;
                return;
            }
            // Brief status toast — user must Zero with no load after invert.
            Status = $"Polaritate inversată pe {ch.Name} (Scale={ch.Scale:G6}). Faceți Zero CH fără sarcină.";
            _journal.Setup(Status);
            HelpPanelText =
                "Polaritate: dacă la compresie forța arată −N, click dreapta pe canal → " +
                "Inversare polaritate (Scale × −1), apoi Zero CH fără sarcină. Comanda este toggle.";
        });
        LoadAnalysisCommand = new RelayCommand(LoadAnalysisCsv);
        SmoothAnalysisCommand = new RelayCommand(SmoothAnalysis);
        CutAnalysisCommand = new RelayCommand(CutAnalysis);
        FindPeaksCommand = new RelayCommand(FindPeaks);
        SaveAnalysisCsvCommand = new RelayCommand(SaveAnalysisCsv);
        ExportAnalysisTxtCommand = new RelayCommand(async () => await ExportAnalysisAsync("txt"));
        ExportAnalysisMatCommand = new RelayCommand(async () => await ExportAnalysisAsync("mat"));
        ExportAnalysisExcelCommand = new RelayCommand(async () => await ExportAnalysisAsync("xlsx"));
        ExportUpetReportCommand = new RelayCommand(ExportUpetReport);
        OpenUpetReportCommand = new RelayCommand(OpenUpetReport);
        CopyMeasurementFingerprintCommand = new RelayCommand(() =>
        {
            if (string.IsNullOrWhiteSpace(MeasurementFingerprint))
            {
                Status = "Nu există amprentă de copiat.";
                return;
            }
            try
            {
                System.Windows.Clipboard.SetText(MeasurementFingerprint);
                Status = "Amprentă copiată: " + MeasurementFingerprint;
            }
            catch (Exception ex)
            {
                Status = "Clipboard: " + ex.Message;
            }
        });
        ExportLabPackageCommand = new RelayCommand(async () => await ExportLabPackageAsync());
        OpenLabPackageCommand = new RelayCommand(OpenLabPackage);
        StartExperimentCommand = new RelayCommand(StartExperimentDialog);
        CloseExperimentCommand = new RelayCommand(CloseExperiment, () => IsExperimentActive);
        ExportRegionCommand = new RelayCommand(ExportCursorRegion);
        RunMacroCommand = new RelayCommand(async () => await RunMacroAsync(), () => IsConnected);
        CancelMacroCommand = new RelayCommand(CancelMacro);
        ContinueMacroCommand = new RelayCommand(() =>
        {
            MacroWaitingOperator = false;
            _macroOperatorWait?.TrySetResult();
            _macroOperatorWait = null;
            Status = "Macro: operator a confirmat — continuare.";
        }, () => MacroWaitingOperator);
        AddMacroStepCommand = new RelayCommand(() =>
        {
            MacroSteps.Add(new MacroStepRow { Type = nameof(MacroStepType.WaitMs), IntParam = 500 });
            RefreshMacroFlow();
        });
        ClearMacroCommand = new RelayCommand(() => { MacroSteps.Clear(); RefreshMacroFlow(); });
        LoadDefaultMacroCommand = new RelayCommand(() => { LoadDefaultMacroSteps(); RefreshMacroFlow(); });
        LoadLabMacroCommand = new RelayCommand(() => { LoadLabMacroSteps(); RefreshMacroFlow(); });
        SaveMacroFileCommand = new RelayCommand(async () => await SaveMacroFileAsync());
        LoadMacroFileCommand = new RelayCommand(async () => await LoadMacroFileAsync());
        AddDeviceCommand = new RelayCommand(AddDevice);
        RebuildChannelsFromDevicesCommand = new RelayCommand(RebuildChannelsFromDevices);
        TedsScanCommand = new RelayCommand(TedsAutoMapSensors);
        ToggleInfoModeCommand = new RelayCommand(() => InfoMode = !InfoMode);
        AutoscalePlotsCommand = new RelayCommand(AutoscaleLivePlots);
        ResetPlotZoomCommand = new RelayCommand(ResetLivePlotZoom);
        ShowAllChannelsOnPlotCommand = new RelayCommand(() =>
        {
            _suppressPlotRebuild = true;
            try
            {
                foreach (var ch in Channels.Where(c => c.Enabled))
                    ch.ShowOnPlot = true;
            }
            finally
            {
                _suppressPlotRebuild = false;
            }
            RebuildPlotSeries();
            Status = "Toate canalele On sunt pe grafic — verificați unitățile (Dual Y(t) dacă amestecați µm/m + bar).";
        });
        HideAllChannelsOnPlotCommand = new RelayCommand(() =>
        {
            _suppressPlotRebuild = true;
            try
            {
                foreach (var ch in Channels)
                    ch.ShowOnPlot = false;
            }
            finally
            {
                _suppressPlotRebuild = false;
            }
            RebuildPlotSeries();
            Status = "Canale ascunse pe grafic (DAQ On rămâne neschimbat).";
        });
        WireLiveGaugeCommands();
        WireChannelPlotCommands();
        WireLivePlotCardCommands();
        WireLiveCwt();
        WireDefectLibrary();
    }

    private void RaiseCommands() => CommandManager.InvalidateRequerySuggested();

    private void SetFingerprintStatusBrush(string hex)
    {
        FingerprintStatusBrushHex = hex;
        try
        {
            FingerprintStatusBrush = (Brush)new BrushConverter().ConvertFromString(hex)!;
        }
        catch
        {
            FingerprintStatusBrush = new SolidColorBrush(Color.FromRgb(0x60, 0x7D, 0x8B));
        }
        OnPropertyChanged(nameof(FingerprintStatusBrushHex));
    }

    private void RefreshMontagePreviewImages()
    {
        MontageBeforeImage = TryLoadMontagePreview(MontagePhotoPath);
        MontageAfterImage = TryLoadMontagePreview(MontagePhotoAfterPath);
    }

    private static ImageSource? TryLoadMontagePreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.UriSource = new Uri(Path.GetFullPath(path));
            bmp.DecodePixelWidth = 420;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Run UI-bound mutations on the WPF dispatcher (safe after await / background work).</summary>
    private Task UiAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        return _dispatcher.InvokeAsync(action).Task;
    }

    public async ValueTask DisposeAsync()
    {
        CancelMacro();
        StopWatchdog();
        _playbackTimer?.Stop();
        if (_defectScanTimer is not null)
        {
            _defectScanTimer.Stop();
            _defectScanTimer = null;
        }
        if (_pcClockTimer is not null)
        {
            _pcClockTimer.Stop();
            _pcClockTimer = null;
        }
        _camera?.Dispose();
        await DisconnectAsync();
        await _engine.DisposeAsync();
        if (_db is not null) await _db.DisposeAsync();
    }
}
