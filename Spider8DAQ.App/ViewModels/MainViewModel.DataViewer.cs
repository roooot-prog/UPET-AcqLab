using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Spider8DAQ.App.Export;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.DataViewer;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    private readonly PlaybackCursor _playback = new();
    private DispatcherTimer? _playbackTimer;
    private OfflineSession? _viewerSession;
    private ScottPlot.WPF.WpfPlot? _plotDataViewer;
    private ScottPlot.Plottables.Crosshair? _playbackCrosshair;
    private string _recordingSearch = "";
    private string _viewerMeta = "Selectați o înregistrare.";
    private string _playbackStatus = "Fără date";
    private int _playbackIndex;
    private int _playbackLength;
    private double _playbackSpeed = 1;
    private RecordingEntry? _selectedRecordingEntry;

    public ObservableCollection<RecordingEntry> RecordingEntries { get; } = new();

    public ICommand ViewerPlayCommand { get; private set; } = null!;
    public ICommand ViewerPauseCommand { get; private set; } = null!;
    public ICommand ViewerStopCommand { get; private set; } = null!;
    public ICommand ViewerLoadCommand { get; private set; } = null!;
    public ICommand ViewerExportCsvCommand { get; private set; } = null!;
    public ICommand ViewerExportExcelCommand { get; private set; } = null!;
    public ICommand ViewerOpenFolderCommand { get; private set; } = null!;
    public ICommand ViewerDeleteCommand { get; private set; } = null!;

    /// <summary>%LocalAppData%\UPETAcqLab\recordings — shown in DataViewer / status.</summary>
    public string RecordingsFolderPath => GetWritableRecordingsDirectory();

    public string RecordingSearch
    {
        get => _recordingSearch;
        set
        {
            _recordingSearch = value;
            OnPropertyChanged();
            RefreshRecordings();
        }
    }

    public string ViewerMeta { get => _viewerMeta; set { _viewerMeta = value; OnPropertyChanged(); } }
    public string PlaybackStatus { get => _playbackStatus; set { _playbackStatus = value; OnPropertyChanged(); } }

    public int PlaybackIndex
    {
        get => _playbackIndex;
        set
        {
            _playbackIndex = value;
            _playback.Seek(value);
            OnPropertyChanged();
            UpdatePlaybackStatus();
            RefreshDataViewerPlot(drawFull: false);
        }
    }

    public int PlaybackLength { get => _playbackLength; set { _playbackLength = value; OnPropertyChanged(); } }

    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            _playbackSpeed = Math.Clamp(value, 0.25, 20);
            _playback.Speed = _playbackSpeed;
            OnPropertyChanged();
            UpdatePlaybackStatus();
        }
    }

    public RecordingEntry? SelectedRecordingEntry
    {
        get => _selectedRecordingEntry;
        set
        {
            if (ReferenceEquals(_selectedRecordingEntry, value)) return;
            _selectedRecordingEntry = value;
            OnPropertyChanged();
            if (value is not null)
            {
                SelectedRecordingA = value.FilePath;
                LoadViewerSession(value.FilePath);
            }
        }
    }

    private void WireDataViewerCommands()
    {
        ViewerPlayCommand = new RelayCommand(ViewerPlay);
        ViewerPauseCommand = new RelayCommand(ViewerPause);
        ViewerStopCommand = new RelayCommand(ViewerStop);
        ViewerLoadCommand = new RelayCommand(() =>
        {
            var path = SelectedRecordingEntry?.FilePath ?? SelectedRecordingA;
            if (path is not null) LoadViewerSession(path);
        });
        ViewerExportCsvCommand = new RelayCommand(() => ExportViewer("csv"));
        ViewerExportExcelCommand = new RelayCommand(async () => await ExportViewerAsync("xlsx"));
        ViewerOpenFolderCommand = new RelayCommand(OpenRecordingsFolder);
        ViewerDeleteCommand = new RelayCommand(DeleteSelectedRecording);

        _playbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _playbackTimer.Tick += (_, _) =>
        {
            if (!_playback.Tick())
                _playbackTimer.Stop();
            _playbackIndex = _playback.Index;
            OnPropertyChanged(nameof(PlaybackIndex));
            UpdatePlaybackStatus();
            RefreshDataViewerPlot(drawFull: false);
        };
    }

    public void AttachDataViewerPlot(ScottPlot.WPF.WpfPlot plot) => _plotDataViewer = plot;

    private string RecordingsFolder => GetWritableRecordingsDirectory();

    private void RefreshRecordings()
    {
        var keep = SelectedRecordingEntry?.FilePath ?? SelectedRecordingA;
        RecordingFiles.Clear();
        RecordingEntries.Clear();
        Directory.CreateDirectory(RecordingsFolder);
        foreach (var e in RecordingIndex.Scan(RecordingsFolder, RecordingSearch))
        {
            RecordingEntries.Add(e);
            RecordingFiles.Add(e.FilePath);
        }

        var match = RecordingEntries.FirstOrDefault(x =>
            string.Equals(x.FilePath, keep, StringComparison.OrdinalIgnoreCase));
        _selectedRecordingEntry = match ?? RecordingEntries.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedRecordingEntry));

        SelectedRecordingA = _selectedRecordingEntry?.FilePath ?? RecordingFiles.FirstOrDefault();
        if (SelectedRecordingB is null || !RecordingFiles.Contains(SelectedRecordingB))
            SelectedRecordingB = RecordingFiles.FirstOrDefault(f =>
                !string.Equals(f, SelectedRecordingA, StringComparison.OrdinalIgnoreCase));

        if (_selectedRecordingEntry is not null && _viewerSession?.SourcePath != _selectedRecordingEntry.FilePath)
            LoadViewerSession(_selectedRecordingEntry.FilePath);

        Status = $"DataViewer: {RecordingEntries.Count} CSV în {RecordingsFolder}";
        OnPropertyChanged(nameof(RecordingsFolderPath));
    }

    private void LoadViewerSession(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ViewerMeta = "Fișier lipsă.";
            return;
        }

        try
        {
            ViewerStop();
            _viewerSession = RecordingIndex.Load(path);
            _offline = _viewerSession;
            if (UpetReportFile.HasReportExtension(path) || UpetReportFile.LooksLikeUpetReport(path))
            {
                Annotations.Clear();
                ApplyAttachedMetaFromOffline(_viewerSession);
                ApplyFingerprintVerification(path);
            }
            _playback.Load(_viewerSession.Timestamps.Count);
            PlaybackLength = Math.Max(0, _viewerSession.Timestamps.Count - 1);
            _playbackIndex = 0;
            OnPropertyChanged(nameof(PlaybackIndex));
            ViewerMeta =
                $"{Path.GetFileName(path)} · {_viewerSession.Timestamps.Count} eșantioane · " +
                $"{_viewerSession.ChannelNames.Count} canale · " +
                string.Join(", ", _viewerSession.ChannelNames.Take(8));
            RefreshDataViewerPlot(drawFull: true);
            UpdatePlaybackStatus();
            Status = $"DataViewer încărcat: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            ViewerMeta = "Eroare: " + ex.Message;
            Status = ViewerMeta;
        }
    }

    private void ViewerPlay()
    {
        if (_viewerSession is null)
        {
            Status = "Încărcați o înregistrare în DataViewer.";
            return;
        }
        _playback.Speed = PlaybackSpeed;
        _playback.Play();
        _playbackTimer?.Start();
        UpdatePlaybackStatus();
    }

    private void ViewerPause()
    {
        _playback.Pause();
        _playbackTimer?.Stop();
        UpdatePlaybackStatus();
    }

    private void ViewerStop()
    {
        _playback.Stop();
        _playbackTimer?.Stop();
        _playbackIndex = 0;
        OnPropertyChanged(nameof(PlaybackIndex));
        UpdatePlaybackStatus();
        RefreshDataViewerPlot(drawFull: false);
    }

    private void UpdatePlaybackStatus() => PlaybackStatus = _playback.StatusText;

    private void RefreshDataViewerPlot(bool drawFull)
    {
        if (_plotDataViewer is null || _viewerSession is null) return;
        var session = _viewerSession;
        if (drawFull || _plotDataViewer.Plot.GetPlottables().Count() == 0)
        {
            _plotDataViewer.Plot.Clear();
            _playbackCrosshair = null;
            var colors = new[]
            {
                ScottPlot.Colors.SteelBlue, ScottPlot.Colors.Orange, ScottPlot.Colors.SeaGreen,
                ScottPlot.Colors.Crimson, ScottPlot.Colors.Purple, ScottPlot.Colors.Teal
            };
            for (var c = 0; c < session.Columns.Count; c++)
            {
                var xs = Enumerable.Range(0, session.Columns[c].Length).Select(i => (double)i).ToArray();
                var sig = _plotDataViewer.Plot.Add.Scatter(xs, session.Columns[c]);
                sig.LegendText = session.ChannelNames[c];
                sig.Color = colors[c % colors.Length];
                sig.MarkerSize = 0;
            }
            _plotDataViewer.Plot.Title("DataViewer — redare / review");
            _plotDataViewer.Plot.ShowLegend();
            _plotDataViewer.Plot.Axes.AutoScale();
        }

        if (_playbackCrosshair is not null)
            _plotDataViewer.Plot.Remove(_playbackCrosshair);
        _playbackCrosshair = _plotDataViewer.Plot.Add.Crosshair(_playback.Index, 0);
        _playbackCrosshair.LineColor = ScottPlot.Colors.Red;
        _playbackCrosshair.LineWidth = 1.5f;
        _plotDataViewer.Refresh();
    }

    /// <summary>Compare A/B on DataViewer plot + Analysis (with stats summary).</summary>
    private void CompareRecordingsMulti()
    {
        if (SelectedRecordingA is null || SelectedRecordingB is null ||
            !File.Exists(SelectedRecordingA) || !File.Exists(SelectedRecordingB))
        {
            Status = "Selectați două fișiere CSV/.upet (A și B).";
            return;
        }

        try
        {
            var a = RecordingIndex.Load(SelectedRecordingA);
            var b = RecordingIndex.Load(SelectedRecordingB);
            var chA = Math.Clamp(CompareChannelA - 1, 0, Math.Max(0, a.Columns.Count - 1));
            var chB = Math.Clamp(CompareChannelB - 1, 0, Math.Max(0, b.Columns.Count - 1));
            if (a.Columns.Count == 0 || b.Columns.Count == 0)
            {
                Status = "Unul din fișiere nu are canale.";
                return;
            }

            void Draw(ScottPlot.WPF.WpfPlot? plot, bool setTitle)
            {
                if (plot is null) return;
                plot.Plot.Clear();
                var xsA = Enumerable.Range(0, a.Columns[chA].Length).Select(i => (double)i).ToArray();
                var xsB = Enumerable.Range(0, b.Columns[chB].Length).Select(i => (double)i).ToArray();
                var s1 = plot.Plot.Add.Scatter(xsA, a.Columns[chA]);
                s1.LegendText = $"A:{Path.GetFileName(SelectedRecordingA)} [{a.ChannelNames[chA]}]";
                s1.MarkerSize = 0;
                s1.Color = ScottPlot.Colors.SteelBlue;
                var s2 = plot.Plot.Add.Scatter(xsB, b.Columns[chB]);
                s2.LegendText = $"B:{Path.GetFileName(SelectedRecordingB)} [{b.ChannelNames[chB]}]";
                s2.MarkerSize = 0;
                s2.Color = ScottPlot.Colors.Orange;
                if (setTitle) plot.Plot.Title("Comparare înregistrări");
                plot.Plot.Axes.Bottom.Label.Text = "index eșantion";
                plot.Plot.ShowLegend();
                plot.Plot.Axes.AutoScale();
                plot.Refresh();
            }

            Draw(_plotDataViewer, true);
            Draw(_plotAnalysis, true);

            var stA = a.Stats[chA];
            var stB = b.Stats[chB];
            CompareSummary =
                $"A mean={stA.Mean:0.####} σ={stA.StdDev:0.####} P2P={stA.PeakToPeak:0.####} (N={stA.Count})  |  " +
                $"B mean={stB.Mean:0.####} σ={stB.StdDev:0.####} P2P={stB.PeakToPeak:0.####} (N={stB.Count})  |  " +
                $"Δmean={stA.Mean - stB.Mean:0.####}";
            ViewerMeta =
                $"Compare A[{CompareChannelA}] vs B[{CompareChannelB}] — " +
                $"{Path.GetFileName(SelectedRecordingA)} / {Path.GetFileName(SelectedRecordingB)}";
            Status = $"Comparat canale A#{CompareChannelA} vs B#{CompareChannelB}.";
        }
        catch (Exception ex)
        {
            Status = "Compare eșuat: " + ex.Message;
        }
    }

    private void ExportViewer(string kind) => _ = ExportViewerAsync(kind);

    private async Task ExportViewerAsync(string kind)
    {
        var session = _viewerSession ?? _offline;
        if (session is null)
        {
            Status = "Nicio sesiune DataViewer încărcată.";
            return;
        }

        if (kind == "csv")
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = Path.GetFileNameWithoutExtension(session.SourcePath) + "_export.csv",
                InitialDirectory = GetWritableRecordingsDirectory()
            };
            if (dlg.ShowDialog() != true) return;
            session.SaveCsv(dlg.FileName);
            var png = SessionPlotRenderer.TrySaveSiblingPng(session, dlg.FileName);
            Status = png is null
                ? $"Export CSV: {dlg.FileName}"
                : $"Export CSV: {dlg.FileName} + {Path.GetFileName(png)}";
            return;
        }

        var xdlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = Path.GetFileNameWithoutExtension(session.SourcePath) + ".xlsx",
            InitialDirectory = GetWritableRecordingsDirectory()
        };
        if (xdlg.ShowDialog() != true) return;
        if (!TryBeginExport()) return;

        var dest = xdlg.FileName;
        var meta = CurrentProjectMeta();
        Status = "Export Excel în curs… interfața rămâne activă.";
        try
        {
            var result = await Task.Run(() =>
            {
                CylinderContourExport.MergeFromSession(meta, session);
                return ExcelReportExporter.Export(
                    dest,
                    session,
                    meta,
                    csvPath: Core.Export.MeasurementFingerprint.ResolveExistingCsvPath(session.SourcePath));
            }).ConfigureAwait(true);
            Status = FormatExcelExportStatus(result);
        }
        catch (Exception ex)
        {
            NoteAdvisorExportFailed("Export eșuat.");
            Status = "Export eșuat: " + AppPaths.FriendlyIoMessage(ex, dest);
        }
        finally
        {
            EndExport();
        }
    }

    private void OpenRecordingsFolder()
    {
        try
        {
            var folder = RecordingsFolder;
            Directory.CreateDirectory(folder);
            var select = ResolveLatestCsvPath();
            var args = !string.IsNullOrWhiteSpace(select) && File.Exists(select)
                ? $"/select,\"{select}\""
                : $"\"{folder}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = args,
                UseShellExecute = true
            });
            Status = $"Folder CSV: {folder}";
        }
        catch (Exception ex)
        {
            Status = "Nu pot deschide folderul CSV: " + ex.Message;
        }
    }

    /// <summary>Save-As copy of last / selected recording so the user can download CSV anywhere.</summary>
    private void DownloadLastCsv()
    {
        try
        {
            var src = ResolveLatestCsvPath();
            if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
            {
                Status =
                    "Niciun CSV de descărcat. Faceți Record (după Start), apoi: Export → Descarcă CSV… " +
                    $"sau deschideți folderul: {RecordingsFolder}";
                OpenRecordingsFolder();
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = Path.GetFileName(src),
                Title = "Descarcă CSV — alege unde salvezi o copie"
            };
            if (dlg.ShowDialog() != true) return;
            File.Copy(src, dlg.FileName, overwrite: true);
            LastRecordingPath = src;
            Status = $"CSV descărcat: {dlg.FileName}  (sursă: {src})";
            _journal.Info(Status);
        }
        catch (Exception ex)
        {
            Status = "Descărcare CSV eșuată: " + AppPaths.FriendlyIoMessage(ex, RecordingsFolder);
        }
    }

    private string? ResolveLatestCsvPath()
    {
        if (!string.IsNullOrWhiteSpace(LastRecordingPath) && File.Exists(LastRecordingPath)
            && !LastRecordingPath.EndsWith("_stats.csv", StringComparison.OrdinalIgnoreCase))
            return LastRecordingPath;

        var selected = SelectedRecordingEntry?.FilePath ?? SelectedRecordingA;
        if (!string.IsNullOrWhiteSpace(selected) && File.Exists(selected)
            && !selected.EndsWith("_stats.csv", StringComparison.OrdinalIgnoreCase))
            return selected;

        Directory.CreateDirectory(RecordingsFolder);
        return Directory.GetFiles(RecordingsFolder, "*.csv")
            .Where(p => !p.EndsWith("_stats.csv", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private void DeleteSelectedRecording()
    {
        var path = SelectedRecordingEntry?.FilePath ?? SelectedRecordingA;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Status = "Selectați un fișier de șters.";
            return;
        }

        var ask = MessageBox.Show(
            $"Ștergeți înregistrarea?\n{Path.GetFileName(path)}",
            "UPET AcqLab",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (ask != MessageBoxResult.Yes) return;
        try
        {
            ViewerStop();
            File.Delete(path);
            if (string.Equals(_viewerSession?.SourcePath, path, StringComparison.OrdinalIgnoreCase))
                _viewerSession = null;
            RefreshRecordings();
            Status = "Înregistrare ștearsă.";
        }
        catch (Exception ex)
        {
            Status = "Ștergere eșuată: " + ex.Message;
        }
    }
}
