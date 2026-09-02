using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Acquisition;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Metrology;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Sensors;
using Spider8DAQ.Core.Time;
using Spider8DAQ.Hardware;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    public ObservableCollection<string> RecordStopModes { get; } = new()
    {
        "Manual", "Duration", "Samples", "Trigger", "Threshold"
    };
    public ObservableCollection<string> StorageModes { get; } = new()
    {
        "Full", "PeakInterval"
    };
    public ObservableCollection<string> Annotations { get; } = new();
    public ObservableCollection<string> PreflightLines { get; } = new();

    public ICommand ShuntAllCommand { get; private set; } = null!;
    public ICommand PreflightCommand { get; private set; } = null!;
    public ICommand AddAnnotationCommand { get; private set; } = null!;
    public ICommand ClearAnnotationsCommand { get; private set; } = null!;
    public ICommand FftRegionCommand { get; private set; } = null!;
    public ICommand CwtRegionCommand { get; private set; } = null!;
    public ICommand ExportSpectrumCommand { get; private set; } = null!;
    public ICommand LoadExampleProjectCommand { get; private set; } = null!;
    public ICommand ToggleMultiPanelCommand { get; private set; } = null!;
    public ICommand RenameSelectedChannelCommand { get; private set; } = null!;
    public ICommand CopyAnalysisStatsCommand { get; private set; } = null!;
    public ICommand ToggleDisplayFreezeCommand { get; private set; } = null!;
    public ICommand CopyLiveValuesCommand { get; private set; } = null!;
    public ICommand MarkRecordingCommand { get; private set; } = null!;
    public ICommand ExportLiveValuesCommand { get; private set; } = null!;
    public ICommand RecAllEnabledCommand { get; private set; } = null!;
    public ICommand RecNoneCommand { get; private set; } = null!;
    public ICommand PlotAllEnabledCommand { get; private set; } = null!;
    public ICommand PlotNoneCommand { get; private set; } = null!;
    public ICommand ApplyDefaultFiltersCommand { get; private set; } = null!;

    private string _recordStopMode = "Manual";
    private string _storageMode = "Full";
    private double _peakIntervalSeconds = 1.0;
    private bool _statsJournalEnabled = true;
    private double _statsIntervalSeconds = 1;
    private bool _stopRecordOnAlarm;
    private bool _multiPanelMode;
    private bool _displayFrozen;
    private string _annotationText = "";
    private string _statsJournalStatus = "Jurnal statistici: oprit";
    private string _selectedExample = "traction";
    private double[]? _lastSpectrum;
    private double _lastSpectrumDf;
    private double _shuntAllowedDifferencePercent = ShuntCheck.DefaultTolerancePercent;
    private string _lastShuntStatus = "";
    private string? _lastConnectFirmware;
    private bool _internalShuntFromSpider830;

    public ObservableCollection<string> ExampleProjects { get; } = new()
    {
        "traction", "yx", "strain", "presa3"
    };

    public string SelectedExample
    {
        get => _selectedExample;
        set { _selectedExample = value; OnPropertyChanged(); }
    }

    public ObservableCollection<double> ShuntAllowedDifferenceOptions { get; } = new()
    {
        0.5, 1, 2, 3, 5
    };

    /// <summary>Last Shunt CH / Shunt all status (wrapped in Canale DAQ; not overwritten by live Status).</summary>
    public string LastShuntStatus
    {
        get => _lastShuntStatus;
        set { _lastShuntStatus = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>catman Easy A05566 §4.20 — percent of expected unbalance (0.5%–5%).</summary>
    public double ShuntAllowedDifferencePercent
    {
        get => _shuntAllowedDifferencePercent;
        set
        {
            var v = ShuntCheck.ClampAllowedDifferencePercent(value);
            if (Math.Abs(_shuntAllowedDifferencePercent - v) < 1e-9) return;
            _shuntAllowedDifferencePercent = v;
            OnPropertyChanged();
            LabUiPrefs.ShuntAllowedDifferencePercent = v;
        }
    }

    public string RecordStopMode
    {
        get => _recordStopMode;
        set
        {
            _recordStopMode = value;
            ApplyRecordStopMode();
            OnPropertyChanged();
        }
    }

    public string StorageMode
    {
        get => _storageMode;
        set { _storageMode = value; OnPropertyChanged(); }
    }

    public double PeakIntervalSeconds
    {
        get => _peakIntervalSeconds;
        set { _peakIntervalSeconds = Math.Max(0.05, value); OnPropertyChanged(); }
    }

    public bool StatsJournalEnabled
    {
        get => _statsJournalEnabled;
        set { _statsJournalEnabled = value; OnPropertyChanged(); }
    }

    public double StatsIntervalSeconds
    {
        get => _statsIntervalSeconds;
        set { _statsIntervalSeconds = Math.Max(0.1, value); OnPropertyChanged(); }
    }

    public bool StopRecordOnAlarm
    {
        get => _stopRecordOnAlarm;
        set { _stopRecordOnAlarm = value; OnPropertyChanged(); }
    }

    public bool MultiPanelMode
    {
        get => _multiPanelMode;
        set
        {
            _multiPanelMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowPlots));
            OnPropertyChanged(nameof(ShowPlotB));
            OnPropertyChanged(nameof(ShowNumericPanel));
            UiLayoutChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Freeze live UI (plot/numeric) while acquisition continues — catman Easy–like Hold.</summary>
    public bool DisplayFrozen
    {
        get => _displayFrozen;
        set
        {
            if (_displayFrozen == value) return;
            _displayFrozen = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayFreezeLabel));
            Status = _displayFrozen
                ? "Hold afișaj ON — achiziția continuă; plot/valori înghețate (Pause)."
                : "Hold afișaj OFF — live repornit.";
            RefreshOperatorStatus();
        }
    }

    public string DisplayFreezeLabel => DisplayFrozen ? "Hold ON" : "Hold";

    public string AnnotationText
    {
        get => _annotationText;
        set { _annotationText = value; OnPropertyChanged(); }
    }

    public string StatsJournalStatus
    {
        get => _statsJournalStatus;
        set { _statsJournalStatus = value; OnPropertyChanged(); }
    }

    private void WireLabCommands()
    {
        try
        {
            var loaded = LabUiPrefs.ShuntAllowedDifferencePercent;
            _shuntAllowedDifferencePercent = ShuntCheck.ClampAllowedDifferencePercent(loaded);
            if (!ShuntAllowedDifferenceOptions.Contains(_shuntAllowedDifferencePercent))
                ShuntAllowedDifferenceOptions.Insert(0, _shuntAllowedDifferencePercent);
        }
        catch
        {
            _shuntAllowedDifferencePercent = ShuntCheck.DefaultTolerancePercent;
        }

        ShuntAllCommand = new RelayCommand(async () => await RunShuntAllAsync(), () => IsConnected);
        PreflightCommand = new RelayCommand(async () => await RunPreflightAsync());
        AddAnnotationCommand = new RelayCommand(AddAnnotation);
        ClearAnnotationsCommand = new RelayCommand(() =>
        {
            Annotations.Clear();
            RefreshAnalysisPlot();
            Status = "Adnotări șterse.";
        });
        FftRegionCommand = new RelayCommand(RunFftRegion);
        CwtRegionCommand = new RelayCommand(RunCwtRegion);
        ExportSpectrumCommand = new RelayCommand(ExportSpectrum);
        LoadExampleProjectCommand = new RelayCommand(async () => await LoadExampleAsync(SelectedExample));
        ToggleMultiPanelCommand = new RelayCommand(() => MultiPanelMode = !MultiPanelMode);
        RenameSelectedChannelCommand = new RelayCommand(() =>
        {
            var ch = ResolveSelectedHwChannel();
            if (ch is null)
            {
                Status = "Selectați un canal, apoi Redenumește — editați coloana Nume.";
                return;
            }
            Status = $"Redenumire: editați coloana Nume pentru «{ch.Name}» (dublu-click pe celulă).";
            HelpPanelText = "Redenumire canal: dublu-click pe coloana Nume din grilă. Context menu doar ghidează.";
            RequestMeasureTab?.Invoke(this, EventArgs.Empty);
        });
        CopyAnalysisStatsCommand = new RelayCommand(() =>
        {
            if (AnalysisStats.Count == 0)
            {
                Status = "Încarcă CSV în Analiză pentru statistici.";
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine("Ch\tN\tMin\tMax\tMean\tσ\tRMS\tP2P\tCrest");
            foreach (var s in AnalysisStats)
                sb.AppendLine($"{s.Name}\t{s.Count}\t{s.Min}\t{s.Max}\t{s.Mean}\t{s.StdDev}\t{s.Rms}\t{s.PeakToPeak}\t{s.Crest}");
            try
            {
                System.Windows.Clipboard.SetText(sb.ToString());
                Status = $"Statistici Analysis copiate ({AnalysisStats.Count} canale).";
            }
            catch (Exception ex)
            {
                Status = "Clipboard: " + ex.Message;
            }
        });
        ToggleDisplayFreezeCommand = new RelayCommand(() =>
        {
            DisplayFrozen = !DisplayFrozen;
            HelpPanelText = DisplayFrozen
                ? "Hold: UI înghețat ca Pause/Freeze catman. Record/USB nu se opresc. Pause din nou pentru live."
                : HelpPanelText;
        });
        CopyLiveValuesCommand = new RelayCommand(() =>
        {
            if (LiveValues.Count == 0)
            {
                Status = "Nicio valoare live — Start măsurare întâi.";
                return;
            }
            var sb = new StringBuilder();
            sb.AppendLine($"PC\t{AppClock.FormatIso(AppClock.Now)}");
            sb.AppendLine("Name\tValue\tUnit\tAlarm");
            foreach (var lv in LiveValues)
                sb.AppendLine($"{lv.Name}\t{lv.Display}\t{lv.Unit}\t{(lv.IsAlarm ? "ALM" : "")}");
            try
            {
                System.Windows.Clipboard.SetText(sb.ToString());
                Status = $"Live values copiate ({LiveValues.Count}){(DisplayFrozen ? " [Hold]" : "")}.";
            }
            catch (Exception ex)
            {
                Status = "Clipboard: " + ex.Message;
            }
        });
        MarkRecordingCommand = new RelayCommand(() =>
        {
            if (!IsRecording)
            {
                Status = "Marcaj CSV: porniți Record (F7) întâi.";
                return;
            }
            var label = string.IsNullOrWhiteSpace(AnnotationText) ? "mark" : AnnotationText.Trim();
            if (_engine.TryWriteRecordingMark(label))
            {
                var line = $"{DateTime.Now:HH:mm:ss}  {label}";
                Annotations.Insert(0, line);
                while (Annotations.Count > 200) Annotations.RemoveAt(Annotations.Count - 1);
                var chMark = ResolveSelectedHwChannel() ?? Channels.FirstOrDefault(c => c.Enabled);
                if (chMark is not null && TryParseLiveDisplay(chMark, out var markVal))
                {
                    LastMarkValue = markVal;
                    if (PlotShowLastMarkLine) RebuildPlotSeries();
                }
                Status = "Marcaj CSV: # MARK " + label;
            }
            else Status = "Marcaj eșuat (înregistrare neactivă).";
        }, () => IsRecording);
        ExportLiveValuesCommand = new RelayCommand(() =>
        {
            if (LiveValues.Count == 0)
            {
                Status = "Nicio valoare live — Start măsurare întâi.";
                return;
            }
            var dlg = new SaveFileDialog
            {
                Title = "Export valori live",
                Filter = "CSV|*.csv|Text|*.txt",
                FileName = $"live_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            if (dlg.ShowDialog() != true) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# UPET AcqLab live snapshot");
                sb.AppendLine($"# PC={AppClock.FormatIso(AppClock.Now)}");
                sb.AppendLine($"# Hold={DisplayFrozen}");
                sb.AppendLine("Name,Value,Unit,Alarm");
                foreach (var lv in LiveValues)
                {
                    var v = (lv.Display ?? "").Replace(',', '.');
                    sb.AppendLine($"{EscapeCsv(lv.Name)},{v},{EscapeCsv(lv.Unit)},{(lv.IsAlarm ? "ALM" : "")}");
                }
                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                Status = "Live snapshot: " + dlg.FileName;
            }
            catch (Exception ex)
            {
                Status = "Export live: " + ex.Message;
            }
        });
        RecAllEnabledCommand = new RelayCommand(() =>
        {
            var n = 0;
            foreach (var ch in Channels.Where(c => c.Enabled))
            {
                ch.RecordEnabled = true;
                n++;
            }
            Status = $"Rec ON pe {n} canale activate (store flag).";
        });
        RecNoneCommand = new RelayCommand(() =>
        {
            foreach (var ch in Channels) ch.RecordEnabled = false;
            Status = "Rec OFF pe toate canalele.";
        });
        PlotAllEnabledCommand = new RelayCommand(() =>
        {
            var n = 0;
            foreach (var ch in Channels.Where(c => c.Enabled))
            {
                ch.ShowOnPlot = true;
                n++;
            }
            Status = $"Graf ON pe {n} canale activate.";
            RebuildPlotSeries();
        });
        PlotNoneCommand = new RelayCommand(() =>
        {
            foreach (var ch in Channels) ch.ShowOnPlot = false;
            Status = "Graf OFF pe toate canalele.";
            RebuildPlotSeries();
        });
        ApplyDefaultFiltersCommand = new RelayCommand(() =>
        {
            // Easy-like: Bessel ~ SampleRate/10 (min 1 Hz) — hardware FI + software LPF
            var f = Math.Max(1.0, SampleRateHz / 10.0);
            var n = 0;
            foreach (var ch in Channels.Where(c => c.Enabled))
            {
                ch.FilterHz = f;
                n++;
            }

            if (string.IsNullOrWhiteSpace(MetrologyFilterKind)
                || MetrologyFilterKind == nameof(DigitalFilterKind.Off))
                MetrologyFilterKind = nameof(DigitalFilterKind.Bessel);
            else
                ApplyMetrologyFilterConfig();
            Status = $"Filtru BE ≈ {f:0.##} Hz pe {n} canale ON (SampleRate/10) — SoftSetup + LPF software.";
        });

        _engine.StatisticsTick += (_, msg) => _dispatcher.Invoke(() =>
        {
            StatsJournalStatus = "Stats: " + msg;
        });
        _engine.RecordingStopped += (_, _) => _dispatcher.Invoke(() =>
        {
            if (!string.IsNullOrWhiteSpace(_engine.LastStatisticsJournalPath))
                StatsJournalStatus = "Jurnal statistici: " + _engine.LastStatisticsJournalPath;
        });
    }

    private static string EscapeCsv(string? s)
    {
        s ??= "";
        return s.Contains(',') || s.Contains('"') || s.Contains('\n')
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }

    private void ApplyRecordStopMode()
    {
        switch (RecordStopMode)
        {
            case "Duration":
                TriggerEnabled = false;
                if (MaxSeconds is null or <= 0) MaxSeconds = 10;
                MaxSamples = null;
                break;
            case "Samples":
                TriggerEnabled = false;
                if (MaxSamples is null or <= 0) MaxSamples = 500;
                MaxSeconds = null;
                break;
            case "Trigger":
                TriggerEnabled = true;
                break;
            case "Threshold":
                TriggerEnabled = false;
                MaxSeconds = null;
                MaxSamples = null;
                if (RecordThresholdValue <= 0) RecordThresholdValue = 1000;
                break;
            default:
                TriggerEnabled = false;
                MaxSeconds = null;
                MaxSamples = null;
                break;
        }
    }

    private async Task<string?> RunShuntAllAsync(bool clearLines = true)
    {
        if (_device is null) return null;
        if (clearLines)
            PreflightLines.Clear();
        var pass = 0;
        var fail = 0;
        var sim = 0;
        string? firstSug = null;
        for (var i = 0; i < Channels.Count; i++)
        {
            if (!Channels[i].Enabled) continue;
            if (Channels[i].Name.Contains("DI", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                var reading = await _device.ShuntCheckAsync(i);
                reading = FallbackShuntReading(Channels[i], reading);
                var result = ApplyShuntEvaluation(Channels[i], reading);
                if (result.Kind == ShuntCheckKind.Simulator)
                    sim++;
                else if (result.Kind == ShuntCheckKind.Skipped)
                    { /* force/DC without Timbru — not a shunt FAIL */ }
                else if (result.Kind == ShuntCheckKind.Info || result.Passed)
                    pass++;
                else
                {
                    fail++;
                    if (firstSug is null && !string.IsNullOrWhiteSpace(result.Suggestion))
                        firstSug = result.Suggestion;
                }
                PreflightLines.Add(result.StatusLine);
                _journal.Setup(result.StatusLine);
            }
            catch (Exception ex)
            {
                fail++;
                var line = $"FAIL  CH{i} {Channels[i].Name}  {ex.Message}";
                if (ShuntCheck.LooksLikeEstLedError(DeviceErrorText))
                {
                    firstSug ??= ShuntCheck.SuggestEstError;
                    line += "\n" + ShuntCheck.FormatSuggestionLine(ShuntCheck.SuggestEstError);
                }
                PreflightLines.Add(line);
                _journal.Error(line);
            }
        }

        var summary = sim > 0 && pass == 0 && fail == 0
            ? $"Shunt all: {sim} canal(e) — {ShuntCheck.SimulatorLabel}"
            : $"Shunt all: {pass} PASS, {fail} FAIL"
              + (sim > 0 ? $", {sim} simulator" : "");
        Status = fail > 0 && firstSug is not null
            ? summary + "  " + ShuntCheck.FormatSuggestionLine(firstSug)
            : summary;
        HelpPanelText = Status;
        await _device.ApplyChannelConfigAsync(Channels.Select(ToConfig));
        CommandManager.InvalidateRequerySuggested();
        return firstSug;
    }

    private async Task RunPreflightAsync()
    {
        PreflightLines.Clear();
        PreflightLines.Add("=== Preflight UPET AcqLab ===");
        PreflightLines.Add($"Backend: {SelectedBackend} · țintă: {SelectedPort ?? "(none)"}");
        PreflightLines.Add(HbmUsbDeviceScanner.IsUsbHbmPresent()
            ? "PnP USBHBM: prezent"
            : "PnP USBHBM: absent");
        PreflightLines.Add(IsCatmanEasyRunning()
            ? "catman Easy: RULEAZĂ (închideți înainte de Connect USB)"
            : "catman Easy: oprit");
        try
        {
            PreflightLines.Add(Intfac32Native.IsAvailable()
                ? $"Intfac32: OK · handles={Intfac32Native.GetNumOpenHandles()}"
                : "Intfac32: lipsă (DEST SoftSetup fallback pe HBM USB)");
        }
        catch (Exception ex)
        {
            PreflightLines.Add("Intfac: " + ex.Message);
        }
        var on = Channels.Count(c => c.Enabled);
        var rec = Channels.Count(c => c.Enabled && c.RecordEnabled);
        PreflightLines.Add($"Canale On={on} · Rec={rec} · Storage={StorageMode}");
        if (DisplayFrozen)
            PreflightLines.Add("Hold afișaj: ON (Pause pentru live)");
        var bridges = Channels.Where(c => c.Enabled).Select(c => $"{c.Name}:{c.Bridge}").ToList();
        if (bridges.Count > 0)
            PreflightLines.Add("Punte: " + string.Join(", ", bridges));

        PreflightLines.Add($"Filtru SW: {MetrologyFilterKind} · cutoff=FilterHz · sync: Timestamp+Sequence comun");
        var cfgs = Channels.Select(ToConfig).ToList();
        foreach (var line in ConnectDiagnostics.BuildConnectStatusLines(cfgs, id =>
                 {
                     var s = _sensorLibrary.Sensors.FirstOrDefault(x =>
                         string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)
                         || string.Equals(x.Code, id, StringComparison.OrdinalIgnoreCase));
                     return s?.ExcitationV;
                 }).Take(5))
            PreflightLines.Add("WARN: " + line);

        if (_device is null || !IsConnected)
        {
            PreflightLines.Add("Connect lipsă — tare/shunt sărite (Simulator/Serial/HBM USB → Connect).");
            Status = "Preflight parțial (fără dispozitiv).";
            HelpPanelText = Status;
            return;
        }

        PreflightLines.Add("--- Tare + Shunt ---");
        await TareAsync(null);
        PreflightLines.Add("TARE  all channels — OK");
        var firstSug = await RunShuntAllAsync(clearLines: false);
        var fails = PreflightLines.Count(IsShuntOrPreflightFailLine);
        if (IsSimulatedShuntBackend())
        {
            Status = "Preflight: " + ShuntCheck.SimulatorLabel + " — fără PASS vs Rsh.";
        }
        else
        {
            Status = fails == 0
                ? "Preflight PASS — gata de măsurătoare."
                : $"Preflight cu {fails} FAIL — verifică cablajul/shunt / Timbru / Rsh kΩ.";
            if (fails > 0 && !string.IsNullOrWhiteSpace(firstSug))
                Status += "  " + ShuntCheck.FormatSuggestionLine(firstSug);
        }
        HelpPanelText = Status;
    }

    private static bool IsShuntOrPreflightFailLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        if (line.StartsWith("FAIL", StringComparison.Ordinal)) return true;
        if (line.Contains("PASS", StringComparison.Ordinal)) return false;
        if (line.Contains("Nu e FAIL de cablaj", StringComparison.Ordinal)) return false;
        if (line.Contains("shunt intern NU e în punte", StringComparison.Ordinal)) return false;
        if (line.Contains("SKIP verificare quarter", StringComparison.Ordinal)) return false;
        if (line.Contains(ShuntCheck.SimulatorLabel, StringComparison.Ordinal)) return false;
        if (line.Contains("sărit —", StringComparison.Ordinal)) return false;
        return line.Contains("FAIL", StringComparison.Ordinal)
               || line.Contains("lipsește", StringComparison.Ordinal);
    }

    private void AddAnnotation()
    {
        if (_offline is null)
        {
            Status = "Load Analysis CSV before annotating.";
            return;
        }
        var idx = Math.Clamp(CursorA, 0, Math.Max(0, _offline.Timestamps.Count - 1));
        var label = string.IsNullOrWhiteSpace(AnnotationText) ? "mark" : AnnotationText.Trim();
        var line = $"@{idx}  t={_offline.Timestamps[idx]:HH:mm:ss.fff}  {label}";
        Annotations.Add(line);
        Status = "Adnotare: " + line;
        RefreshAnalysisPlot();
    }

    private void RunFftRegion()
    {
        if (_offline is null || _offline.Columns.Count == 0)
        {
            Status = "Load Analysis CSV first.";
            return;
        }
        var ch = Math.Clamp(AnalysisChannel, 0, _offline.Columns.Count - 1);
        var a = Math.Clamp(Math.Min(CursorA, CursorB), 0, _offline.Columns[ch].Length - 1);
        var b = Math.Clamp(Math.Max(CursorA, CursorB), a, _offline.Columns[ch].Length - 1);
        var slice = _offline.Columns[ch].Skip(a).Take(b - a + 1).ToArray();
        var mag = LiveFft.Magnitude(slice);
        if (mag.Length == 0)
        {
            Status = "FFT regiune: prea puține eșantioane (min ~8 power-of-2).";
            return;
        }

        var dt = 1.0 / Math.Max(1, SampleRateHz);
        if (_offline.Timestamps.Count > 1)
        {
            var span = (_offline.Timestamps[Math.Min(b, _offline.Timestamps.Count - 1)] -
                        _offline.Timestamps[a]).TotalSeconds;
            if (span > 1e-9)
                dt = span / Math.Max(1, b - a);
        }
        _lastSpectrumDf = 1.0 / (mag.Length * 2 * dt);
        _lastSpectrum = mag;

        if (_plotAnalysis is not null)
        {
            _plotAnalysis.Plot.Clear();
            var freqs = Enumerable.Range(0, mag.Length).Select(i => i * _lastSpectrumDf).ToArray();
            var s = _plotAnalysis.Plot.Add.Scatter(freqs, mag);
            s.LegendText = $"FFT {_offline.ChannelNames[ch]} [{a}:{b}]";
            s.MarkerSize = 0;
            _plotAnalysis.Plot.Axes.Bottom.Label.Text = "Hz";
            _plotAnalysis.Plot.Axes.Left.Label.Text = "|Y|";
            _plotAnalysis.Plot.Axes.AutoScale();
            _plotAnalysis.Refresh();
        }

        Status = $"FFT regiune: {mag.Length} bins, Δf≈{_lastSpectrumDf:0.###} Hz";
        RequestAnalysisTab?.Invoke(this, EventArgs.Empty);
    }

    private void ExportSpectrum()
    {
        if (_lastSpectrum is null || _lastSpectrum.Length == 0)
        {
            Status = "Rulează FFT regiune întâi.";
            return;
        }
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "spectrum.csv" };
        if (dlg.ShowDialog() != true) return;
        var sb = new StringBuilder();
        sb.AppendLine("FrequencyHz,Magnitude");
        for (var i = 0; i < _lastSpectrum.Length; i++)
        {
            sb.Append((_lastSpectrumDf * i).ToString("G9", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(_lastSpectrum[i].ToString("G9", CultureInfo.InvariantCulture));
            sb.AppendLine();
        }
        File.WriteAllText(dlg.FileName, sb.ToString());
        Status = "Spectrum: " + dlg.FileName;
    }

    private async Task LoadExampleAsync(string? id)
    {
        var dir = GetWritableExamplesDirectory();
        EnsureExampleProjects(dir);
        var file = id switch
        {
            "yx" => "upet_forta_deplasare.s8proj",
            "strain" => "upet_punte_tensometrica.s8proj",
            "presa3" => "upet_3senzori_presa.s8proj",
            _ => "upet_tractiune.s8proj"
        };
        var path = Path.Combine(dir, file);
        if (!File.Exists(path))
        {
            // Fallback: examples shipped next to the EXE (read-only install).
            var bundled = Path.Combine(AppContext.BaseDirectory, "examples", file);
            if (File.Exists(bundled)) path = bundled;
            else
            {
                Status = "Exemplu lipsă: " + path;
                return;
            }
        }
        try
        {
            var project = await ProjectStore.LoadAsync(path);
            ApplyProject(project);
            Status = $"Proiect exemplu încărcat: {project.Name}";
            _journal.Setup(Status);
        }
        catch (Exception ex)
        {
            Status = "Exemplu: " + ex.Message;
        }
    }

    public void EnsureStartupAssets()
    {
        try
        {
            EnsureExampleProjects(GetWritableExamplesDirectory());
        }
        catch
        {
            // ignore
        }

        try
        {
            UpetReportFileAssociation.EnsureRegistered();
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>Open a .upet path (double-click / command line).</summary>
    public void TryOpenUpetReportPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        if (!UpetReportFile.HasReportExtension(path) &&
            !UpetReportFile.LooksLikeUpetReport(path))
            return;

        try
        {
            _offline = UpetReportFile.Load(path);
            _replayMode = true;
            Annotations.Clear();
            ApplyAttachedMetaFromOffline(_offline);
            RefreshAnalysisUi();
            ApplyFingerprintVerification(path);
            RequestAnalysisTab?.Invoke(this, EventArgs.Empty);
            try { ScanOfflineDefectsNow(); } catch { /* ignore */ }
            var photos = CountMontageInSession(_offline);
            Status = photos > 0
                ? $"Raport UPET deschis: {_offline.Timestamps.Count:N0} eșantioane · {photos} poză(e) montaj — {path}"
                : $"Raport UPET deschis: {_offline.Timestamps.Count:N0} eșantioane — {path}";
            if (!string.IsNullOrWhiteSpace(FingerprintStatusText))
                Status += " · " + FingerprintStatusText;
            _journal.Info(Status);
        }
        catch (Exception ex)
        {
            Status = "Open .upet eșuat: " + ex.Message;
        }
    }

    /// <summary>LocalAppData examples folder (writable under Program Files installs).</summary>
    private static string GetWritableExamplesDirectory()
    {
        try
        {
            return AppPaths.EnsureWritable(AppPaths.Examples);
        }
        catch
        {
            var fallback = AppPaths.BundledExamples;
            try { Directory.CreateDirectory(fallback); } catch { /* ignore */ }
            return fallback;
        }
    }

    /// <summary>LocalAppData recordings folder (writable under Program Files installs).</summary>
    internal static string GetWritableRecordingsDirectory()
    {
        try
        {
            return AppPaths.EnsureWritable(AppPaths.Recordings);
        }
        catch
        {
            var fallback = Path.Combine(AppContext.BaseDirectory, "recordings");
            try { Directory.CreateDirectory(fallback); } catch { /* ignore */ }
            return fallback;
        }
    }

    private static void EnsureExampleProjects(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            WriteExampleIfMissing(Path.Combine(dir, "upet_tractiune.s8proj"), new ProjectFile
            {
                Name = "UPET Tracțiune",
                DeviceMode = "Simulator",
                SampleRateHz = 50,
                PlotMode = "YT",
                Meta = new ProjectMeta { SampleId = "TRAC-001", Comment = "Încercare tracțiune — exemplu laborator UPET", Location = "UPET Lab" },
                Channels = Enumerable.Range(0, 8).Select(i => new Core.Devices.ChannelConfig
                {
                    Index = i,
                    Name = i == 0 ? "Forță" : i == 1 ? "Deplasare" : $"CH{i + 1}",
                    Unit = i == 0 ? "kN" : i == 1 ? "mm" : "mV/V",
                    Enabled = i < 2,
                    Scale = i == 0 ? 10 : 1,
                    Bridge = i < 2 ? Core.Devices.BridgeType.Full : Core.Devices.BridgeType.None,
                    LastShuntReading = 0,
                    AlarmLow = -1e9,
                    AlarmHigh = 1e9
                }).ToList()
            });

            WriteExampleIfMissing(Path.Combine(dir, "upet_forta_deplasare.s8proj"), new ProjectFile
            {
                Name = "UPET Forță–Deplasare Y(X)",
                DeviceMode = "Simulator",
                SampleRateHz = 100,
                PlotMode = "YX",
                XyXChannel = 1,
                XyYChannel = 0,
                Meta = new ProjectMeta { SampleId = "YX-001", Comment = "Y(X) forță vs deplasare + fit în Analysis", Location = "UPET Lab" },
                Channels = Enumerable.Range(0, 8).Select(i => new Core.Devices.ChannelConfig
                {
                    Index = i,
                    Name = i == 0 ? "Forță" : i == 1 ? "Deplasare" : $"CH{i + 1}",
                    Unit = i == 0 ? "kN" : i == 1 ? "mm" : "mV/V",
                    Enabled = i < 2,
                    Scale = 1,
                    LastShuntReading = 0,
                    AlarmLow = -1e9,
                    AlarmHigh = 1e9
                }).ToList()
            });

            WriteExampleIfMissing(Path.Combine(dir, "upet_punte_tensometrica.s8proj"), new ProjectFile
            {
                Name = "UPET Punte tensometrică",
                DeviceMode = "Simulator",
                SampleRateHz = 50,
                PlotMode = "Numeric",
                Meta = new ProjectMeta { SampleId = "STR-001", Comment = "Punte ¼ / ½ — tare + shunt preflight", Location = "UPET Lab" },
                Channels = Enumerable.Range(0, 8).Select(i => new Core.Devices.ChannelConfig
                {
                    Index = i,
                    Name = $"SG{i + 1}",
                    Unit = "µm/m",
                    Enabled = i < 4,
                    Bridge = Core.Devices.BridgeType.Quarter,
                    ExcitationV = 2.5,
                    RangeMvPerV = 2,
                    Scale = 2000,
                    GaugeFactor = 2,
                    GaugeOhm = 350,
                    ShuntKohm = 100,
                    LastShuntReading = 0,
                    AlarmLow = -1e9,
                    AlarmHigh = 1e9
                }).ToList()
            });

            WriteExampleIfMissing(Path.Combine(dir, "upet_3senzori_presa.s8proj"), new ProjectFile
            {
                Name = "UPET 3 senzori (presă)",
                DeviceMode = "Simulator",
                SampleRateHz = 50,
                PlotMode = "Dual Y(t)",
                Recording = new RecordingSettings { StorageMode = "Full", PeakIntervalSeconds = 1 },
                Meta = new ProjectMeta
                {
                    SampleId = "PRESS-3CH",
                    Comment = "Forță + P15 presiune DcVoltage + deplasare — Dual Y(t)",
                    Location = "UPET Lab"
                },
                Channels = Enumerable.Range(0, 8).Select(i => new Core.Devices.ChannelConfig
                {
                    Index = i,
                    Name = i switch { 0 => "Forță", 1 => "Presiune", 2 => "Deplasare", _ => $"CH{i}" },
                    Unit = i switch { 0 => "kN", 1 => "bar", 2 => "mm", _ => "mV/V" },
                    Enabled = i < 3,
                    RecordEnabled = i < 3,
                    Scale = i switch { 0 => 10, 1 => 20, _ => 1 },
                    Bridge = i == 1 ? Core.Devices.BridgeType.DcVoltage : i < 3 ? Core.Devices.BridgeType.Full : Core.Devices.BridgeType.None,
                    ExcitationV = i == 1 ? 0 : 2.5,
                    LastShuntReading = 0,
                    AlarmLow = -1e9,
                    AlarmHigh = 1e9
                }).ToList()
            });
        }
        catch
        {
            // ignore
        }
    }

    private static void WriteExampleIfMissing(string path, ProjectFile project)
    {
        try
        {
            if (File.Exists(path) && new FileInfo(path).Length > 0) return;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            ProjectStore.Save(path, project);
        }
        catch
        {
            // ignore single file
        }
    }
}
