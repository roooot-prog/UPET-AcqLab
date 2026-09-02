using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Input;
using ClosedXML.Excel;
using Microsoft.Win32;
using Spider8DAQ.App.Export;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Metrology;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Time;

namespace Spider8DAQ.App.ViewModels;

public sealed class MetrologyStepRow
{
    public int Step { get; init; }
    public string Label { get; init; } = "";
    public string MassKg { get; init; } = "";
    public string RefN { get; init; } = "";
    public string Force { get; init; } = "";
    public string Strain { get; init; } = "";
    public string DeltaPct { get; init; } = "";
    public string Note { get; init; } = "";
}

/// <summary>Measurement accuracy: filter, sync, step readings, Δ%, linearity, hysteresis, diagnostics.</summary>
public partial class MainViewModel
{
    private string _metrologyFilterKind = nameof(DigitalFilterKind.Bessel);
    private double _stepMassKg;
    private double _stepReferenceForceN;
    private string _stepLabel = "";
    private int _metrologyForceChannel = 1;
    private int _metrologyStrainChannel = 2;
    private string _stepAggregateKind = nameof(StepAggregateKind.Median);
    private double _stepWindowSeconds = MetrologyConstants.DefaultStepWindowSeconds;
    private string _metrologyStatus = "Metrologie: filtru software pe live/Record; Citire treaptă = mediană/medie pe fereastră.";
    private string _linearityText = "Liniaritate ε(F): —";
    private string _hysteresisText = "Histereză Zero: —";
    private string _syncNoteText = "";
    private bool _highLoadAck;
    private string _metrologyDiagText = "";
    private DateTime _lastZeroUtc = DateTime.MinValue;

    public ObservableCollection<MetrologyStepRow> MetrologySteps { get; } = new();
    public ObservableCollection<string> MetrologyFilterKinds { get; } = new(
        Enum.GetNames(typeof(DigitalFilterKind)));
    public ObservableCollection<string> StepAggregateKinds { get; } = new(
        Enum.GetNames(typeof(StepAggregateKind)));

    public ICommand CaptureStepReadingCommand { get; private set; } = null!;
    public ICommand ClearMetrologyStepsCommand { get; private set; } = null!;
    public ICommand ComputeLinearityCommand { get; private set; } = null!;
    public ICommand MarkHysteresisZeroCommand { get; private set; } = null!;
    public ICommand MeasureHysteresisResidualCommand { get; private set; } = null!;
    public ICommand ExportMetrologyExcelCommand { get; private set; } = null!;
    public ICommand ApplyDefaultSoftwareFiltersCommand { get; private set; } = null!;
    public ICommand AcknowledgeHighLoadCommand { get; private set; } = null!;

    public string MetrologyFilterKind
    {
        get => _metrologyFilterKind;
        set
        {
            _metrologyFilterKind = value;
            OnPropertyChanged();
            ApplyMetrologyFilterConfig();
            try { RefreshStatusLineCompact(); } catch { /* ignore */ }
        }
    }

    public double StepMassKg
    {
        get => _stepMassKg;
        set { _stepMassKg = Math.Max(0, value); OnPropertyChanged(); RefreshStepRefPreview(); }
    }

    public double StepReferenceForceN
    {
        get => _stepReferenceForceN;
        set { _stepReferenceForceN = value; OnPropertyChanged(); RefreshStepRefPreview(); }
    }

    public string StepLabel
    {
        get => _stepLabel;
        set { _stepLabel = value; OnPropertyChanged(); }
    }

    public int MetrologyForceChannel
    {
        get => _metrologyForceChannel;
        set { _metrologyForceChannel = value; OnPropertyChanged(); RefreshMetrologyStepGrid(); }
    }

    public int MetrologyStrainChannel
    {
        get => _metrologyStrainChannel;
        set { _metrologyStrainChannel = value; OnPropertyChanged(); RefreshMetrologyStepGrid(); }
    }

