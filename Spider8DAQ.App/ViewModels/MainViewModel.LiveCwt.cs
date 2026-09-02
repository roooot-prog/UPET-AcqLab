using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Spider8DAQ.Core.Acquisition;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    /// <summary>Sliding window length (time bins). Newest samples pushed every DAQ tick.</summary>
    private const int LiveCwtBufferSamples = 512;
    /// <summary>Target UI refresh ~20 Hz; adaptive skip if previous frame still computing.</summary>
    private const int LiveCwtMinRefreshMs = 50;
    /// <summary>Minimum samples before first scalogram (do not wait for full buffer).</summary>
    private const int LiveCwtMinSamples = 32;

    private readonly Dictionary<int, double[]> _cwtRing = new();
    private readonly Dictionary<int, int> _cwtRingWrite = new();
    private readonly Dictionary<int, int> _cwtRingCount = new();
    private readonly Dictionary<string, ScottPlot.WPF.WpfPlot> _cwtPlots = new(StringComparer.Ordinal);
    private readonly Stopwatch _cwtRefreshWatch = Stopwatch.StartNew();
    private long _cwtLastRefreshMs;
    private int _cwtComputeBusy; // 0 idle, 1 computing (Interlocked)

    public ObservableCollection<LiveCwtPanelViewModel> LiveCwtPanels { get; } = new();

    public bool ShowLiveCwt => PlotMode == DisplayLayouts.Cwt;

    public ICommand ShowGraficMorletCommand { get; private set; } = null!;

    internal void WireLiveCwt()
    {
        ShowGraficMorletCommand = new RelayCommand(ShowGraficMorlet);
    }

    /// <summary>Ribbon shortcut: switch Grafic live to CWT (Morlet) and focus panels.</summary>
    private void ShowGraficMorlet()
    {
        PlotMode = DisplayLayouts.Cwt;
        ShowLivePlotCard = true;
        RequestFocusLivePlotCard?.Invoke(this, EventArgs.Empty);
        Status = "Grafic Morlet (CWT) — scalogramă live HQ, refresh ~15–20 Hz (adaptiv).";
        HelpPanelText = DisplayLayouts.Describe(DisplayLayouts.Cwt);
    }

    internal void AttachLiveCwtPlot(LiveCwtPanelViewModel panel, ScottPlot.WPF.WpfPlot plot)
    {
        _cwtPlots[panel.Id] = plot;
        plot.Plot.Clear();
        plot.Plot.Title(panel.Title);
        plot.Plot.Axes.Bottom.Label.Text = "Timp [s]";
        plot.Plot.Axes.Left.Label.Text = "Frecvență [Hz]";
        plot.Refresh();
    }

    internal void DetachLiveCwtPlot(LiveCwtPanelViewModel panel)
    {
        _cwtPlots.Remove(panel.Id);
    }

    private void SyncLiveCwtPanels()
    {
        var enabled = Channels.Where(c => c.Enabled).OrderBy(c => c.Index).ToList();
        var wanted = enabled.Select(c => c.Index).ToHashSet();

        for (var i = LiveCwtPanels.Count - 1; i >= 0; i--)
        {
            if (!wanted.Contains(LiveCwtPanels[i].ChannelIndex))
            {
                var id = LiveCwtPanels[i].Id;
                _cwtPlots.Remove(id);
                LiveCwtPanels.RemoveAt(i);
            }
        }

        foreach (var ch in enabled)
        {
            var existing = LiveCwtPanels.FirstOrDefault(p => p.ChannelIndex == ch.Index);
            if (existing is null)
            {
                var panel = new LiveCwtPanelViewModel($"cwt-{ch.Index}-{Guid.NewGuid():N}"[..18])
                {
                    ChannelIndex = ch.Index,
                    ChannelName = ch.Name,
                    Unit = ch.Unit,
                    Subtitle = $"Morlet · live HQ · fereastră {LiveCwtBufferSamples} eș."
                };
                LiveCwtPanels.Add(panel);
                EnsureCwtRing(ch.Index);
            }
            else
            {
                existing.ChannelName = ch.Name;
                existing.Unit = ch.Unit;
            }
        }

        foreach (var key in _cwtRing.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            _cwtRing.Remove(key);
            _cwtRingWrite.Remove(key);
            _cwtRingCount.Remove(key);
        }

        OnPropertyChanged(nameof(ShowLiveCwt));
    }

    private void EnsureCwtRing(int channelIndex)
    {
        if (_cwtRing.ContainsKey(channelIndex)) return;
        _cwtRing[channelIndex] = new double[LiveCwtBufferSamples];
        _cwtRingWrite[channelIndex] = 0;
        _cwtRingCount[channelIndex] = 0;
    }

    private void PushCwtSample(int channelIndex, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        EnsureCwtRing(channelIndex);
        var buf = _cwtRing[channelIndex];
        var w = _cwtRingWrite[channelIndex];
        buf[w] = value;
        _cwtRingWrite[channelIndex] = (w + 1) % buf.Length;
        if (_cwtRingCount[channelIndex] < buf.Length)
            _cwtRingCount[channelIndex]++;
    }

    private double[] SnapshotCwtRing(int channelIndex)
    {
        if (!_cwtRing.TryGetValue(channelIndex, out var buf)) return [];
        var count = _cwtRingCount[channelIndex];
        if (count < LiveCwtMinSamples) return [];
        var dst = new double[count];
        var w = _cwtRingWrite[channelIndex];
        var start = (w - count + buf.Length) % buf.Length;
        for (var i = 0; i < count; i++)
            dst[i] = buf[(start + i) % buf.Length];
        return dst;
    }

    private void UpdateLiveCwt(ProcessedSample sample)
    {
        if (PlotMode != DisplayLayouts.Cwt) return;

        foreach (var ch in Channels.Where(c => c.Enabled))
        {
            if (ch.Index < 0 || ch.Index >= sample.Physical.Length) continue;
            PushCwtSample(ch.Index, sample.Physical[ch.Index]);
        }

        var now = _cwtRefreshWatch.ElapsedMilliseconds;
        if (now - _cwtLastRefreshMs < LiveCwtMinRefreshMs) return;
        if (LiveCwtPanels.Count == 0 || _cwtPlots.Count == 0) return;
        // Skip frame if previous CWT still running — adaptive rate under load
        if (Interlocked.CompareExchange(ref _cwtComputeBusy, 1, 0) != 0) return;
        _cwtLastRefreshMs = now;

        var fs = Math.Max(1, SampleRateHz);
        var opts = CwtOptions.ForLive();
        var jobs = new List<(LiveCwtPanelViewModel Panel, double[] Snap, ScottPlot.WPF.WpfPlot Plot)>();
        foreach (var panel in LiveCwtPanels)
        {
            if (!_cwtPlots.TryGetValue(panel.Id, out var plot)) continue;
            var snap = SnapshotCwtRing(panel.ChannelIndex);
            if (snap.Length < LiveCwtMinSamples) continue;
            jobs.Add((panel, snap, plot));
        }

        if (jobs.Count == 0)
        {
            Interlocked.Exchange(ref _cwtComputeBusy, 0);
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                var results = new (LiveCwtPanelViewModel Panel, CwtResult Cwt, ScottPlot.WPF.WpfPlot Plot)[jobs.Count];
                Parallel.For(0, jobs.Count, i =>
                {
                    var (panel, snap, plot) = jobs[i];
                    var cwt = ContinuousWaveletTransform.Compute(snap, fs, opts, panel.ChannelName);
                    results[i] = (panel, cwt, plot);
                });

                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is null)
                {
                    Interlocked.Exchange(ref _cwtComputeBusy, 0);
                    return;
                }

                _ = dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
                {
                    try
                    {
                        foreach (var (panel, cwt, plot) in results)
                        {
                            if (cwt.AnalyzedSamples < 16) continue;
                            if (!_cwtPlots.ContainsKey(panel.Id)) continue;
                            RenderCwtToPlot(plot, cwt, panel);
                            panel.Subtitle =
                                $"Morlet · {cwt.AnalyzedSamples} eș. · {cwt.FrequenciesHz.Length} scări · " +
                                $"{cwt.FrequenciesHz[0]:0.##}–{cwt.FrequenciesHz[^1]:0.##} Hz · live";
                        }
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _cwtComputeBusy, 0);
                    }
                });
            }
            catch
            {
                Interlocked.Exchange(ref _cwtComputeBusy, 0);
            }
        });
    }

    private static void RenderCwtToPlot(ScottPlot.WPF.WpfPlot plot, CwtResult cwt, LiveCwtPanelViewModel panel)
    {
        plot.Plot.Clear();
        CwtHeatmapRendering.AddHeatmap(
            plot.Plot, cwt,
            CwtHeatmapRendering.LiveRowUpsample,
            CwtHeatmapRendering.LiveColUpsample);
        plot.Plot.Title(panel.Title);
        plot.Plot.Axes.Bottom.Label.Text = "Timp [s]";
        plot.Plot.Axes.Left.Label.Text = "Frecvență [Hz]";
        plot.Refresh();
    }

    /// <summary>Offline CWT on Analysis CursorA–CursorB for AnalysisChannel.</summary>
    private void RunCwtRegion()
    {
        if (_offline is null || _offline.Columns.Count == 0)
        {
            Status = "Încărcați CSV în Analiză înainte de CWT.";
            return;
        }

        var ch = Math.Clamp(AnalysisChannel, 0, _offline.Columns.Count - 1);
        var a = Math.Clamp(Math.Min(CursorA, CursorB), 0, _offline.Columns[ch].Length - 1);
        var b = Math.Clamp(Math.Max(CursorA, CursorB), a, _offline.Columns[ch].Length - 1);
        var slice = _offline.Columns[ch].Skip(a).Take(b - a + 1).ToArray();
        var fs = CwtPlotRenderer.EstimateSampleRate(_offline, SampleRateHz);
        if (_offline.Timestamps.Count > 1 && b > a)
        {
            var span = (_offline.Timestamps[Math.Min(b, _offline.Timestamps.Count - 1)] -
                        _offline.Timestamps[a]).TotalSeconds;
            if (span > 1e-9)
                fs = (b - a) / span;
        }

        var name = ch < _offline.ChannelNames.Count ? _offline.ChannelNames[ch] : $"CH{ch}";
        var cwt = ContinuousWaveletTransform.Compute(slice, fs, CwtOptions.ForExport(), name);

        if (cwt.AnalyzedSamples < 16 || _plotAnalysis is null)
        {
            Status = "CWT: prea puține eșantioane (min ~16).";
            return;
        }

        // Offline: export-grade densify + Smooth for max sharpness
        _plotAnalysis.Plot.Clear();
        CwtHeatmapRendering.AddHeatmap(
            _plotAnalysis.Plot, cwt,
            CwtHeatmapRendering.ExportRowUpsample,
            CwtHeatmapRendering.ExportColUpsample);
        _plotAnalysis.Plot.Title($"CWT — {name}");
        _plotAnalysis.Plot.Axes.Bottom.Label.Text = "Timp [s]";
        _plotAnalysis.Plot.Axes.Left.Label.Text = "Frecvență [Hz]";
        _plotAnalysis.Refresh();

        Status =
            $"CWT regiune [{a}:{b}] · {name} · {cwt.WaveletName} · {cwt.AnalyzedSamples} eș. · " +
            $"{cwt.FrequenciesHz.Length} scări · {cwt.FrequenciesHz[0]:0.##}–{cwt.FrequenciesHz[^1]:0.##} Hz";
        RequestAnalysisTab?.Invoke(this, EventArgs.Empty);
    }
}
