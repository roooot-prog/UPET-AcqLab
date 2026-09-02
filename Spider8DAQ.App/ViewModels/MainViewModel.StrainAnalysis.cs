using System.Globalization;
using System.Windows.Input;
using Spider8DAQ.Core.Analysis;

namespace Spider8DAQ.App.ViewModels;

/// <summary>Poisson ν live/offline + elastic recovery / permanent strain (Analiză).</summary>
public partial class MainViewModel
{
    private int _poissonEpsLChannel = 1;
    private int _poissonEpsTChannel = 2;
    private string _livePoissonText = "ν: —";
    private double _livePoissonNu = double.NaN;
    private string _analysisPoissonText = "ν (Analiză): —";
    private string _elasticRecoveryText = "Recuperare elastică: —";
    private int _unloadStartIndex = -1;
    private int _unloadEndIndex = -1;
    private double _reportPoissonNu = double.NaN;
    private string _reportPoissonSummary = "";
    private string _reportElasticRecoverySummary = "";

    public ICommand ComputePoissonAnalysisCommand { get; private set; } = null!;
    public ICommand SetUnloadStartCommand { get; private set; } = null!;
    public ICommand SetUnloadEndCommand { get; private set; } = null!;
    public ICommand ClearUnloadMarkersCommand { get; private set; } = null!;

    public int PoissonEpsLChannel
    {
        get => _poissonEpsLChannel;
        set
        {
            _poissonEpsLChannel = Math.Max(1, value);
            OnPropertyChanged();
            if (PlotMode == DisplayLayouts.Poisson)
                RebuildPlotSeries();
        }
    }

    public int PoissonEpsTChannel
    {
        get => _poissonEpsTChannel;
        set
        {
            _poissonEpsTChannel = Math.Max(1, value);
            OnPropertyChanged();
            if (PlotMode == DisplayLayouts.Poisson)
                RebuildPlotSeries();
        }
    }

    public string LivePoissonText
    {
        get => _livePoissonText;
        set { _livePoissonText = value ?? "ν: —"; OnPropertyChanged(); }
    }

    public double LivePoissonNu
    {
        get => _livePoissonNu;
        private set { _livePoissonNu = value; OnPropertyChanged(); }
    }

    public string AnalysisPoissonText
    {
        get => _analysisPoissonText;
        set { _analysisPoissonText = value ?? "ν (Analiză): —"; OnPropertyChanged(); }
    }

    public string ElasticRecoveryText
    {
        get => _elasticRecoveryText;
        set { _elasticRecoveryText = value ?? "Recuperare elastică: —"; OnPropertyChanged(); }
    }