    public string StepAggregateKindName
    {
        get => _stepAggregateKind;
        set
        {
            _stepAggregateKind = value;
            OnPropertyChanged();
            if (Enum.TryParse<StepAggregateKind>(value, out var k))
                _engine.Metrology.AggregateKind = k;
        }
    }

    public double StepWindowSeconds
    {
        get => _stepWindowSeconds;
        set
        {
            _stepWindowSeconds = Math.Clamp(value, 0.2, 5.0);
            OnPropertyChanged();
            _engine.Metrology.StepWindowSeconds = _stepWindowSeconds;
        }
    }

    public string MetrologyStatus
    {
        get => _metrologyStatus;
        set { _metrologyStatus = value; OnPropertyChanged(); }
    }

    public string LinearityText
    {
        get => _linearityText;
        set { _linearityText = value; OnPropertyChanged(); }
    }

    public string HysteresisText
    {
        get => _hysteresisText;
        set { _hysteresisText = value; OnPropertyChanged(); }
    }

    public string SyncNoteText
    {
        get => _syncNoteText;
        set { _syncNoteText = value; OnPropertyChanged(); }
    }

    public bool HighLoadAcknowledged
    {
        get => _highLoadAck;
        set
        {
            _highLoadAck = value;
            _engine.Metrology.HighLoadAcknowledged = value;
            OnPropertyChanged();
        }
    }

    public string MetrologyDiagText
    {
        get => _metrologyDiagText;
        set
        {
            _metrologyDiagText = value;
            OnPropertyChanged();
            try { RefreshStatusLineCompact(); } catch { /* ignore */ }
        }
    }

    public string StepRefPreview
    {
        get
        {
            if (StepReferenceForceN > 0)
                return $"Ref = {StepReferenceForceN:0.###} N";
            if (StepMassKg > 0)
                return $"Ref = m·g = {StepMassKg:0.###} × {MetrologyConstants.StandardGravity} = {MetrologyMath.ForceFromMassKg(StepMassKg):0.###} N";
            return "Ref = — (introduceți masă kg sau forță N)";
        }
    }

    internal void WireMetrologyCommands()
    {
        SyncNoteText = _engine.Metrology.SyncBehaviorNote;
        CaptureStepReadingCommand = new RelayCommand(CaptureStepReading);
        ClearMetrologyStepsCommand = new RelayCommand(() =>
        {
            _engine.Metrology.ClearSteps();
            RefreshMetrologyStepGrid();
            LinearityText = "Liniaritate ε(F): —";
            MetrologyStatus = "Treapte șterse.";
        });
        ComputeLinearityCommand = new RelayCommand(ComputeMetrologyLinearity);
        MarkHysteresisZeroCommand = new RelayCommand(() =>
        {
            var ch = ResolveMetrologyChannelIndex(MetrologyForceChannel);
            _engine.Metrology.MarkHysteresisZero(ch);
            var name = ch >= 0 && ch < Channels.Count ? Channels[ch].Name : $"CH{ch}";
            HysteresisText = $"Histereză Zero: marcat pe {name} înainte de încărcare. După descărcare → «Măsoară residual».";
            MetrologyStatus = HysteresisText;
        });
        MeasureHysteresisResidualCommand = new RelayCommand(() =>
        {
            var ch = ResolveMetrologyChannelIndex(MetrologyForceChannel);
            var name = ch >= 0 && ch < Channels.Count ? Channels[ch].Name : $"CH{ch}";
            var r = _engine.Metrology.MeasureHysteresisResidual(name);
            HysteresisText = r.Message;
            MetrologyStatus = r.Message;
        });
        ExportMetrologyExcelCommand = new RelayCommand(ExportMetrologyExcel);
        ApplyDefaultSoftwareFiltersCommand = new RelayCommand(() =>
        {
            var f = LiveFilterBank.DefaultCutoffHz(SampleRateHz);
            foreach (var ch in Channels.Where(c => c.Enabled))
                ch.FilterHz = f;
            ApplyMetrologyFilterConfig();
            MetrologyStatus = $"Filtru software {_metrologyFilterKind} ≈ {f:0.##} Hz (Rate/10) pe canale ON.";
            Status = MetrologyStatus;
        });
        AcknowledgeHighLoadCommand = new RelayCommand(() =>
        {
            HighLoadAcknowledged = true;
            MetrologyStatus = "Sarcină mare confirmată — Scale suspect suprimat până la Reset.";
        });
    }

