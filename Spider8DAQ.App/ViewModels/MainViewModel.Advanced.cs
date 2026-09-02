using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Acquisition;
using Spider8DAQ.Core.Alarms;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Calibration;
using Spider8DAQ.Core.Compute;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Display;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Import;
using Spider8DAQ.Core.Time;
using Spider8DAQ.Core.Versioning;
using Spider8DAQ.Hardware;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<CalibrationPointRow> CalibrationPoints { get; } = new();
    public ObservableCollection<ComputeRow> ComputeRows { get; } = new();
    public ObservableCollection<string> RecordingFiles { get; } = new();
    public ObservableCollection<string> SnapshotFiles { get; } = new();
    public ObservableCollection<string> AlarmEventLines { get; } = new();
    public ObservableCollection<string> SyncLines { get; } = new();
    public ObservableCollection<string> TemplateNames { get; } = new(
        DisplayTemplate.BuiltIns.Select(t => t.Name));
    public ObservableCollection<string> BinPreviewLines { get; } = new();
    public ObservableCollection<string> MacroFlowLines { get; } = new();

    public ICommand AddCalibrationPointCommand { get; private set; } = null!;
    public ICommand FitCalibrationCommand { get; private set; } = null!;
    public ICommand ApplyCalibrationCommand { get; private set; } = null!;
    public ICommand ShowWiringCommand { get; private set; } = null!;
    public ICommand AckAlarmsCommand { get; private set; } = null!;
    public ICommand ExportAlarmLogCommand { get; private set; } = null!;
    public ICommand RefreshRecordingsCommand { get; private set; } = null!;
    public ICommand CompareRecordingsCommand { get; private set; } = null!;
    public ICommand SaveSnapshotCommand { get; private set; } = null!;
    public ICommand DiffSnapshotsCommand { get; private set; } = null!;
    public ICommand ExportPdfCommand { get; private set; } = null!;
    public ICommand ExportIndustrialReportCommand { get; private set; } = null!;
    public ICommand InspectBinCommand { get; private set; } = null!;
    public ICommand ApplyTemplateCommand { get; private set; } = null!;
    public ICommand SaveTemplateCommand { get; private set; } = null!;
    public ICommand LoadTemplateCommand { get; private set; } = null!;
    public ICommand AddComputeCommand { get; private set; } = null!;
    public ICommand RefreshMacroFlowCommand { get; private set; } = null!;
    public ICommand DeviceInfoCommand { get; private set; } = null!;
    public ICommand ToggleFullscreenCommand { get; private set; } = null!;
    public ICommand SyncComputeCommand { get; private set; } = null!;
    public ICommand OpenRecordingCommand { get; private set; } = null!;

    private CalibrationResult? _lastCalibration;
    private string _wiringText = WiringDiagrams.Describe(BridgeType.Full);
    private string _selectedTemplate = DisplayTemplate.CreateDefault().Name;
    private string? _selectedRecordingA;
    private string? _selectedRecordingB;
    private string? _selectedSnapshotA;
    private string? _selectedSnapshotB;
    private string _diffText = "";
    private string _deviceInfoText = "";
    private string _compareSummary = "Compare: —";
    private int _compareChannelA = 1;
    private int _compareChannelB = 1;
    private int _calChannel = 2;
    private double _calRaw;
    private double _calRef;
    private double _alarmHysteresis = 0.05;
    private bool _alarmLatch = true;
    private string _triggerLogic = "Single";
    private bool _windowTrigger;
    private int _preTriggerMs = 500;
    private int _postTriggerMs;

    public string WiringText { get => _wiringText; set { _wiringText = value; OnPropertyChanged(); } }
    public string SelectedTemplate { get => _selectedTemplate; set { _selectedTemplate = value; OnPropertyChanged(); } }
    public string? SelectedRecordingA { get => _selectedRecordingA; set { _selectedRecordingA = value; OnPropertyChanged(); } }
    public string? SelectedRecordingB { get => _selectedRecordingB; set { _selectedRecordingB = value; OnPropertyChanged(); } }
    public string? SelectedSnapshotA { get => _selectedSnapshotA; set { _selectedSnapshotA = value; OnPropertyChanged(); } }
    public string? SelectedSnapshotB { get => _selectedSnapshotB; set { _selectedSnapshotB = value; OnPropertyChanged(); } }
    public string DiffText { get => _diffText; set { _diffText = value; OnPropertyChanged(); } }
    public string DeviceInfoText { get => _deviceInfoText; set { _deviceInfoText = value; OnPropertyChanged(); } }
    public string CompareSummary { get => _compareSummary; set { _compareSummary = value; OnPropertyChanged(); } }
    public int CompareChannelA { get => _compareChannelA; set { _compareChannelA = Math.Max(1, value); OnPropertyChanged(); } }
    public int CompareChannelB { get => _compareChannelB; set { _compareChannelB = Math.Max(1, value); OnPropertyChanged(); } }
    public int CalChannel { get => _calChannel; set { _calChannel = value; OnPropertyChanged(); } }
    public double CalRaw { get => _calRaw; set { _calRaw = value; OnPropertyChanged(); } }
    public double CalRef { get => _calRef; set { _calRef = value; OnPropertyChanged(); } }
    public double AlarmHysteresis { get => _alarmHysteresis; set { _alarmHysteresis = value; _engine.Alarms.Hysteresis = value; OnPropertyChanged(); } }
    public bool AlarmLatch { get => _alarmLatch; set { _alarmLatch = value; _engine.Alarms.LatchEnabled = value; OnPropertyChanged(); } }
    public string TriggerLogic { get => _triggerLogic; set { _triggerLogic = value; OnPropertyChanged(); SyncAdvancedTrigger(); } }
    public bool WindowTrigger { get => _windowTrigger; set { _windowTrigger = value; OnPropertyChanged(); SyncAdvancedTrigger(); } }
    public int PreTriggerMs { get => _preTriggerMs; set { _preTriggerMs = value; OnPropertyChanged(); SyncAdvancedTrigger(); } }
    public int PostTriggerMs { get => _postTriggerMs; set { _postTriggerMs = value; OnPropertyChanged(); SyncAdvancedTrigger(); } }

    private void WireAdvancedCommands()
    {
        AddCalibrationPointCommand = new RelayCommand(() =>
        {
            CalibrationPoints.Add(new CalibrationPointRow { Raw = CalRaw, Reference = CalRef });
            Status = $"Calibration point added ({CalibrationPoints.Count}).";
        });
        FitCalibrationCommand = new RelayCommand(FitCalibration);
        ApplyCalibrationCommand = new RelayCommand(ApplyCalibration);
        ShowWiringCommand = new RelayCommand(ShowWiring);
        AckAlarmsCommand = new RelayCommand(() => { _engine.Alarms.AcknowledgeAll(); Status = "Alarme ACK (F12)."; });
        ExportAlarmLogCommand = new RelayCommand(() =>
        {
            if (AlarmEventLines.Count == 0)
            {
                Status = "Jurnal alarme gol.";
                return;
            }
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export jurnal alarme",
                Filter = "CSV|*.csv|Text|*.txt",
                FileName = $"alarms_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("# UPET AcqLab alarm log");
                sb.AppendLine($"# PC={AppClock.FormatIso(AppClock.Now)}");
                sb.AppendLine("Time\tEvent");
                foreach (var line in AlarmEventLines.Reverse())
                {
                    var tab = line.IndexOf(' ');
                    if (tab > 0 && tab < 12)
                        sb.AppendLine(line[..tab] + "\t" + line[(tab + 1)..]);
                    else
                        sb.AppendLine("\t" + line);
                }
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                Status = "Jurnal alarme: " + dlg.FileName;
            }
            catch (Exception ex)
            {
                Status = "Export alarme: " + ex.Message;
            }
        });
        RefreshRecordingsCommand = new RelayCommand(RefreshRecordings);
        CompareRecordingsCommand = new RelayCommand(CompareRecordingsMulti);
        WireDataViewerCommands();
        WireIntegrationsCommands();
        SaveSnapshotCommand = new RelayCommand(async () => await SaveSnapshotAsync());
        DiffSnapshotsCommand = new RelayCommand(async () => await DiffSnapshotsAsync());
        ExportPdfCommand = new RelayCommand(ExportPdfWithPlot);
        ExportIndustrialReportCommand = new RelayCommand(ExportIndustrialReportPdf);
        InspectBinCommand = new RelayCommand(InspectBin);
        ApplyTemplateCommand = new RelayCommand(ApplyTemplate);
        SaveTemplateCommand = new RelayCommand(async () => await SaveTemplateAsync());
        LoadTemplateCommand = new RelayCommand(async () => await LoadTemplateAsync());
        AddComputeCommand = new RelayCommand(() =>
        {
            ComputeRows.Add(new ComputeRow { Name = $"Cmp{ComputeRows.Count + 1}", Operation = nameof(ComputeOp.MovingAverage), Window = 25, Enabled = true });
            SyncComputeToEngine();
        });
        RefreshMacroFlowCommand = new RelayCommand(RefreshMacroFlow);
        DeviceInfoCommand = new RelayCommand(ShowDeviceInfo);
        ToggleFullscreenCommand = new RelayCommand(() =>
        {
            FullscreenPanel = !FullscreenPanel;
            if (FullscreenPanel) PanelMode = true;
            Status = FullscreenPanel
                ? "Fullscreen ON — panoul stânga ascuns, fereastra maximizată (F11)."
                : "Fullscreen OFF.";
            _journal.Info(Status);
        });
        SyncComputeCommand = new RelayCommand(() =>
        {
            SyncComputeToEngine();
            Status = $"Compute sync: {ComputeRows.Count} canale.";
        });
        OpenRecordingCommand = new RelayCommand(OpenSelectedRecording);

        _engine.SyncJournal += (_, msg) => _dispatcher.Invoke(() =>
        {
            SyncLines.Insert(0, $"{DateTime.Now:HH:mm:ss} {msg}");
            while (SyncLines.Count > 200) SyncLines.RemoveAt(SyncLines.Count - 1);
        });
        _engine.AlarmRaised += (_, msg) => _dispatcher.Invoke(() =>
        {
            AlarmEventLines.Insert(0, $"{DateTime.Now:HH:mm:ss} {msg}");
            while (AlarmEventLines.Count > 300) AlarmEventLines.RemoveAt(AlarmEventLines.Count - 1);
        });

        RefreshRecordings();
        RefreshMacroFlow();
    }

    private void FitCalibration()
    {
        var pts = CalibrationPoints.Select(p => new CalibrationPoint { Raw = p.Raw, Reference = p.Reference }).ToList();
        _lastCalibration = CalibrationWizard.FitLinear(pts);
        Status = $"Cal fit: scale={_lastCalibration.Scale:G6} offset={_lastCalibration.Offset:G6} R²={_lastCalibration.RSquared:0.000} ({_lastCalibration.Notes})";
        _journal.Setup(Status);
    }

    private void ApplyCalibration()
    {
        var idx = ResolveHwIndex(CalChannel);
        if (_lastCalibration is null || idx is null)
        {
            Status = "Fit calibration first / select channel (CH# 1-based).";
            return;
        }
        Channels[idx.Value].Scale = _lastCalibration.Scale;
        Channels[idx.Value].Offset = _lastCalibration.Offset;
        Status = $"Applied calibration to CH{CalChannel}.";
        _journal.Setup(Status);
    }

    private void ShowWiring()
    {
        var idx = ResolveHwIndex(CalChannel);
        if (idx is null) idx = ResolveHwIndexFromUiSelector(SelectedChannelForSensor);
        if (idx is null)
        {
            Status = "Setează CH# (1-based) pentru schema de cablare.";
            return;
        }
        var bridge = Enum.TryParse<BridgeType>(Channels[idx.Value].Bridge, out var b) ? b : BridgeType.None;
        WiringText = WiringDiagrams.Describe(bridge) + Environment.NewLine + Environment.NewLine + WiringDiagrams.AsciiArt(bridge);
        Status = $"Wiring CH{idx.Value + 1}: {bridge}";
    }

    private void OpenSelectedRecording()
    {
        var path = SelectedRecordingEntry?.FilePath ?? SelectedRecordingA ?? RecordingFiles.FirstOrDefault();
        if (path is null || !File.Exists(path))
        {
            Status = "Selectează un CSV/.upet din listă (Refresh dacă e goală).";
            return;
        }
        try
        {
            if (UpetReportFile.HasReportExtension(path) || UpetReportFile.LooksLikeUpetReport(path))
            {
                TryOpenUpetReportPath(path);
                return;
            }

            _offline = OfflineSession.FromCsv(path);
            RefreshAnalysisUi();
            RequestAnalysisTab?.Invoke(this, EventArgs.Empty);
            Status = $"Deschis în Analysis: {Path.GetFileName(path)}";
        }
        catch (Exception ex) { Status = $"Open failed: {ex.Message}"; }
    }

    private string SnapshotsFolder => AppPaths.Snapshots;
    private string TemplatesFolder => AppPaths.Templates;

    private async Task SaveSnapshotAsync()
    {
        await ProjectVersionStore.SaveSnapshotAsync(SnapshotsFolder, BuildProject(), ProjectName);
        SnapshotFiles.Clear();
        foreach (var f in ProjectVersionStore.ListSnapshots(SnapshotsFolder))
            SnapshotFiles.Add(f);
        Status = "Project snapshot saved.";
        _journal.Setup(Status);
    }

    private async Task DiffSnapshotsAsync()
    {
        SnapshotFiles.Clear();
        foreach (var f in ProjectVersionStore.ListSnapshots(SnapshotsFolder))
            SnapshotFiles.Add(f);
        if (SelectedSnapshotA is null) SelectedSnapshotA = SnapshotFiles.FirstOrDefault();
        if (SelectedSnapshotB is null) SelectedSnapshotB = SnapshotFiles.Skip(1).FirstOrDefault();
        if (SelectedSnapshotA is null || SelectedSnapshotB is null)
        {
            Status = "Need two snapshots.";
            return;
        }
        var a = await ProjectVersionStore.LoadSnapshotAsync(SelectedSnapshotA);
        var b = await ProjectVersionStore.LoadSnapshotAsync(SelectedSnapshotB);
        DiffText = ProjectVersionStore.DiffJson(a?.Json ?? "", b?.Json ?? "");
        Status = "Snapshot diff ready.";
    }

    private void InspectBin()
    {
        var dlg = new OpenFileDialog { Filter = "BIN HBM|*.bin|All|*.*" };
        if (dlg.ShowDialog() != true) return;
        var info = CatmanBinImporter.Inspect(dlg.FileName);
        BinPreviewLines.Clear();
        BinPreviewLines.Add(info.Summary);
        foreach (var s in info.Strings.Take(40)) BinPreviewLines.Add("str: " + s);
        foreach (var v in info.PreviewValues.Take(40)) BinPreviewLines.Add("f: " + v.ToString("G6", CultureInfo.InvariantCulture));
        Status = $"Inspected BIN {Path.GetFileName(dlg.FileName)}";
        _journal.Info(Status);
    }

    private void ApplyTemplate()
    {
        var t = DisplayTemplate.BuiltIns.FirstOrDefault(x =>
                    string.Equals(x.Name, SelectedTemplate, StringComparison.OrdinalIgnoreCase))
                ?? DisplayTemplate.CreateDefault();
        ApplyDisplayTemplate(t);
    }

    private void ApplyDisplayTemplate(DisplayTemplate t)
    {
        foreach (var w in t.Widgets.Where(x => x.ChannelIndex >= 0 && x.ChannelIndex < Channels.Count))
            Channels[w.ChannelIndex].Enabled = true;

        if (!string.IsNullOrWhiteSpace(t.PlotMode) && DisplayLayouts.All.Contains(t.PlotMode))
            PlotMode = t.PlotMode;
        else
        {
            PlotMode = t.Name switch
            {
                "Numerics only" => DisplayLayouts.Numeric,
                "Bars focus" => DisplayLayouts.Bar,
                "Forță–Deplasare Y(X)" => DisplayLayouts.Yx,
                "FFT live" => DisplayLayouts.Fft,
                "Y(t) single" => DisplayLayouts.Yt,
                _ => DisplayLayouts.Yt
            };
        }

        if (t.XyXChannel > 0) XyXChannel = t.XyXChannel;
        if (t.XyYChannel > 0) XyYChannel = t.XyYChannel;
        if (t.FftChannel > 0) FftChannel = t.FftChannel;
        PanelMode = t.PanelMode;
        MultiPanelMode = t.MultiPanel;
        FullscreenPanel = false;
        RefreshLiveValueHeaders();
        SelectedTemplate = t.Name;
        if (!TemplateNames.Contains(t.Name))
            TemplateNames.Add(t.Name);
        Status = $"Template '{t.Name}' aplicat → afișaj {PlotMode}, {t.Widgets.Count} widget-uri.";
        _journal.Setup(Status);
    }

    private DisplayTemplate CaptureCurrentTemplate(string name) => new()
    {
        Name = name,
        PlotMode = PlotMode,
        PanelMode = PanelMode,
        MultiPanel = MultiPanelMode,
        XyXChannel = XyXChannel,
        XyYChannel = XyYChannel,
        FftChannel = FftChannel,
        Widgets = Channels.Where(c => c.Enabled).Take(8).Select((c, i) => new PanelWidget
        {
            Type = PlotMode switch
            {
                DisplayLayouts.Bar => PanelWidgetType.Bar,
                DisplayLayouts.Yx => PanelWidgetType.XyPlot,
                DisplayLayouts.Fft => PanelWidgetType.Fft,
                DisplayLayouts.Numeric => PanelWidgetType.Numeric,
                DisplayLayouts.DualYt => PanelWidgetType.DualYt,
                _ => PanelWidgetType.YtPlot
            },
            Title = c.Name,
            ChannelIndex = c.Index,
            Row = i / 4,
            Column = i % 4
        }).ToList()
    };

    private async Task SaveTemplateAsync()
    {
        Directory.CreateDirectory(TemplatesFolder);
        var dlg = new SaveFileDialog
        {
            InitialDirectory = TemplatesFolder,
            Filter = "UPET template (*.s8tpl.json)|*.s8tpl.json|JSON|*.json",
            FileName = (SelectedTemplate ?? "layout").Replace(' ', '_') + ".s8tpl.json"
        };
        if (dlg.ShowDialog() != true) return;
        var t = CaptureCurrentTemplate(Path.GetFileNameWithoutExtension(dlg.FileName));
        await DisplayTemplate.SaveAsync(dlg.FileName, t);
        if (!TemplateNames.Contains(t.Name))
            TemplateNames.Add(t.Name);
        SelectedTemplate = t.Name;
        Status = "Template salvat: " + dlg.FileName;
        _journal.Setup(Status);
    }

    private async Task LoadTemplateAsync()
    {
        var dlg = new OpenFileDialog
        {
            InitialDirectory = Directory.Exists(TemplatesFolder) ? TemplatesFolder : null,
            Filter = "UPET template (*.s8tpl.json)|*.s8tpl.json|JSON|*.json"
        };
        if (dlg.ShowDialog() != true) return;
        var t = await DisplayTemplate.LoadAsync(dlg.FileName);
        ApplyDisplayTemplate(t);
    }

    private void SyncComputeToEngine()
    {
        _engine.ComputeChannels.Clear();
        foreach (var c in ComputeRows)
        {
            _engine.ComputeChannels.Add(new ComputeDefinition
            {
                Name = c.Name,
                Operation = Enum.TryParse<ComputeOp>(c.Operation, out var op) ? op : ComputeOp.MovingAverage,
                SourceChannel = c.SourceChannel,
                Window = c.Window,
                Param = c.Param,
                Enabled = c.Enabled
            });
        }
    }

    private void SyncAdvancedTrigger()
    {
        var chA = Math.Max(0, TriggerChannel - 1);
        var chB = Math.Min(Channels.Count - 1, chA + 1);
        _engine.AdvancedTrigger = new AdvancedTriggerSettings
        {
            Enabled = TriggerEnabled,
            ChannelA = chA,
            ThresholdA = TriggerThreshold,
            RisingA = TriggerRising,
            ChannelB = chB,
            ThresholdB = TriggerThreshold,
            RisingB = TriggerRising,
            Logic = Enum.TryParse<Core.Acquisition.TriggerLogic>(TriggerLogic, out var logic) ? logic : Core.Acquisition.TriggerLogic.Single,
            WindowMode = WindowTrigger,
            WindowLow = TriggerThreshold - 0.1,
            WindowHigh = TriggerThreshold + 0.1,
            PreTriggerMs = PreTriggerMs,
            PostTriggerMs = PostTriggerMs
        };
    }

    private void RefreshMacroFlow()
    {
        MacroFlowLines.Clear();
        MacroFlowLines.Add("[Start]");
        foreach (var s in MacroSteps)
        {
            var label = s.Type switch
            {
                nameof(Core.Macros.MacroStepType.LoopStart) => $"┌─ LOOP x{s.IntParam}",
                nameof(Core.Macros.MacroStepType.LoopEnd) => "└─ END LOOP",
                nameof(Core.Macros.MacroStepType.WaitTrigger) => "◆ WaitTrigger",
                nameof(Core.Macros.MacroStepType.WaitMs) => $"⏱ Wait {s.IntParam} ms",
                nameof(Core.Macros.MacroStepType.SetSampleRate) => $"⚙ SampleRate {s.IntParam} Hz",
                nameof(Core.Macros.MacroStepType.ApplyFilters) => $"⚙ Filter {s.IntParam} Hz",
                nameof(Core.Macros.MacroStepType.WaitOperator) => "⏸ WaitOperator",
                nameof(Core.Macros.MacroStepType.Beep) => "♪ Beep",
                _ => $"• {s.Type}" + (string.IsNullOrWhiteSpace(s.Text) ? "" : $" ({s.Text})")
            };
            MacroFlowLines.Add("  " + label);
        }
        MacroFlowLines.Add("[End]");
    }

    private void ShowDeviceInfo()
    {
        if (_device is CascadedSpider8 cascade)
            DeviceInfoText = cascade.DeviceInfo;
        else if (_device is not null)
            DeviceInfoText = $"{_device.DisplayName}; state={_device.State}; channels={_device.Channels.Count}; rate={_device.SampleRateHz} Hz; frames={_engine.FramesReceived}; gaps≈{_engine.FramesDroppedEstimate}";
        else
            DeviceInfoText = "Not connected.";
        Status = DeviceInfoText;
        _journal.Info(DeviceInfoText);
    }

    /// <summary>Backward-compatible entry; prefer HandleGlobalKey(key, modifiers).</summary>
    public void HandleGlobalKey(Key key) => HandleGlobalKey(key, ModifierKeys.None);
}

public sealed class CalibrationPointRow
{
    public double Raw { get; set; }
    public double Reference { get; set; }
}

public sealed class ComputeRow
{
    public string Name { get; set; } = "Compute";
    public string Operation { get; set; } = nameof(ComputeOp.MovingAverage);
    public int SourceChannel { get; set; }
    public int Window { get; set; } = 25;
    public double Param { get; set; } = 0.1;
    public bool Enabled { get; set; } = true;
}
