using System.IO;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Spider8DAQ.Core.Export;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    private MediaElement? _analysisVideo;
    private DispatcherTimer? _analysisPlayTimer;
    private ScottPlot.Plottables.VerticalLine? _analysisPlayhead;
    private readonly List<ScottPlot.Plottables.Scatter> _analysisRevealSeries = new();
    private bool _analysisPlaying;
    private DateTime _analysisPlayAnchorUtc;
    private double _analysisPlayAnchorSeconds;
    private int _analysisPlayIndex;
    private string _analysisLoadedSourcePath = "";
    private string _analysisVideoPath = "";
    private string _analysisVideoSearchDir = "";
    private string _analysisPlaybackStatus = "Play: curba se scrie odată cu linia de timp (și filmul, dacă există).";
    private string _analysisVideoHint = "Niciun clip. Rec cu «Filmează epruveta» sau Open pachet cu *_video.mp4.";

    public ICommand AnalysisPlayCommand { get; private set; } = null!;
    public ICommand AnalysisPauseCommand { get; private set; } = null!;
    public ICommand AnalysisStopCommand { get; private set; } = null!;

    public bool HasAnalysisVideo =>
        !string.IsNullOrWhiteSpace(_analysisVideoPath) && File.Exists(_analysisVideoPath);

    public string AnalysisPlaybackStatus
    {
        get => _analysisPlaybackStatus;
        set { _analysisPlaybackStatus = value; OnPropertyChanged(); }
    }

    public string AnalysisVideoHint
    {
        get => _analysisVideoHint;
        set { _analysisVideoHint = value; OnPropertyChanged(); }
    }

    public string AnalysisVideoPath
    {
        get => _analysisVideoPath;
        private set
        {
            _analysisVideoPath = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAnalysisVideo));
        }
    }

    private void WireAnalysisVideoCommands()
    {
        AnalysisPlayCommand = new RelayCommand(AnalysisPlay);
        AnalysisPauseCommand = new RelayCommand(AnalysisPause);
        AnalysisStopCommand = new RelayCommand(AnalysisStop);
        _analysisPlayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _analysisPlayTimer.Tick += (_, _) => TickAnalysisPlayback();
    }

    public void AttachAnalysisVideo(MediaElement player)
    {
        _analysisVideo = player;
        _analysisVideo.LoadedBehavior = MediaState.Manual;
        _analysisVideo.UnloadedBehavior = MediaState.Manual;
        _analysisVideo.MediaEnded += (_, _) => FinishAnalysisPlaybackAtEnd();
        _analysisVideo.MediaFailed += (_, e) =>
        {
            AnalysisPlaybackStatus = "Clipul nu s-a putut reda: " + (e.ErrorException?.Message ?? "codec / fișier");
            AnalysisVideoHint = AnalysisPlaybackStatus;
        };
        ApplyAnalysisVideoSource();
    }

    private void BindAnalysisVideoFromSession(string? extraDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(extraDirectory))
            _analysisVideoSearchDir = extraDirectory;

        var found = ExperimentVideoLocator.Find(
            _offline?.SourcePath,
            _offline?.AttachedMeta ?? (ExperimentVideoEnabled ? CurrentProjectMeta() : null),
            ExperimentVideoFiles,
            _analysisVideoSearchDir,
            GetWritableRecordingsDirectory());
        AnalysisVideoPath = found ?? "";
        if (HasAnalysisVideo)
        {
            AnalysisVideoHint = "Clip epruvetă: " + Path.GetFileName(AnalysisVideoPath);
            AnalysisPlaybackStatus = "Gata — Play scrie curba și pornește filmul împreună.";
        }
        else
        {
            AnalysisVideoHint = "Niciun clip lângă această înregistrare. Rec cu film USB sau Open pachet.";
            AnalysisPlaybackStatus = _offline is { Timestamps.Count: > 0 }
                ? "Play scrie curba pe grafic (fără film)."
                : "Încărcați CSV / pachet, apoi Play.";
        }

        ApplyAnalysisVideoSource();
        MoveAnalysisPlayhead(_analysisPlayIndex, refreshPlot: true);
    }

    private void ApplyAnalysisVideoSource()
    {
        if (_analysisVideo is null) return;
        try
        {
            _analysisVideo.Stop();
        }
        catch { /* ignore */ }

        if (!HasAnalysisVideo)
        {
            _analysisVideo.Source = null;
            return;
        }

        try
        {
            _analysisVideo.Source = new Uri(AnalysisVideoPath);
            _analysisVideo.Position = TimeSpan.Zero;
        }
        catch (Exception ex)
        {
            AnalysisPlaybackStatus = "Clip invalid: " + ex.Message;
        }
    }

    private void AnalysisPlay()
    {
        if (_offline is null || _offline.Timestamps.Count == 0)
        {
            Status = "Încărcați CSV / pachet în Analiză, apoi Play.";
            return;
        }

        BindAnalysisVideoFromSession();
        if (_analysisPlayIndex >= Math.Max(0, _offline.Timestamps.Count - 1))
            _analysisPlayIndex = 0;

        _analysisPlayAnchorSeconds = _offline.TimeSecondsAt(_analysisPlayIndex, SampleRateHz > 0 ? SampleRateHz : 50);
        _analysisPlayAnchorUtc = DateTime.UtcNow;
        _analysisPlaying = true;
        if (HasAnalysisVideo && _analysisVideo is not null)
        {
            try
            {
                _analysisVideo.Position = TimeSpan.FromSeconds(Math.Max(0, _analysisPlayAnchorSeconds));
                _analysisVideo.Play();
            }
            catch (Exception ex)
            {
                AnalysisPlaybackStatus = "Play film eșuat: " + ex.Message;
            }
        }

        _analysisPlayTimer?.Start();
        MoveAnalysisPlayhead(_analysisPlayIndex, refreshPlot: true);
        AnalysisPlaybackStatus = HasAnalysisVideo
            ? "▶ Curba + film"
            : "▶ Curba se scrie odată cu linia de timp";
        Status = AnalysisPlaybackStatus;
    }

    private void AnalysisPause()
    {
        _analysisPlaying = false;
        _analysisPlayTimer?.Stop();
        try { _analysisVideo?.Pause(); } catch { /* ignore */ }
        if (_offline is not null)
            _analysisPlayAnchorSeconds = _offline.TimeSecondsAt(_analysisPlayIndex, SampleRateHz > 0 ? SampleRateHz : 50);
        AnalysisPlaybackStatus = HasAnalysisVideo ? "❚❚ Pauză (grafic + film)" : "❚❚ Pauză";
    }

    private void AnalysisStop()
    {
        _analysisPlaying = false;
        _analysisPlayTimer?.Stop();
        _analysisPlayIndex = 0;
        _analysisPlayAnchorSeconds = 0;
        try
        {
            _analysisVideo?.Stop();
            if (_analysisVideo is not null)
                _analysisVideo.Position = TimeSpan.Zero;
        }
        catch { /* ignore */ }
        MoveAnalysisPlayhead(0, refreshPlot: true);
        AnalysisPlaybackStatus = HasAnalysisVideo
            ? "■ Stop — Play reia curba și filmul de la t = 0."
            : "■ Stop — curba revine la început.";
    }

    private void TickAnalysisPlayback()
    {
        if (!_analysisPlaying || _offline is null || _offline.Timestamps.Count == 0)
        {
            AnalysisPause();
            return;
        }

        var hz = SampleRateHz > 0 ? SampleRateHz : 50;
        double t;
        if (HasAnalysisVideo && _analysisVideo is not null)
        {
            t = _analysisVideo.Position.TotalSeconds;
            if (_analysisVideo.NaturalDuration.HasTimeSpan
                && t >= _analysisVideo.NaturalDuration.TimeSpan.TotalSeconds - 0.04)
            {
                FinishAnalysisPlaybackAtEnd();
                return;
            }
        }
        else
        {
            t = _analysisPlayAnchorSeconds + (DateTime.UtcNow - _analysisPlayAnchorUtc).TotalSeconds;
        }

        var idx = _offline.IndexAtTimeSeconds(t, hz);
        MoveAnalysisPlayhead(idx, refreshPlot: true);
        if (idx >= _offline.Timestamps.Count - 1 && t >= _offline.DurationSeconds(hz) - 0.05)
        {
            FinishAnalysisPlaybackAtEnd();
        }
        else
        {
            AnalysisPlaybackStatus = HasAnalysisVideo
                ? $"▶ t = {t:0.00} s · eș. {idx + 1}/{_offline.Timestamps.Count}"
                : $"▶ t = {t:0.00} s";
        }
    }

    private void FinishAnalysisPlaybackAtEnd()
    {
        _analysisPlaying = false;
        _analysisPlayTimer?.Stop();
        try { _analysisVideo?.Pause(); } catch { /* ignore */ }
        if (_offline is not null && _offline.Timestamps.Count > 0)
            MoveAnalysisPlayhead(_offline.Timestamps.Count - 1, refreshPlot: true);
        AnalysisPlaybackStatus = "Sfârșit — curba e completă. Stop revine la t = 0.";
    }

    private void MoveAnalysisPlayhead(int index, bool refreshPlot)
    {
        if (_offline is null) return;
        _analysisPlayIndex = Math.Clamp(index, 0, Math.Max(0, _offline.Timestamps.Count - 1));
        ApplyAnalysisCurveReveal();
        if (_plotAnalysis is null) return;
        if (_analysisPlayhead is null)
        {
            try
            {
                _analysisPlayhead = _plotAnalysis.Plot.Add.VerticalLine(_analysisPlayIndex);
                _analysisPlayhead.Color = ScottPlot.Color.FromHex("#C8102E");
                _analysisPlayhead.LineWidth = 2;
            }
            catch
            {
                _analysisPlayhead = null;
            }
        }
        else
        {
            _analysisPlayhead.X = _analysisPlayIndex;
        }

        if (refreshPlot)
            _plotAnalysis.Refresh();
    }

    /// <summary>
    /// Draws the analysis curve only up to the playhead so the trace writes in time, not as a finished plot.
    /// Axes stay on the full recording so the red line travels left → right.
    /// </summary>
    private void ApplyAnalysisCurveReveal()
    {
        if (_offline is null) return;
        var last = Math.Max(0, _offline.Timestamps.Count - 1);
        var reveal = Math.Clamp(_analysisPlayIndex, 0, last);
        foreach (var s in _analysisRevealSeries)
        {
            try { s.MaxRenderIndex = reveal; }
            catch { /* ScottPlot version / disposed plottable */ }
        }
    }

    private void ResetAnalysisPlayheadForNewSession()
    {
        if (_offline is null) return;
        var key = _offline.SourcePath ?? "";
        if (string.Equals(key, _analysisLoadedSourcePath, StringComparison.OrdinalIgnoreCase))
            return;
        _analysisLoadedSourcePath = key;
        _analysisPlaying = false;
        _analysisPlayTimer?.Stop();
        _analysisPlayIndex = 0;
        _analysisPlayAnchorSeconds = 0;
    }

    private void ResetAnalysisPlayheadAfterPlotClear()
    {
        _analysisPlayhead = null;
        if (_offline is not null)
            MoveAnalysisPlayhead(_analysisPlayIndex, refreshPlot: false);
    }
}