    private void RefreshStepRefPreview() => OnPropertyChanged(nameof(StepRefPreview));

    private int ResolveMetrologyChannelIndex(int hwOrOneBased)
    {
        // UI often uses CH# as HW index (0-based) like CalChannel; also accept 1-based if matches Count.
        if (hwOrOneBased >= 0 && hwOrOneBased < Channels.Count) return hwOrOneBased;
        if (hwOrOneBased >= 1 && hwOrOneBased <= Channels.Count) return hwOrOneBased - 1;
        return Math.Clamp(hwOrOneBased, 0, Math.Max(0, Channels.Count - 1));
    }

    internal void ApplyMetrologyFilterConfig(bool resetState = true)
    {
        var kind = Enum.TryParse<DigitalFilterKind>(MetrologyFilterKind, out var k)
            ? k
            : DigitalFilterKind.Bessel;
        _engine.Metrology.AggregateKind = Enum.TryParse<StepAggregateKind>(StepAggregateKindName, out var a)
            ? a
            : StepAggregateKind.Median;
        _engine.Metrology.StepWindowSeconds = StepWindowSeconds;
        var cfgs = Channels.Select(ToConfig).ToList();
        _engine.Metrology.ConfigureFilters(SampleRateHz, cfgs, kind);
        if (resetState)
            _engine.Metrology.ResetFilters();
    }

    private void CaptureStepReading()
    {
        if (!IsStreaming && _engine.Metrology.SnapshotLastPhysical().Count == 0)
        {
            MetrologyStatus = "Citire treaptă: porniți Start (live) și așteptați fereastra (~0.75 s).";
            Status = MetrologyStatus;
            return;
        }

        var names = Channels.Select(c => c.Name).ToList();
        var refN = StepReferenceForceN > 0
            ? StepReferenceForceN
            : (StepMassKg > 0 ? MetrologyMath.ForceFromMassKg(StepMassKg) : 0);
        var step = _engine.Metrology.CaptureStep(names, StepLabel, StepMassKg, refN);
        RefreshMetrologyStepGrid();
        var fi = ResolveMetrologyChannelIndex(MetrologyForceChannel);
        var forceTxt = fi < step.ChannelValues.Length ? step.ChannelValues[fi].ToString("G6", CultureInfo.InvariantCulture) : "—";
        MetrologyStatus =
            $"Treaptă {step.StepIndex}: {step.Label} · F={forceTxt} · {step.AggregateNote}" +
            (refN > 0 ? $" · Ref={refN:0.###} N" : "");
        Status = MetrologyStatus;
        StepLabel = "";
    }

    private void ComputeMetrologyLinearity()
    {
        var fi = ResolveMetrologyChannelIndex(MetrologyForceChannel);
        var si = ResolveMetrologyChannelIndex(MetrologyStrainChannel);
        var report = _engine.Metrology.ComputeLinearity(fi, si);
        LinearityText = report.Summary;
        MetrologyStatus = report.Summary;
        Status = report.Summary;
    }

