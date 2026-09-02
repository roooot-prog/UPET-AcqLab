using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Calibration;
using Spider8DAQ.Core.Export;

namespace Spider8DAQ.App.ViewModels;

/// <summary>UPET AcqLab 3.0 feature pack: Matest, DB15 guide, wizard, undo, plot annotations, threshold stop.</summary>
public partial class MainViewModel
{
    private readonly Stack<ChannelEditSnapshot> _channelEditUndo = new();
    private MatestSeries? _matestSeries;
    private string _matestCompareSummary = "";
    private double _recordThresholdValue = 1000;
    private int _recordThresholdChannel;
    private bool _plotShowZeroLine = true;
    private bool _plotShowThresholdLine;
    private bool _plotShowLastMarkLine = true;
    private double _lastMarkValue = double.NaN;
    private DateTime? _streamingStartedUtc;
    private bool _flatSampleWarned;
    private long _framesAtStart;

    public ICommand ImportMatestCommand { get; private set; } = null!;
    public ICommand ShowDb15WiringGuideCommand { get; private set; } = null!;
    public ICommand FirstMeasureWizardCommand { get; private set; } = null!;
    public ICommand UndoChannelEditCommand { get; private set; } = null!;
    public ICommand OpenJournalFolderCommand { get; private set; } = null!;

    public string MatestCompareSummary
    {
        get => _matestCompareSummary;
        set { _matestCompareSummary = value; OnPropertyChanged(); }
    }

    public double RecordThresholdValue
    {
        get => _recordThresholdValue;
        set { _recordThresholdValue = value; OnPropertyChanged(); }
    }

    public int RecordThresholdChannel
    {
        get => _recordThresholdChannel;
        set { _recordThresholdChannel = Math.Max(0, value); OnPropertyChanged(); }
    }

    public bool PlotShowZeroLine
    {
        get => _plotShowZeroLine;
        set { _plotShowZeroLine = value; OnPropertyChanged(); RebuildPlotSeries(); }
    }

    public bool PlotShowThresholdLine
    {
        get => _plotShowThresholdLine;
        set { _plotShowThresholdLine = value; OnPropertyChanged(); RebuildPlotSeries(); }
    }

    public bool PlotShowLastMarkLine
    {
        get => _plotShowLastMarkLine;
        set { _plotShowLastMarkLine = value; OnPropertyChanged(); RebuildPlotSeries(); }
    }

    public double LastMarkValue
    {
        get => _lastMarkValue;
        set { _lastMarkValue = value; OnPropertyChanged(); }
    }

    internal void WireV3FeatureCommands()
    {
        ImportMatestCommand = new RelayCommand(ImportMatestTxt);
        ShowDb15WiringGuideCommand = new RelayCommand(ShowDb15WiringGuide);
        FirstMeasureWizardCommand = new RelayCommand(RunFirstMeasureWizard);
        UndoChannelEditCommand = new RelayCommand(UndoChannelEdit, () => _channelEditUndo.Count > 0);
        OpenJournalFolderCommand = new RelayCommand(OpenJournalAndRecordings);
    }

    private void ImportMatestTxt()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import Matest TXT",
            Filter = "Matest TXT|*.txt|All|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            _matestSeries = MatestTxtParser.Parse(dlg.FileName);
            if (_matestSeries.Points.Count == 0)
            {
                Status = "Matest: niciun punct [GRAPH] în fișier.";
                MatestCompareSummary = Status;
                return;
            }

            string summary;
            if (_offline is not null && _offline.Columns.Count > 0)
            {
                var ch = Math.Clamp(AnalysisChannel, 0, _offline.Columns.Count - 1);
                var unit = "";
                var name = _offline.ChannelNames[ch];
                var br = name.LastIndexOf('[');
                if (br >= 0 && name.EndsWith("]", StringComparison.Ordinal))
                    unit = name[(br + 1)..^1];
                else if (Channels.Count > ch)
                    unit = Channels[ch].Unit;
                var upetKn = MatestTxtParser.ToForceKn(_offline.Columns[ch], unit);
                var cmp = MatestTxtParser.ComparePeaks(_matestSeries.PeakForceKn, upetKn);
                summary = cmp.Summary + $" · fișier={Path.GetFileName(dlg.FileName)} ({_matestSeries.Points.Count} pct)";
            }
            else
            {
                summary =
                    $"Matest: peak={_matestSeries.PeakForceKn:0.####} kN @ {_matestSeries.PeakTimeSec:0.##}s " +
                    $"({_matestSeries.Points.Count} pct). Încarcă Analysis CSV UPET pentru Δ%.";
            }

