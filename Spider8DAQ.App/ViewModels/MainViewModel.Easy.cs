using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using Spider8DAQ.Core;
using Spider8DAQ.Core.MathChannels;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    private FormulaLibrary _formulaLibrary = FormulaLibrary.CreateDefault();
    private string FormulasPath => AppPaths.FormulasLibrary;

    private void EnsureLocalFormulasSeed()
    {
        var path = FormulasPath;
        if (File.Exists(path)) return;
        var bundled = AppPaths.BundledVendorFile("formulas.json");
        try
        {
            AppPaths.EnsureWritable(AppPaths.Vendor);
            if (File.Exists(bundled))
                File.Copy(bundled, path, overwrite: false);
        }
        catch { /* LoadAsync creates defaults */ }
    }

    public ObservableCollection<FormulaEntry> FormulaEntries { get; } = new();
    public ICommand IntegrateAnalysisCommand { get; private set; } = null!;
    public ICommand DifferentiateAnalysisCommand { get; private set; } = null!;
    public ICommand OutlierAnalysisCommand { get; private set; } = null!;
    public ICommand LowPassAnalysisCommand { get; private set; } = null!;
    public ICommand ScaleOffsetAnalysisCommand { get; private set; } = null!;
    public ICommand FitXyAnalysisCommand { get; private set; } = null!;
    public ICommand FitPoly2AnalysisCommand { get; private set; } = null!;
    public ICommand SaveFormulaCommand { get; private set; } = null!;
    public ICommand ApplyFormulaFromLibraryCommand { get; private set; } = null!;
    public ICommand ReloadFormulasCommand { get; private set; } = null!;
    public ICommand JobNextCommand { get; private set; } = null!;
    public ICommand JobPrevCommand { get; private set; } = null!;
    public ICommand JobRunRecordCommand { get; private set; } = null!;
    public ICommand JobExportReportCommand { get; private set; } = null!;
    public ICommand JobApplyRateFilterCommand { get; private set; } = null!;
    public ICommand JobSyncChecklistCommand { get; private set; } = null!;
    public ICommand JobApplyVizCommand { get; private set; } = null!;

    private string _fitResultText = "Fit Y(X): —";
    private string _analysisScale = "1";
    private string _analysisOffset = "0";
    private int _fitXChannel = 1;
    private int _fitYChannel = 2;
    private int _jobStep;
    private string _jobChecklist = "";
    private double _jobFilterHz = 10;
    private string _jobStatusSummary = "";
    private FormulaEntry? _selectedFormula;

    public string FitResultText { get => _fitResultText; set { _fitResultText = value; OnPropertyChanged(); } }
    public string AnalysisScale { get => _analysisScale; set { _analysisScale = value; OnPropertyChanged(); } }
    public string AnalysisOffset { get => _analysisOffset; set { _analysisOffset = value; OnPropertyChanged(); } }
    public int FitXChannel { get => _fitXChannel; set { _fitXChannel = value; OnPropertyChanged(); } }
    public int FitYChannel { get => _fitYChannel; set { _fitYChannel = value; OnPropertyChanged(); } }
    public int JobStep { get => _jobStep; set { _jobStep = value; OnPropertyChanged(); RefreshJobChecklist(); } }
    public string JobChecklist { get => _jobChecklist; set { _jobChecklist = value; OnPropertyChanged(); } }
    public double JobFilterHz
    {
        get => _jobFilterHz;
        set { _jobFilterHz = Math.Max(0.1, value); OnPropertyChanged(); }
    }
    public string JobStatusSummary { get => _jobStatusSummary; set { _jobStatusSummary = value; OnPropertyChanged(); } }
    public FormulaEntry? SelectedFormula
    {
        get => _selectedFormula;
        set { _selectedFormula = value; OnPropertyChanged(); }
    }

    private void WireEasyCommands()
    {
        IntegrateAnalysisCommand = new RelayCommand(() =>
        {
            if (_offline is null) { Status = "Încarcă Analysis CSV întâi."; return; }
            var region = Math.Abs(CursorA - CursorB) > 0;
            _offline.IntegrateChannel(AnalysisChannel, regionOnly: region);
            RefreshAnalysisUi();
            Status = region
                ? $"Integrală pe CH{AnalysisChannel + 1} (regiune CursorA–B)."
                : $"Integrală pe CH{AnalysisChannel + 1}.";
        });
        DifferentiateAnalysisCommand = new RelayCommand(() =>
        {
            if (_offline is null) { Status = "Încarcă Analysis CSV întâi."; return; }
            _offline.DifferentiateChannel(AnalysisChannel);
            RefreshAnalysisUi();
            Status = $"Derivată pe CH{AnalysisChannel + 1}.";
        });
        OutlierAnalysisCommand = new RelayCommand(() =>
        {
            if (_offline is null) return;
            _offline.RemoveOutliers(AnalysisChannel, 3);
            RefreshAnalysisUi();
            Status = $"Outliers eliminați (mean±3σ) pe CH{AnalysisChannel + 1}.";
        });
        LowPassAnalysisCommand = new RelayCommand(() =>
        {
            if (_offline is null) return;
            _offline.LowPassChannel(AnalysisChannel, 0.2);
            RefreshAnalysisUi();
            Status = $"Low-pass aplicat pe CH{AnalysisChannel + 1}.";
        });
        ScaleOffsetAnalysisCommand = new RelayCommand(() =>
        {
            if (_offline is null) return;
            if (!double.TryParse(AnalysisScale, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var sc)) sc = 1;
            if (!double.TryParse(AnalysisOffset, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var of)) of = 0;
            _offline.ScaleOffsetChannel(AnalysisChannel, sc, of);
            RefreshAnalysisUi();
            Status = $"Scale/Offset ({sc}, {of}) pe CH{AnalysisChannel + 1}.";
        });
        FitXyAnalysisCommand = new RelayCommand(RunXyFit);
        FitPoly2AnalysisCommand = new RelayCommand(RunPoly2Fit);
        SaveFormulaCommand = new RelayCommand(async () => await SaveFormulaAsync());
        ApplyFormulaFromLibraryCommand = new RelayCommand(ApplySelectedFormula);
        ReloadFormulasCommand = new RelayCommand(async () => await LoadFormulasAsync());
        JobNextCommand = new RelayCommand(() => JobStep = Math.Min(6, JobStep + 1));
        JobPrevCommand = new RelayCommand(() => JobStep = Math.Max(0, JobStep - 1));
        JobRunRecordCommand = new RelayCommand(async () => await RunJobRecordAsync());
        JobExportReportCommand = new RelayCommand(async () => await ExportHtmlReportAsync());
        JobApplyRateFilterCommand = new RelayCommand(async () => await ApplyJobRateFilterAsync());
        JobSyncChecklistCommand = new RelayCommand(SyncJobChecklistFromState);
        JobApplyVizCommand = new RelayCommand(ApplyJobVisualization);

        _ = LoadFormulasAsync();
        JobStep = 0;
    }

    private async Task LoadFormulasAsync()
    {
        try
        {
            EnsureLocalFormulasSeed();
            _formulaLibrary = await FormulaLibrary.LoadAsync(FormulasPath);
            await UiAsync(() =>
            {
                FormulaEntries.Clear();
                foreach (var f in _formulaLibrary.Formulas)
                    FormulaEntries.Add(f);
                SelectedFormula = FormulaEntries.FirstOrDefault();
                Status = $"Bibliotecă formule: {FormulaEntries.Count} intrări.";
            });
        }
        catch (Exception ex)
        {
            await UiAsync(() => Status = "Formule: " + ex.Message);
        }
    }

    private async Task SaveFormulaAsync()
    {
        var name = string.IsNullOrWhiteSpace(SelectedFormula?.Name) ? "Custom" : SelectedFormula!.Name;
        var expr = SelectedFormula?.Expression ?? "CH1";
        var existing = _formulaLibrary.Formulas.FirstOrDefault(f =>
            f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            existing = new FormulaEntry { Name = name, Expression = expr };
            _formulaLibrary.Formulas.Add(existing);
            FormulaEntries.Add(existing);
        }
        else
        {
            existing.Expression = expr;
            existing.Notes = SelectedFormula?.Notes ?? "";
        }

        await _formulaLibrary.SaveAsync(FormulasPath);
        Status = $"Formulă salvată: {name}";
        _journal.Setup(Status);
    }

    private void ApplySelectedFormula()
    {
        if (SelectedFormula is null)
        {
            Status = "Selectează o formulă din bibliotecă.";
            return;
        }

        MathChannels.Add(new MathChannelRow
        {
            Name = SelectedFormula.Name,
            Operation = nameof(MathOp.Formula),
            Formula = SelectedFormula.Expression,
            Enabled = true,
            Unit = ""
        });
        Status = $"Formulă aplicată ca math channel: {SelectedFormula.Expression}";
    }

    private void RunXyFit()
    {
        if (_offline is null)
        {
            Status = "Încarcă CSV în Analysis.";
            return;
        }

        var xi = Math.Clamp(FitXChannel - 1, 0, _offline.Columns.Count - 1);
        var yi = Math.Clamp(FitYChannel - 1, 0, _offline.Columns.Count - 1);
        var fit = _offline.FitXy(xi, yi);
        FitResultText = fit.ToString();
        Status = FitResultText;
        HelpPanelText = "Fit linear Y(X) — forță–deplasare / calibrare.";

        if (_plotAnalysis is null || fit.Count < 2) return;
        _plotAnalysis.Plot.Clear();
        var xs = _offline.Columns[xi];
        var ys = _offline.Columns[yi];
        var scatter = _plotAnalysis.Plot.Add.Scatter(xs, ys);
        scatter.LegendText = $"{_offline.ChannelNames[yi]} vs {_offline.ChannelNames[xi]}";
        scatter.MarkerSize = 3;
        scatter.LineWidth = 0;
        var line = _plotAnalysis.Plot.Add.Scatter(xs, fit.FittedY);
        line.LegendText = $"fit R²={fit.RSquared:0.####}";
        line.MarkerSize = 0;
        line.LineWidth = 2;
        line.Color = ScottPlot.Colors.Orange;
        _plotAnalysis.Plot.ShowLegend();
        _plotAnalysis.Plot.Axes.AutoScale();
        _plotAnalysis.Refresh();
        RequestAnalysisTab?.Invoke(this, EventArgs.Empty);
    }

    private void RunPoly2Fit()
    {
        if (_offline is null)
        {
            Status = "Încarcă CSV în Analysis.";
            return;
        }

        var xi = Math.Clamp(FitXChannel - 1, 0, _offline.Columns.Count - 1);
        var yi = Math.Clamp(FitYChannel - 1, 0, _offline.Columns.Count - 1);
        var fit = _offline.FitPolyXy(xi, yi, 2);
        FitResultText = fit.ToString();
        Status = FitResultText;
        HelpPanelText = "Fit polinomial grad 2 Y(X) — calibrare / curbă forță–deplasare.";

        if (_plotAnalysis is null || fit.Count < 3) return;
        _plotAnalysis.Plot.Clear();
        var xs = _offline.Columns[xi];
        var ys = _offline.Columns[yi];
        var scatter = _plotAnalysis.Plot.Add.Scatter(xs, ys);
        scatter.LegendText = $"{_offline.ChannelNames[yi]} vs {_offline.ChannelNames[xi]}";
        scatter.MarkerSize = 3;
        scatter.LineWidth = 0;
        var line = _plotAnalysis.Plot.Add.Scatter(xs, fit.FittedY);
        line.LegendText = $"poly2 R²={fit.RSquared:0.####}";
        line.MarkerSize = 0;
        line.LineWidth = 2;
        line.Color = ScottPlot.Colors.DarkGreen;
        _plotAnalysis.Plot.ShowLegend();
        _plotAnalysis.Plot.Axes.AutoScale();
        _plotAnalysis.Refresh();
        RequestAnalysisTab?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshJobChecklist()
    {
        var steps = new[]
        {
            "1. Meta (operator / probă / comentariu)",
            "2. Rată eșantionare + filtre pe canale",
            "3. Senzori pe canale (Apply)",
            "4. Connect + Start măsurare",
            "5. Tare (zero balance)",
            "6. Record (durată / samples / trigger)",
            "7. Vizualizare + raport HTML/PDF"
        };
        var lines = steps.Select((s, i) => (i <= JobStep ? "✓ " : "○ ") + s);
        JobChecklist = string.Join(Environment.NewLine, lines);
        HelpPanelText = JobStep switch
        {
            0 => "Job: completează Operator / Sample / Comment.",
            1 => "Setează SampleRateHz + JobFilterHz, apoi «Aplică rată/filtru».",
            2 => "Alege senzori din bibliotecă și Apply pe canalele activate.",
            3 => "Backend + Connect, apoi Start măsurare (F5).",
            4 => "Zero / Tare All (F9).",
            5 => "RecordStopMode + Record (sau «Tare + Record job»).",
            _ => "Alege tip afișaj / template, apoi raport HTML/PDF."
        };
        UpdateJobStatusSummary();
    }

    private void UpdateJobStatusSummary()
    {
        var enabled = Channels.Count(c => c.Enabled);
        var avgFilter = Channels.Where(c => c.Enabled).Select(c => c.FilterHz).DefaultIfEmpty(JobFilterHz).Average();
        JobStatusSummary =
            $"Rată {SampleRateHz} Hz · filtru ~{avgFilter:0.#} Hz · {enabled} canale ON · " +
            $"stop={RecordStopMode} · " +
            (IsConnected ? "conectat" : "deconectat") +
            (IsStreaming ? " · streaming" : "") +
            (IsRecording ? " · REC" : "") +
            $" · afișaj {PlotMode}";
    }

    private void SyncJobChecklistFromState()
    {
        if (!string.IsNullOrWhiteSpace(OperatorName) || !string.IsNullOrWhiteSpace(SampleId))
            JobStep = Math.Max(JobStep, 0);
        if (SampleRateHz > 0)
            JobStep = Math.Max(JobStep, 1);
        if (Channels.Any(c => c.Enabled && !string.IsNullOrWhiteSpace(c.SensorName)))
            JobStep = Math.Max(JobStep, 2);
        if (IsConnected)
            JobStep = Math.Max(JobStep, 3);
        if (IsStreaming)
            JobStep = Math.Max(JobStep, 4);
        if (IsRecording || !string.IsNullOrWhiteSpace(LastRecordingPath))
            JobStep = Math.Max(JobStep, 5);
        if (!string.IsNullOrWhiteSpace(LastRecordingPath))
            JobStep = Math.Max(JobStep, 6);
        UpdateJobStatusSummary();
        Status = "Checklist job sincronizat cu starea curentă.";
    }

    private async Task ApplyJobRateFilterAsync()
    {
        SampleRateHz = Math.Max(1, SampleRateHz);
        foreach (var ch in Channels.Where(c => c.Enabled))
        {
            ch.ChannelSampleRateHz = SampleRateHz;
            ch.FilterHz = JobFilterHz;
        }
        if (_device is not null)
        {
            _device.SampleRateHz = SampleRateHz;
            await _device.ApplyChannelConfigAsync(Channels.Select(ToConfig));
        }
        JobStep = Math.Max(JobStep, 1);
        UpdateJobStatusSummary();
        Status = $"Job: rată {SampleRateHz} Hz, filtru {JobFilterHz:0.#} Hz pe canalele activate.";
        _journal.Setup(Status);
    }

    private void ApplyJobVisualization()
    {
        // Prefer current experiment preset / plot mode — keep live panels coherent for the job.
        if (SelectedExperiment is not null)
            PlotMode = SelectedExperiment.PlotMode;
        MultiPanelMode = PlotMode is DisplayLayouts.Numeric or DisplayLayouts.DualYt;
        JobStep = Math.Max(JobStep, 6);
        UpdateJobStatusSummary();
        Status = $"Job vizualizare: {PlotMode}" + (SelectedExperiment is null ? "" : $" ({SelectedExperiment.Name})");
        HelpPanelText = "Tab Măsurare: grafice live. DataViewer/Analysis după Record.";
    }

    private async Task RunJobRecordAsync()
    {
        if (!IsConnected)
        {
            Status = "Connect întâi.";
            JobStep = 3;
            return;
        }

        await ApplyJobRateFilterAsync();
        if (!IsStreaming)
            await StartAsync();
        await TareAsync(null);
        JobStep = 5;

        switch (RecordStopMode)
        {
            case "Duration":
                if (MaxSeconds is null or <= 0) MaxSeconds = 10;
                MaxSamples = null;
                break;
            case "Samples":
                if (MaxSamples is null or <= 0) MaxSamples = 500;
                MaxSeconds = null;
                break;
            case "Trigger":
                TriggerEnabled = true;
                break;
            default:
                if (MaxSeconds is null or <= 0) MaxSeconds = 10;
                RecordStopMode = "Duration";
                break;
        }

        if (!IsRecording)
            await StartRecordingAsync();
        Status = RecordStopMode switch
        {
            "Samples" => $"Job: înregistrare {MaxSamples} eșantioane…",
            "Trigger" => "Job: înregistrare cu trigger…",
            _ => $"Job: înregistrare {MaxSeconds}s…"
        };
        UpdateJobStatusSummary();
    }
}
