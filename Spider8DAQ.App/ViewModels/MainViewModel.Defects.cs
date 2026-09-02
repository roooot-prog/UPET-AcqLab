using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Spider8DAQ.Core.Defects;

namespace Spider8DAQ.App.ViewModels;

public sealed class DefectCatalogRow
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Category { get; init; } = "";
    public string Summary { get; init; } = "";
    public string Symptoms { get; init; } = "";
    public string LikelyCauses { get; init; } = "";
    public string FixSteps { get; init; } = "";
    public string ExampleCaption { get; init; } = "";
    public string SeverityLabel { get; init; } = "";
}

public sealed class MistakeFindingRow
{
    public string DefectId { get; init; } = "";
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string ChannelHint { get; init; } = "";
    public string SeverityLabel { get; init; } = "";
    public string WhenLocal { get; init; } = "";
}

public partial class MainViewModel
{
    private DispatcherTimer? _defectScanTimer;
    private DateTime _lastDefectStatusUtc = DateTime.MinValue;
    private string _lastDefectStatusKey = "";
    private DefectCatalogRow? _selectedDefect;
    private string _defectScanSummary = "Apăsați «Scanează acum» sau porniți Start — detectarea rulează periodic live.";
    private ScottPlot.WPF.WpfPlot? _plotDefect;
    private bool _showPolarityWarning;
    private string _polarityWarningText = "";
    private bool _polarityWarningDismissed;
    /// <summary>HW index of the channel named in the polarity banner (−1 if unknown).</summary>
    private int _polarityWarnedChannelIndex = -1;
    private string _polarityWarnedChannelHint = "";
    private bool _showDefectInvertButton;

    public ObservableCollection<DefectCatalogRow> DefectCatalogItems { get; } = new();
    public ObservableCollection<MistakeFindingRow> LiveMistakeFindings { get; } = new();

    public ICommand ScanDefectsNowCommand { get; private set; } = null!;
    public ICommand ScanOfflineDefectsCommand { get; private set; } = null!;
    public ICommand ClearMistakeFindingsCommand { get; private set; } = null!;
    public ICommand DismissPolarityWarningCommand { get; private set; } = null!;
    /// <summary>Banner «Inversare CH» — inverts the warned channel (not the UI selection).</summary>
    public ICommand InvertWarnedChannelPolarityCommand { get; private set; } = null!;