    private void RefreshMetrologyStepGrid()
    {
        MetrologySteps.Clear();
        var fi = ResolveMetrologyChannelIndex(MetrologyForceChannel);
        var si = ResolveMetrologyChannelIndex(MetrologyStrainChannel);
        var names = Channels.Select(c => c.Name).ToList();
        foreach (var s in _engine.Metrology.Steps)
        {
            var force = fi < s.ChannelValues.Length ? s.ChannelValues[fi] : double.NaN;
            var strain = si < s.ChannelValues.Length ? s.ChannelValues[si] : double.NaN;
            var delta = "";
            if (s.ReferenceForceN > 0 && !double.IsNaN(force))
            {
                var pct = MetrologyMath.DeltaPercent(force, s.ReferenceForceN);
                delta = double.IsNaN(pct) ? "—" : $"{pct:0.###} %";
            }

            MetrologySteps.Add(new MetrologyStepRow
            {
                Step = s.StepIndex,
                Label = s.Label,
                MassKg = s.MassKg > 0 ? s.MassKg.ToString("0.###", CultureInfo.InvariantCulture) : "",
                RefN = s.ReferenceForceN > 0 ? s.ReferenceForceN.ToString("0.###", CultureInfo.InvariantCulture) : "",
                Force = double.IsNaN(force) ? "—" : force.ToString("G6", CultureInfo.InvariantCulture),
                Strain = double.IsNaN(strain) ? "—" : strain.ToString("G6", CultureInfo.InvariantCulture),
                DeltaPct = delta,
                Note = s.AggregateNote
            });
        }
    }

    private void ExportMetrologyExcel()
    {
        if (_engine.Metrology.Steps.Count == 0)
        {
            MetrologyStatus = "Export metrologie: nicio treaptă — folosiți «Citire treaptă».";
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"UPET_Metrologie_{DateTime.Now:yyyyMMdd_HHmm}.xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Metrologie");
        ws.Style.Font.FontName = "Calibri";
        ws.Style.Font.FontSize = 11;
        var r = ExcelReportHeader.Apply(ws, startRow: 1, mergeCols: 10);
        ws.Range(r, 1, r, 10).Merge();
        ws.Cell(r, 1).Value = "UPET AcqLab — Raport erori / trepte / liniaritate";
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 1).Style.Font.FontSize = 14;
        ws.Cell(r, 1).Style.Alignment.WrapText = true;
        r++;
        ws.Cell(r, 1).Value = $"g = {MetrologyConstants.StandardGravity}";
        r++;
        ws.Range(r, 1, r, 10).Merge();
        ws.Cell(r, 1).Value = _engine.Metrology.SyncBehaviorNote;
        ws.Cell(r, 1).Style.Alignment.WrapText = true;
        r++;
        var stepTimes = _engine.Metrology.Steps.Select(s => s.Utc).ToList();
        ws.Cell(r, 1).Value = "Timp experiment";
        ws.Cell(r, 2).Value = SamplingRateInfo.FormatExperimentTime(stepTimes);
        var metaRow = r + 1;
        if (_engine.Metrology.LastLinearity is { } lin)
        {
            ws.Cell(metaRow, 1).Value = lin.Summary;
            metaRow++;
        }
        if (_engine.Metrology.LastHysteresis is { } hyst)
        {
            ws.Cell(metaRow, 1).Value = hyst.Message;
            metaRow++;
        }

        var headerRow = metaRow + 1;
        string[] headers = ["Step", "Label", "MassKg", "RefN", "Force", "Strain", "Delta%", "Note", "Sequence", "PC"];
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(headerRow, c + 1).Value = headers[c];

        var fi = ResolveMetrologyChannelIndex(MetrologyForceChannel);
        var si = ResolveMetrologyChannelIndex(MetrologyStrainChannel);
        var row = headerRow + 1;
        foreach (var s in _engine.Metrology.Steps)
        {
            var force = fi < s.ChannelValues.Length ? s.ChannelValues[fi] : double.NaN;
            var strain = si < s.ChannelValues.Length ? s.ChannelValues[si] : double.NaN;
            var pct = s.ReferenceForceN > 0 ? MetrologyMath.DeltaPercent(force, s.ReferenceForceN) : double.NaN;
            ws.Cell(row, 1).Value = s.StepIndex;
            ws.Cell(row, 2).Value = s.Label;
            ws.Cell(row, 3).Value = s.MassKg;
            ws.Cell(row, 4).Value = s.ReferenceForceN;
            ws.Cell(row, 5).Value = force;
            ws.Cell(row, 6).Value = strain;
            if (!double.IsNaN(pct)) ws.Cell(row, 7).Value = pct;
            ws.Cell(row, 8).Value = s.AggregateNote;
            ws.Cell(row, 9).Value = s.Sequence;
            ws.Cell(row, 10).Value = AppClock.FormatIso(s.Utc);
            row++;
        }