            MatestCompareSummary = summary;
            Status = summary;
            HelpPanelText = "Import Matest: comparare peak kN vs UPET (cel mai apropiat eșantion de forță).";
            _journal.Info(summary);
        }
        catch (Exception ex)
        {
            Status = "Import Matest eșuat: " + ex.Message;
            MatestCompareSummary = Status;
        }
    }

    private void ShowDb15WiringGuide()
    {
        var text = Db15WiringGuide.FullText();
        WiringText = text;
        HelpPanelText = Db15WiringGuide.Title;
        MessageBox.Show(text, Db15WiringGuide.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        Status = "Ghid cablare DB15 afișat.";
    }

    private void RunFirstMeasureWizard()
    {
        var steps = new[]
        {
            "1/5 Connect — alegeți backend (HBM USB / Simulator) și apăsați Connect.",
            "2/5 Apply senzor — U2B (forță N) sau P15 (bar) pe canalul activ.",
            "3/5 Zero — Start (F5) apoi Zero (F9) fără sarcină.",
            "4/5 Start — confirmați Live OK pe canalele ON.",
            "5/5 Record — F7 (opțional: Oprire=Threshold)."
        };
        foreach (var step in steps)
        {
            var r = MessageBox.Show(
                step + Environment.NewLine + Environment.NewLine + "OK = următorul pas · Cancel = ieșire",
                "Asistent prima măsurătoare",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);
            if (r != MessageBoxResult.OK) { Status = "Asistent anulat."; return; }
        }
        Status = "Asistent finalizat — Connect → Apply → Zero → Start → Record.";
        HelpPanelText = Status;
    }

    private void OpenJournalAndRecordings()
    {
        OpenRecordingsFolder();
        try
        {
            var last = ResolveLatestCsvPath();
            if (!string.IsNullOrWhiteSpace(last) && File.Exists(last))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = last,
                    UseShellExecute = true
                });
                Status = "Jurnal/înregistrări: folder + ultimul CSV deschis — " + last;
            }
        }
        catch (Exception ex)
        {
            Status = "Folder OK; deschidere CSV: " + ex.Message;
        }
    }

    private sealed class ChannelEditSnapshot
    {
        public int ChannelIndex { get; init; }
        public double Scale { get; init; }
        public double Tare { get; init; }
        public double Offset { get; init; }
    }

    private void PushChannelEditUndo(ChannelRow ch)
    {
        _channelEditUndo.Push(new ChannelEditSnapshot
        {
            ChannelIndex = ch.Index,
            Scale = ch.Scale,
            Tare = ch.TareValue,
            Offset = ch.Offset
        });
        if (_channelEditUndo.Count > 40)
        {
            var keep = _channelEditUndo.Take(40).Reverse().ToList();
            _channelEditUndo.Clear();
            foreach (var x in keep) _channelEditUndo.Push(x);
        }
        CommandManager.InvalidateRequerySuggested();
    }

    private void UndoChannelEdit()
    {
        if (_channelEditUndo.Count == 0) { Status = "Undo: stivă goală."; return; }
        var snap = _channelEditUndo.Pop();
        var ch = Channels.FirstOrDefault(c => c.Index == snap.ChannelIndex);
        if (ch is null) { Status = "Undo: canal lipsă."; return; }
        ch.Scale = snap.Scale;
        ch.TareValue = snap.Tare;
        ch.Offset = snap.Offset;
        if (_device is not null && IsConnected)
            _ = PushChannelConfigAsync(reapplyAcquisition: false);
        Status = $"Undo: {ch.Name} Scale={ch.Scale:G6} Tare={ch.TareValue:G6} Off={ch.Offset:G6}";
        CommandManager.InvalidateRequerySuggested();
    }

    private void ApplyLivePlotAnnotations(ScottPlot.Plot plot)
    {
        if (PlotShowZeroLine)
        {
            var z = plot.Add.HorizontalLine(0);
            z.Color = ScottPlot.Colors.Gray;
            z.LineWidth = 1;
            z.LinePattern = ScottPlot.LinePattern.Dashed;
            z.LegendText = "Zero";
        }
        if (PlotShowThresholdLine && Math.Abs(RecordThresholdValue) > 0)
        {
            var t = plot.Add.HorizontalLine(RecordThresholdValue);
            t.Color = ScottPlot.Colors.OrangeRed;
            t.LineWidth = 1.2f;
            t.LinePattern = ScottPlot.LinePattern.Dotted;
            t.LegendText = "Prag";
            if (RecordThresholdValue > 0)
            {
                var tn = plot.Add.HorizontalLine(-RecordThresholdValue);
                tn.Color = ScottPlot.Colors.OrangeRed;
                tn.LineWidth = 1f;
                tn.LinePattern = ScottPlot.LinePattern.Dotted;
            }
        }
        if (PlotShowLastMarkLine && !double.IsNaN(LastMarkValue) && !double.IsInfinity(LastMarkValue))
        {
            var m = plot.Add.HorizontalLine(LastMarkValue);
            m.Color = ScottPlot.Colors.Purple;
            m.LineWidth = 1.2f;
            m.LegendText = "MARK";
        }
    }

    private IReadOnlyList<PeakAtMarkRow> ComputePeakAtMarksForSession(OfflineSession session, string? csvPath)
    {
        try
        {
            var path = csvPath ?? session.SourcePath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return Array.Empty<PeakAtMarkRow>();
            var marks = PeakAtMark.ParseMarksFromCsvLines(CsvSharedIO.EnumerateLines(path));
            return PeakAtMark.Compute(session, marks);
        }
        catch { return Array.Empty<PeakAtMarkRow>(); }
    }

    private void ScheduleFlatSampleWatch()
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            await _dispatcher.InvokeAsync(() =>
            {
                if (!IsStreaming || _flatSampleWarned) return;
                var frames = _engine.FramesReceived - _framesAtStart;
                if (frames < 2)
                {
                    _flatSampleWarned = true;
                    Status = "Avertisment: fără eșantioane live după 2s — verificați OMB/USB/Start.";
                    HelpPanelText = Status;
                    _journal.Warn(Status);
                }
            });
        });
    }

    /// <summary>Push undo snapshot before Scale grid edit (call from UI BeginningEdit if needed).</summary>
    public void PushSelectedChannelUndo()
    {
        var ch = ResolveSelectedHwChannel();
        if (ch is not null) PushChannelEditUndo(ch);
    }
}
