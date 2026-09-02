using System.Collections.ObjectModel;
using System.Windows.Input;
using Spider8DAQ.App.Controls;
using Spider8DAQ.Core.Acquisition;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    private readonly Dictionary<string, ChannelPlotRuntime> _channelPlotRuntimes = new(StringComparer.Ordinal);

    public ObservableCollection<LiveChannelPlotItemViewModel> ChannelPlots { get; } = new();

    public ICommand OpenSeparatePlotForSelectedCommand { get; private set; } = null!;
    public ICommand AddChannelPlotCommand { get; private set; } = null!;

    internal void WireChannelPlotCommands()
    {
        OpenSeparatePlotForSelectedCommand = new RelayCommand(OpenSeparatePlotForSelected);
        AddChannelPlotCommand = new RelayCommand(AddChannelPlotPicker);
    }

    private void LoadChannelPlotSettings()
    {
        var s = LiveChannelPlotStore.Load();
        ChannelPlots.Clear();
        foreach (var entry in s.Panels)
            ChannelPlots.Add(CreateChannelPlotViewModel(entry));
    }

    private void SaveChannelPlotSettings()
    {
        var settings = new LiveChannelPlotSettings
        {
            Panels = ChannelPlots.Select(p => new LiveChannelPlotEntry
            {
                Id = p.Id,
                ChannelIndex = p.ChannelIndex,
                Left = p.Left,
                Top = p.Top,
                Width = p.Width,
                Height = p.Height,
                FollowLive = p.FollowLive
            }).ToList()
        };
        LiveChannelPlotStore.Save(settings);
    }

    public void OnChannelPlotLayoutChanged(LiveChannelPlotItemViewModel panel) => SaveChannelPlotSettings();

    private LiveChannelPlotItemViewModel CreateChannelPlotViewModel(LiveChannelPlotEntry entry)
    {
        var vm = new LiveChannelPlotItemViewModel(entry.Id)
        {
            ChannelIndex = entry.ChannelIndex,
            Left = entry.Left,
            Top = entry.Top,
            Width = entry.Width,
            Height = entry.Height,
            FollowLive = entry.FollowLive
        };
        RefreshChannelPlotLabels(vm);
        vm.AutoscaleCommand = new RelayCommand(() => AutoscaleChannelPlot(vm));
        vm.RemoveCommand = new RelayCommand(() => RemoveChannelPlot(vm));
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LiveChannelPlotItemViewModel.FollowLive))
                SaveChannelPlotSettings();
        };
        return vm;
    }

    private void RefreshChannelPlotLabels(LiveChannelPlotItemViewModel panel)
    {
        var ch = Channels.FirstOrDefault(c => c.Index == panel.ChannelIndex);
        panel.ChannelName = ch?.Name ?? $"CH{panel.ChannelIndex}";
        panel.Unit = ch?.Unit ?? "";
    }

    private void RefreshAllChannelPlotLabels()
    {
        foreach (var p in ChannelPlots)
            RefreshChannelPlotLabels(p);
    }

    private void OpenSeparatePlotForSelected()
    {
        var ch = SelectedChannelRow
                 ?? (ResolveHwIndexFromUiSelector(SelectedChannelForSensor) is int i
                     ? Channels.ElementAtOrDefault(i)
                     : null);
        if (ch is null)
        {
            Status = "Selectați un canal pentru grafic separat.";
            return;
        }
        OpenSeparatePlotForChannel(ch.Index);
    }

    private void AddChannelPlotPicker()
    {
        var ch = SelectedChannelRow
                 ?? Channels.FirstOrDefault(c => c.Enabled)
                 ?? Channels.FirstOrDefault();
        if (ch is null)
        {
            Status = "Niciun canal disponibil pentru panou grafic.";
            return;
        }
        OpenSeparatePlotForChannel(ch.Index);
    }

    public void OpenSeparatePlotForChannel(int channelIndex)
    {
        var existing = ChannelPlots.FirstOrDefault(p => p.ChannelIndex == channelIndex);
        if (existing is not null)
        {
            Status = $"Grafic separat deschis deja pentru {existing.ChannelName}.";
            return;
        }

        var offset = ChannelPlots.Count * 32;
        var entry = new LiveChannelPlotEntry
        {
            ChannelIndex = channelIndex,
            Left = 620 + offset,
            Top = 480 + offset,
            Width = 420,
            Height = 280
        };
        var vm = CreateChannelPlotViewModel(entry);
        ChannelPlots.Add(vm);
        SaveChannelPlotSettings();
        Status = $"Grafic separat — {vm.Title}. Mutabil/redimensionabil pe spațiul de lucru.";
    }

    private void RemoveChannelPlot(LiveChannelPlotItemViewModel? panel)
    {
        if (panel is null || !ChannelPlots.Contains(panel)) return;
        DetachChannelPlot(panel);
        ChannelPlots.Remove(panel);
        SaveChannelPlotSettings();
        Status = $"Panou grafic închis — {panel.ChannelName}.";
    }

    internal void AttachChannelPlot(LiveChannelPlotItemViewModel panel, ScottPlot.WPF.WpfPlot plot)
    {
        if (!_channelPlotRuntimes.TryGetValue(panel.Id, out var runtime))
        {
            runtime = new ChannelPlotRuntime();
            _channelPlotRuntimes[panel.Id] = runtime;
        }

        runtime.Plot = plot;
        runtime.Panel = panel;
        var needsRebuild = runtime.Logger is null || !ReferenceEquals(runtime.Plot, plot);
        if (needsRebuild)
            RebuildChannelPlotSeries(panel, runtime);
        plot.UserInputProcessor.IsEnabled = true;
    }

    internal void DetachChannelPlot(LiveChannelPlotItemViewModel panel)
    {
        if (!_channelPlotRuntimes.TryGetValue(panel.Id, out var runtime)) return;
        runtime.Plot = null;
        runtime.Logger = null;
        _channelPlotRuntimes.Remove(panel.Id);
    }

    private void RebuildChannelPlotSeries(LiveChannelPlotItemViewModel panel, ChannelPlotRuntime runtime)
    {
        if (runtime.Plot is null) return;
        runtime.Plot.Plot.Clear();
        runtime.YWindow.Clear();
        runtime.LiveYMin = double.NaN;
        runtime.LiveYMax = double.NaN;
        runtime.StrainLike = false;

        RefreshChannelPlotLabels(panel);
        var ch = Channels.FirstOrDefault(c => c.Index == panel.ChannelIndex);
        var unit = ch?.Unit ?? panel.Unit;
        var yLabel = FormatYAxisLabel(unit);
        var xLabel = SampleRateHz > 0 ? $"t [s] @ {SampleRateHz} Hz" : "t [s]";
        ApplyEngineeringPlotStyle(runtime.Plot.Plot, panel.Title, xLabel, yLabel);

        var logger = runtime.Plot.Plot.Add.DataLogger();
        logger.LegendText = $"{panel.ChannelName} [{unit}]";
        logger.Color = Controls.ChannelPalette.GetScottPlotColor(panel.ChannelIndex);
        logger.ManageAxisLimits = false;
        logger.LineStyle.Width = 1.8f;
        logger.Period = SamplePeriodSec;
        runtime.Logger = logger;
        runtime.Plot.Plot.HideLegend();
        runtime.Plot.Refresh();
    }

    private void RebuildAllChannelPlotSeries()
    {
        foreach (var panel in ChannelPlots)
        {
            if (_channelPlotRuntimes.TryGetValue(panel.Id, out var runtime))
                RebuildChannelPlotSeries(panel, runtime);
        }
    }

    private void UpdateChannelPlots(ProcessedSample sample)
    {
        if (ChannelPlots.Count == 0) return;
        var t = TimeFromSample(sample);
        NoteOrResetLoggerTimeline(t);
        var seq = sample.Frame.Sequence;

        foreach (var panel in ChannelPlots)
        {
            if (!_channelPlotRuntimes.TryGetValue(panel.Id, out var runtime) || runtime.Logger is null)
                continue;

            var ch = Channels.FirstOrDefault(c => c.Index == panel.ChannelIndex);
            if (ch is null || !ch.Enabled) continue;
            if (panel.ChannelIndex < 0 || panel.ChannelIndex >= sample.Physical.Length) continue;

            var v = sample.Physical[panel.ChannelIndex];
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;

            AddDataLoggerPoint(runtime.Logger, t, v);
            FeedChannelPlotWindow(runtime, v);

            if (panel.FollowLive && seq % 8 == 0)
                ApplyChannelPlotAxis(runtime, force: true);

            runtime.Plot?.Refresh();
        }
    }

    private static void FeedChannelPlotWindow(ChannelPlotRuntime runtime, double v)
    {
        runtime.YWindow.Enqueue(v);
        runtime.YWindow.Enqueue(v);
        while (runtime.YWindow.Count > LiveYWindowSize * 2)
            runtime.YWindow.Dequeue();
    }

    private void ApplyChannelPlotAxis(ChannelPlotRuntime runtime, bool force)
    {
        if (runtime.Plot is null || runtime.YWindow.Count == 0) return;

        var ch = runtime.Panel is not null
            ? Channels.FirstOrDefault(c => c.Index == runtime.Panel.ChannelIndex)
            : null;
        if (ch is not null && IsStrainUnit(ch.Unit))
            runtime.StrainLike = true;

        UpdateSmoothedYLimits(runtime.YWindow, ref runtime.LiveYMin, ref runtime.LiveYMax, runtime.StrainLike, force);
        if (!double.IsNaN(runtime.LiveYMin) && !double.IsNaN(runtime.LiveYMax))
            runtime.Plot.Plot.Axes.SetLimitsY(runtime.LiveYMin, runtime.LiveYMax);

        if (_liveTimeSec > SamplePeriodSec * 2)
        {
            var x0 = Math.Max(0, _liveTimeSec - LiveXWindowSec);
            var x1 = _liveTimeSec + Math.Max(0.05, LiveXWindowSec / 40);
            runtime.Plot.Plot.Axes.SetLimitsX(x0, x1);
        }
    }

    private void AutoscaleChannelPlot(LiveChannelPlotItemViewModel panel)
    {
        if (!_channelPlotRuntimes.TryGetValue(panel.Id, out var runtime)) return;
        panel.FollowLive = true;
        runtime.YWindow.Clear();
        runtime.LiveYMin = double.NaN;
        runtime.LiveYMax = double.NaN;
        runtime.StrainLike = false;

        if (_latestUiSample is not null)
        {
            var ch = Channels.FirstOrDefault(c => c.Index == panel.ChannelIndex);
            if (ch is not null && ch.Enabled
                && panel.ChannelIndex >= 0
                && panel.ChannelIndex < _latestUiSample.Physical.Length)
            {
                var v = _latestUiSample.Physical[panel.ChannelIndex];
                if (!double.IsNaN(v) && !double.IsInfinity(v))
                    FeedChannelPlotWindow(runtime, v);
            }
        }

        ApplyChannelPlotAxis(runtime, force: true);
        runtime.Plot?.Refresh();
        SaveChannelPlotSettings();
        Status = $"Autoscale — {panel.Title}; urmărire live ON.";
    }

    private void ResetAllChannelPlotScales()
    {
        foreach (var panel in ChannelPlots)
        {
            if (!_channelPlotRuntimes.TryGetValue(panel.Id, out var runtime)) continue;
            runtime.YWindow.Clear();
            runtime.LiveYMin = double.NaN;
            runtime.LiveYMax = double.NaN;
            runtime.StrainLike = false;
            RebuildChannelPlotSeries(panel, runtime);
        }
    }

    private sealed class ChannelPlotRuntime
    {
        public LiveChannelPlotItemViewModel? Panel;
        public ScottPlot.WPF.WpfPlot? Plot;
        public ScottPlot.Plottables.DataLogger? Logger;
        public Queue<double> YWindow { get; } = new();
        public double LiveYMin = double.NaN;
        public double LiveYMax = double.NaN;
        public bool StrainLike;
    }
}