    public int UnloadStartIndex
    {
        get => _unloadStartIndex;
        set { _unloadStartIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnloadMarkers)); }
    }

    public int UnloadEndIndex
    {
        get => _unloadEndIndex;
        set { _unloadEndIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasUnloadMarkers)); }
    }

    public bool HasUnloadMarkers => UnloadStartIndex >= 0 && UnloadEndIndex >= 0;

    private void WireStrainAnalysisCommands()
    {
        ComputePoissonAnalysisCommand = new RelayCommand(ComputePoissonFromAnalysis);
        SetUnloadStartCommand = new RelayCommand(() => SetUnloadMarker(isStart: true));
        SetUnloadEndCommand = new RelayCommand(() => SetUnloadMarker(isStart: false));
        ClearUnloadMarkersCommand = new RelayCommand(ClearUnloadMarkers);
    }

    private void ComputePoissonFromAnalysis()
    {
        if (_offline is null)
        {
            Status = "Încarcă CSV în Analiză pentru ν.";
            AnalysisPoissonText = "ν (Analiză): — (fără CSV)";
            return;
        }

        var li = Math.Clamp(PoissonEpsLChannel - 1, 0, _offline.Columns.Count - 1);
        var ti = Math.Clamp(PoissonEpsTChannel - 1, 0, _offline.Columns.Count - 1);
        if (li == ti)
        {
            AnalysisPoissonText = "ν (Analiză): — (ε_l și ε_t trebuie canale diferite)";
            Status = AnalysisPoissonText;
            return;
        }

        var a = Math.Clamp(Math.Min(CursorA, CursorB), 0, _offline.Columns[li].Length);
        var b = Math.Clamp(Math.Max(CursorA, CursorB) + 1, a, _offline.Columns[li].Length);
        if (b - a < 2)
        {
            // fallback: auto early portion of whole recording
            (a, b) = PoissonRatioAnalysis.AutoEarlyWindow(_offline.Columns[li].Length);
            Status = "Fereastră CursorA–B prea scurtă — folosesc porțiunea timpurie automată.";
        }

        var result = PoissonRatioAnalysis.Compute(_offline.Columns[li], _offline.Columns[ti], a, b);
        AnalysisPoissonText = result.ToDisplayString();
        Status = AnalysisPoissonText;
        HelpPanelText =
            "ν aparent = −dε_t/dε_l pe zona liniară (CursorA–CursorB). Selectați ε_l (longitudinal) și ε_t (transversal).";

        if (result.IsValid)
        {
            _reportPoissonNu = result.Nu;
            _reportPoissonSummary = result.ToSummary();
            LivePoissonNu = result.Nu;
        }

        if (_plotAnalysis is null || result.Count < 2) return;

        _plotAnalysis.Plot.Clear();
        var n = Math.Min(_offline.Columns[li].Length, _offline.Columns[ti].Length);
        var xs = new double[n];
        var ys = new double[n];
        Array.Copy(_offline.Columns[li], xs, n);
        Array.Copy(_offline.Columns[ti], ys, n);
        var scatter = _plotAnalysis.Plot.Add.Scatter(xs, ys);
        scatter.LegendText = $"{_offline.ChannelNames[ti]} vs {_offline.ChannelNames[li]}";
        scatter.MarkerSize = 3;
        scatter.LineWidth = 0;

        if (result.IsValid && b > a)
        {
            var fitXs = new double[b - a];
            var fitYs = new double[b - a];
            for (var i = 0; i < fitXs.Length; i++)
            {
                fitXs[i] = xs[a + i];
                fitYs[i] = result.Slope * fitXs[i] + result.Intercept;
            }
            var line = _plotAnalysis.Plot.Add.Scatter(fitXs, fitYs);
            line.LegendText = $"fit ν≈{result.Nu:0.####}";
            line.MarkerSize = 0;
            line.LineWidth = 2.2f;
            line.Color = ScottPlot.Colors.OrangeRed;
        }

        _plotAnalysis.Plot.Title($"Poisson — ε_t vs ε_l · {AnalysisPoissonText}");
        _plotAnalysis.Plot.Axes.Bottom.Label.Text = $"{_offline.ChannelNames[li]} (ε_l)";
        _plotAnalysis.Plot.Axes.Left.Label.Text = $"{_offline.ChannelNames[ti]} (ε_t)";
        _plotAnalysis.Plot.ShowLegend();
        _plotAnalysis.Plot.Axes.AutoScale();
        _plotAnalysis.Refresh();
        RequestAnalysisTab?.Invoke(this, EventArgs.Empty);
    }

    private void SetUnloadMarker(bool isStart)
    {
        if (_offline is null)
        {
            Status = "Încarcă CSV în Analiză pentru marcaje descărcare.";
            return;
        }

        var idx = Math.Clamp(CursorA, 0, Math.Max(0, _offline.Timestamps.Count - 1));
        if (isStart)
        {
            UnloadStartIndex = idx;
            Status = $"Descărcare start @ idx {idx} (CH analiză {AnalysisChannel + 1}).";
        }
        else
        {
            UnloadEndIndex = idx;
            Status = $"Descărcare sfârșit @ idx {idx} (CH analiză {AnalysisChannel + 1}).";
        }

        RecomputeElasticRecovery();
        RefreshAnalysisPlot();
    }

    private void ClearUnloadMarkers()
    {
        UnloadStartIndex = -1;
        UnloadEndIndex = -1;
        _reportElasticRecoverySummary = "";
        ElasticRecoveryText = "Recuperare elastică: —";
        Status = "Marcaje descărcare șterse.";
        if (_offline is not null)
            RefreshAnalysisPlot();
    }

    private void RecomputeElasticRecovery()
    {
        if (_offline is null || UnloadStartIndex < 0 || UnloadEndIndex < 0)
        {
            ElasticRecoveryText = "Recuperare elastică: — (setați Desc. start și Desc. sfârșit)";
            return;
        }

        var ch = Math.Clamp(AnalysisChannel, 0, _offline.Columns.Count - 1);
        var result = ElasticRecoveryAnalysis.Compute(
            _offline.Columns[ch], UnloadStartIndex, UnloadEndIndex);
        ElasticRecoveryText = result.ToDisplayString();
        if (result.IsValid)
        {
            _reportElasticRecoverySummary = result.ToSummary();
            Status = ElasticRecoveryText;
        }
    }

    private void ApplyUnloadMarkersToAnalysisPlot()
    {
        if (_plotAnalysis is null || _offline is null) return;
        var ch = Math.Clamp(AnalysisChannel, 0, Math.Max(0, _offline.Columns.Count - 1));
        if (ch >= _offline.Columns.Count) return;
        var col = _offline.Columns[ch];

        void Mark(int index, string label, ScottPlot.Color color)
        {
            if (index < 0 || index >= col.Length) return;
            var v = col[index];
            if (double.IsNaN(v)) v = 0;
            var line = _plotAnalysis.Plot.Add.VerticalLine(index);
            line.Color = color;
            line.LineWidth = 1.6f;
            line.LegendText = label;
            var txt = _plotAnalysis.Plot.Add.Text(label, index, v);
            txt.LabelFontColor = color;
            txt.LabelFontSize = 11;
        }

        if (UnloadStartIndex >= 0)
            Mark(UnloadStartIndex, "Desc. start", ScottPlot.Color.FromHex("#C62828"));
        if (UnloadEndIndex >= 0)
            Mark(UnloadEndIndex, "Desc. sfârșit", ScottPlot.Color.FromHex("#2E7D32"));
        if (HasUnloadMarkers)
        {
            var r = ElasticRecoveryAnalysis.Compute(col, UnloadStartIndex, UnloadEndIndex);
            if (r.IsValid && r.MaxIndex >= 0 && r.MaxIndex < col.Length)
            {
                var m = _plotAnalysis.Plot.Add.Scatter(
                    new[] { (double)r.MaxIndex }, new[] { r.EpsilonMax });
                m.Color = ScottPlot.Colors.DarkOrange;
                m.MarkerSize = 10;
                m.LineWidth = 0;
                m.LegendText = "ε_max";
            }
        }
    }

    private void PublishStrainIndicatorsToMeta(Spider8DAQ.Core.Projects.ProjectMeta meta)
    {
        if (!double.IsNaN(_reportPoissonNu) && !double.IsInfinity(_reportPoissonNu))
        {
            meta.ApparentPoissonNu = _reportPoissonNu;
            meta.ApparentPoissonSummary = _reportPoissonSummary ?? "";
        }
        else if (!double.IsNaN(LivePoissonNu) && !double.IsInfinity(LivePoissonNu) && PlotMode == DisplayLayouts.Poisson)
        {
            meta.ApparentPoissonNu = LivePoissonNu;
            meta.ApparentPoissonSummary = string.IsNullOrWhiteSpace(_reportPoissonSummary)
                ? LivePoissonText
                : _reportPoissonSummary;
        }

        if (!string.IsNullOrWhiteSpace(_reportElasticRecoverySummary))
            meta.ElasticRecoverySummary = _reportElasticRecoverySummary;
    }

    private void UpdateLivePoissonDisplay(PoissonRatioResult result, double epsL, double epsT)
    {
        if (result.IsValid)
        {
            LivePoissonNu = result.Nu;
            LivePoissonText = result.ToDisplayString();
            _reportPoissonNu = result.Nu;
            _reportPoissonSummary = result.ToSummary();
            CursorText =
                $"Poisson: ε_l={epsL.ToString("0.####", CultureInfo.InvariantCulture)}  " +
                $"ε_t={epsT.ToString("0.####", CultureInfo.InvariantCulture)}  ·  {LivePoissonText}";
        }
        else
        {
            LivePoissonText = result.Count < 2
                ? "ν: — (acumulare puncte…)"
                : "ν: —";
            CursorText =
                $"Poisson: ε_l={epsL.ToString("0.####", CultureInfo.InvariantCulture)}  " +
                $"ε_t={epsT.ToString("0.####", CultureInfo.InvariantCulture)}  n={_xyXs.Count}";
        }
    }
}