        var used = ws.RangeUsed();
        if (used is not null)
        {
            used.Style.Alignment.WrapText = true;
            ws.Columns().AdjustToContents(1, Math.Min(row, 80), 8, 72);
            ExcelTextFit.Apply(ws, lastColumn: 10, lastRow: row, sizeColumns: false);
        }

        wb.SaveAs(dlg.FileName);
        MetrologyStatus = $"Export metrologie: {dlg.FileName}";
        Status = MetrologyStatus;
    }

    /// <summary>Append metrology notes into report CalibrationNotes.</summary>
    internal string BuildMetrologyReportNotes()
    {
        var parts = new List<string>();
        parts.Add($"Filtru SW {_metrologyFilterKind} (Cutoff=FilterHz; live+Record filtrate)");
        if (_engine.Metrology.Steps.Count > 0)
            parts.Add($"{_engine.Metrology.Steps.Count} trepte ({StepAggregateKindName}, {StepWindowSeconds:0.##}s)");
        if (_engine.Metrology.LastLinearity is { } lin)
            parts.Add(lin.Summary);
        if (_engine.Metrology.LastHysteresis is { } hyst)
            parts.Add(hyst.Message);
        var suspect = _engine.Metrology.LastScaleSuspectMessage;
        if (!string.IsNullOrWhiteSpace(suspect))
            parts.Add(suspect);
        return string.Join(" · ", parts);
    }

    internal void RunConnectMetrologyChecks()
    {
        var cfgs = Channels.Select(ToConfig).ToList();
        double? ExpectedExc(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var s = _sensorLibrary.Sensors.FirstOrDefault(x =>
                string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.Code, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(x.Name, id, StringComparison.OrdinalIgnoreCase));
            return s?.ExcitationV;
        }

        var lines = ConnectDiagnostics.BuildConnectStatusLines(cfgs, ExpectedExc);
        var halfTips = ConnectDiagnostics.CheckHalfBridgeThermal(cfgs);
        MetrologyDiagText = lines.Count == 0
            ? "Connect OK — Exc V / Half-bridge: fără avertismente."
            : string.Join(Environment.NewLine, lines.Take(6));

        if (lines.Count > 0)
        {
            var tip = lines[0];
            Status = tip.Length > 160 ? tip[..160] + "…" : tip;
            try { RefreshLabAdvisorFromStatus(tip); } catch { /* ignore */ }
        }

        foreach (var t in halfTips.Take(2))
        {
            try { RefreshLabAdvisorFromStatus(t.Message); } catch { /* ignore */ }
        }
    }

    internal void OnMetrologyScaleSuspect(ScaleSuspectAlert alert)
    {
        MetrologyDiagText = alert.Message;
        // Idle after recent Zero → stronger wording already in pipeline when idleAfterZero.
        if ((DateTime.UtcNow - _lastZeroUtc).TotalSeconds < 30)
        {
            var idle = _engine.Metrology.CheckScaleSuspect(
                Channels.Select(ToConfig).ToList(), idleAfterZero: true);
            if (idle is not null)
                MetrologyDiagText = idle.Message;
        }

        try { RefreshLabAdvisorFromStatus(MetrologyDiagText); } catch { /* ignore */ }
        // Soft status — do not clobber every live tick
        if (!IsRecording)
            Status = MetrologyDiagText;
    }

    internal void NotifyMetrologyZero()
    {
        _lastZeroUtc = DateTime.UtcNow;
        // Re-settle filters after tare so LPF doesn't drag old offset.
        var snap = _engine.Metrology.SnapshotLastPhysical();
        if (snap.Count > 0)
            _engine.Metrology.SettleFilters(snap);
        HighLoadAcknowledged = false;
    }
}