    public bool ShowPolarityWarning
    {
        get => _showPolarityWarning && !_polarityWarningDismissed;
        private set
        {
            if (_showPolarityWarning == value) return;
            _showPolarityWarning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowPolarityWarningBanner));
        }
    }

    public bool ShowPolarityWarningBanner => ShowPolarityWarning;

    /// <summary>True when the alert bar is showing polarity (Invert CH available).</summary>
    public bool ShowDefectInvertButton
    {
        get => _showDefectInvertButton;
        private set
        {
            if (_showDefectInvertButton == value) return;
            _showDefectInvertButton = value;
            OnPropertyChanged();
        }
    }

    public string PolarityWarningText
    {
        get => _polarityWarningText;
        private set { _polarityWarningText = value; OnPropertyChanged(); }
    }

    public DefectCatalogRow? SelectedDefect
    {
        get => _selectedDefect;
        set
        {
            _selectedDefect = value;
            OnPropertyChanged();
            RefreshDefectExamplePlot();
        }
    }

    public string DefectScanSummary
    {
        get => _defectScanSummary;
        set { _defectScanSummary = value; OnPropertyChanged(); }
    }

    public bool HasMistakeFindings => LiveMistakeFindings.Count > 0;

    private void WireDefectLibrary()
    {
        foreach (var d in LabDefectCatalog.All)
        {
            DefectCatalogItems.Add(new DefectCatalogRow
            {
                Id = d.Id,
                Title = d.Title,
                Category = d.Category,
                Summary = d.Summary,
                Symptoms = d.Symptoms,
                LikelyCauses = d.LikelyCauses,
                FixSteps = d.FixSteps,
                ExampleCaption = d.ExampleCaption,
                SeverityLabel = d.Severity.ToString()
            });
        }

        SelectedDefect = DefectCatalogItems.FirstOrDefault();
        ScanDefectsNowCommand = new RelayCommand(ScanDefectsNow);
        ScanOfflineDefectsCommand = new RelayCommand(ScanOfflineDefectsNow);
        ClearMistakeFindingsCommand = new RelayCommand(() =>
        {
            LiveMistakeFindings.Clear();
            OnPropertyChanged(nameof(HasMistakeFindings));
            UpdatePolarityWarningBanner([]);
            DefectScanSummary = "Lista de detectări a fost golită.";
        });
        DismissPolarityWarningCommand = new RelayCommand(() =>
        {
            _polarityWarningDismissed = true;
            OnPropertyChanged(nameof(ShowPolarityWarning));
            OnPropertyChanged(nameof(ShowPolarityWarningBanner));
            // Acknowledgement only — never blocks Start / Rec / streaming.
            Status = ShowDefectInvertButton
                ? "OK — continuați măsurarea. Polaritatea rămâne inversă până la «Inversare CH» + Zero " +
                  "(sau Ascunde nu oprește achiziția)."
                : "OK — continuați măsurarea. Verificați cablul / senzorul când puteți (achiziția nu e oprită).";
            _journal.Info(ShowDefectInvertButton
                ? "Polaritate inversă: operator a confirmat continuare fără corectare."
                : "Semnal plat: operator a confirmat continuare fără corectare.");
        });
        InvertWarnedChannelPolarityCommand = new RelayCommand(InvertWarnedChannelPolarity);

        _defectScanTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(2.5)
        };
        _defectScanTimer.Tick += (_, _) =>
        {
            if (IsStreaming && !DisplayFrozen)
                RunLiveDefectScan(publishStatus: true);
        };
        _defectScanTimer.Start();
    }

    public void AttachDefectPlot(ScottPlot.WPF.WpfPlot plot)
    {
        _plotDefect = plot;
        RefreshDefectExamplePlot();
    }

    private void RefreshDefectExamplePlot()
    {
        if (_plotDefect is null) return;
        try
        {
            _plotDefect.Plot.Clear();
            var id = SelectedDefect?.Id ?? "zero-uitat";
            var y = LabDefectCatalog.BuildExampleSignal(id, 120);
            var xs = Enumerable.Range(0, y.Length).Select(i => i / 50.0).ToArray();
            var sig = _plotDefect.Plot.Add.Scatter(xs, y);
            sig.LegendText = SelectedDefect?.Title ?? id;
            sig.LineWidth = 2;
            _plotDefect.Plot.Axes.AutoScale();
            _plotDefect.Plot.Title(SelectedDefect?.ExampleCaption ?? "Exemplu semnal");
            _plotDefect.Plot.XLabel("t [s]");
            _plotDefect.Plot.YLabel("valoare (exemplu)");
            _plotDefect.Refresh();
        }
        catch
        {
            /* ignore plot errors */
        }
    }

    private void ScanDefectsNow()
    {
        if (_offline is { Timestamps.Count: > 0 })
            ScanOfflineDefectsNow();
        else
            RunLiveDefectScan(publishStatus: true);
    }

    private void ScanOfflineDefectsNow()
    {
        if (_offline is null || _offline.Timestamps.Count == 0)
        {
            DefectScanSummary = "Nu există sesiune Analiză — Load CSV / Open .upet sau folosiți scan live.";
            return;
        }

        var meta = BuildChannelSnapshots(includeRecent: false);
        var findings = TypicalMistakeDetector.AnalyzeOffline(
            _offline, meta, DateTime.UtcNow - _lastZeroUtc);
        ApplyFindings(findings, source: "Analiză CSV/.upet");
    }

    private void RunLiveDefectScan(bool publishStatus)
    {
        var snaps = BuildChannelSnapshots(includeRecent: true);
        if (snaps.Count == 0)
        {
            DefectScanSummary = "Niciun canal On — activați canale pentru detectare.";
            return;
        }

        var findings = TypicalMistakeDetector.AnalyzeLive(
            snaps,
            DateTime.UtcNow - _lastZeroUtc,
            timeSinceStart: _streamingStartedUtc is DateTime t
                ? DateTime.UtcNow - t
                : null);
        ApplyFindings(findings, source: IsStreaming ? "Live" : "Snapshot", publishStatus);
    }

    private List<ChannelSnapshot> BuildChannelSnapshots(bool includeRecent)
    {
        var snapVals = _engine.Metrology.SnapshotLastPhysical();
        var list = new List<ChannelSnapshot>();
        foreach (var ch in Channels)
        {
            var v = ch.Index < snapVals.Count ? snapVals[ch.Index] : double.NaN;
            double[]? recent = null;
            if (includeRecent && _liveYValueByChannel.TryGetValue(ch.Index, out var q) && q.Count > 0)
                recent = q.ToArray();
            list.Add(new ChannelSnapshot
            {
                Index = ch.Index,
                Name = ch.Name,
                Unit = ch.Unit,
                Enabled = ch.Enabled,
                RecordEnabled = ch.RecordEnabled,
                Scale = ch.Scale,
                Capacity = ch.Capacity,
                Value = v,
                RecentSamples = recent
            });
        }

        return list;
    }

    private void ApplyFindings(
        IReadOnlyList<TypicalMistakeFinding> findings,
        string source,
        bool publishStatus = true)
    {
        LiveMistakeFindings.Clear();
        foreach (var f in findings)
        {
            LiveMistakeFindings.Add(new MistakeFindingRow
            {
                DefectId = f.DefectId,
                Title = f.Title,
                Message = f.Message,
                ChannelHint = f.ChannelHint,
                SeverityLabel = f.Severity.ToString(),
                WhenLocal = f.DetectedLocal.ToString("HH:mm:ss")
            });
        }

        OnPropertyChanged(nameof(HasMistakeFindings));
        UpdatePolarityWarningBanner(findings);
        DefectScanSummary = findings.Count == 0
            ? $"{source}: nicio greșeală tipică detectată acum."
            : $"{source}: {findings.Count} semnal(e) — vedeți lista (mesaje clare, nu alarme prag).";

        if (findings.Count == 0 || !publishStatus) return;

        // Prefer polarity, then flat-signal, then first finding (visible lab mistakes).
        var top = findings.FirstOrDefault(f => f.DefectId == "polaritate-inversa")
                  ?? findings.FirstOrDefault(f => f.DefectId is "semnal-plat" or "cablu-desprins")
                  ?? findings[0];
        var key = top.DefectId + "|" + top.ChannelHint + "|" + top.Message;
        if (key == _lastDefectStatusKey && (DateTime.UtcNow - _lastDefectStatusUtc).TotalSeconds < 20)
            return;
        _lastDefectStatusKey = key;
        _lastDefectStatusUtc = DateTime.UtcNow;

        MetrologyDiagText = top.Message;
        try { RefreshLabAdvisorFromStatus(top.Message); } catch { /* ignore */ }
        try { RefreshLabAdvisorFromState(force: false); } catch { /* ignore */ }
        if (!IsRecording)
            Status = "⚠ " + top.Message;
    }

    private void UpdatePolarityWarningBanner(IReadOnlyList<TypicalMistakeFinding> findings)
    {
        var pol = findings.FirstOrDefault(f => f.DefectId == "polaritate-inversa");
        if (pol is not null)
        {
            _polarityWarnedChannelIndex = pol.ChannelIndex;
            _polarityWarnedChannelHint = pol.ChannelHint ?? "";
            var text =
                $"POLARITATE INVERSATĂ — {pol.ChannelHint}: semnal predominant negativ. " +
                "Apăsați «Inversare CH» (Scale × −1), apoi Zero pe liber.";
            if (!string.Equals(PolarityWarningText, text, StringComparison.Ordinal))
                _polarityWarningDismissed = false; // new detection → show again
            PolarityWarningText = text;
            ShowDefectInvertButton = true;
            ShowPolarityWarning = true;
            return;
        }

        var flat = findings.FirstOrDefault(f => f.DefectId is "semnal-plat" or "cablu-desprins");
        if (flat is not null)
        {
            _polarityWarnedChannelIndex = -1;
            _polarityWarnedChannelHint = "";
            var text = string.IsNullOrWhiteSpace(flat.Message)
                ? $"Semnal plat pe {flat.ChannelHint} — verificați cablul / senzorul."
                : flat.Message;
            if (!string.Equals(PolarityWarningText, text, StringComparison.Ordinal))
                _polarityWarningDismissed = false;
            PolarityWarningText = text;
            ShowDefectInvertButton = false;
            ShowPolarityWarning = true;
            return;
        }

        _polarityWarnedChannelIndex = -1;
        _polarityWarnedChannelHint = "";
        ShowDefectInvertButton = false;
        ShowPolarityWarning = false;
        PolarityWarningText = "";
    }

    private ChannelRow? ResolvePolarityWarnedChannel()
    {
        if (_polarityWarnedChannelIndex >= 0)
        {
            var byIdx = Channels.FirstOrDefault(c => c.Index == _polarityWarnedChannelIndex);
            if (byIdx is not null) return byIdx;
        }

        if (!string.IsNullOrWhiteSpace(_polarityWarnedChannelHint))
        {
            var hint = _polarityWarnedChannelHint.Trim();
            var byName = Channels.FirstOrDefault(c =>
                string.Equals(c.Name, hint, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;

            // ChannelHint may be "CH2" / "CH2 [N]" while Name differs slightly
            byName = Channels.FirstOrDefault(c =>
                c.Name.StartsWith(hint, StringComparison.OrdinalIgnoreCase)
                || hint.StartsWith(c.Name, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
        }

        return null;
    }

    private void InvertWarnedChannelPolarity()
    {
        var ch = ResolvePolarityWarnedChannel();
        if (ch is null)
        {
            Status = "Canalul din avertismentul de polaritate nu a putut fi rezolvat.";
            return;
        }

        if (!TryInvertChannelPolarity(ch, out var detail))
        {
            Status = detail;
            return;
        }

        // Banner fixed — dismiss so operator is not prompted again until a new finding.
        _polarityWarningDismissed = true;
        ShowPolarityWarning = false;
        OnPropertyChanged(nameof(ShowPolarityWarningBanner));
        Status =
            $"Polaritate inversată pe {ch.Name} (Scale={ch.Scale:G6}). " +
            "Faceți Zero CH fără sarcină, apoi verificați semnul la încărcare.";
        _journal.Setup(Status);
        HelpPanelText =
            "După Inversare CH din banner: lăsați senzorul pe liber → Zero CH, " +
            "apoi reîncărcați ușor — forța trebuie să fie pozitivă la compresie.";
    }
}
