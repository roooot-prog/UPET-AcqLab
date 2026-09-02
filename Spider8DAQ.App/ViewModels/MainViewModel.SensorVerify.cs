using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Spider8DAQ.Core.Acquisition;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    private const int VerifySampleCapacity = 256;
    private const int VerifyWaitMs = 2000;
    private readonly Dictionary<int, Queue<double>> _verifySamples = new();
    private int _verifyRunToken;

    public ICommand VerifySensorCommand { get; private set; } = null!;
    public ObservableCollection<string> SensorVerifyLines { get; } = new();

    private void WireSensorVerifyCommands()
    {
        VerifySensorCommand = new RelayCommand(async () => await RunSensorVerificationAsync());
    }

    private void TrackVerificationSample(ProcessedSample sample)
    {
        foreach (var ch in Channels)
        {
            if (ch.Index < 0 || ch.Index >= sample.Physical.Length) continue;
            var v = sample.Physical[ch.Index];
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            if (!_verifySamples.TryGetValue(ch.Index, out var q))
            {
                q = new Queue<double>(VerifySampleCapacity);
                _verifySamples[ch.Index] = q;
            }
            q.Enqueue(v);
            while (q.Count > VerifySampleCapacity)
                q.Dequeue();
        }
    }

    private void ClearVerificationSamples(int? channelIndex = null)
    {
        if (channelIndex is int idx)
        {
            if (_verifySamples.TryGetValue(idx, out var q))
                q.Clear();
            return;
        }
        foreach (var q in _verifySamples.Values)
            q.Clear();
    }

    private IReadOnlyList<double> GetVerificationSamples(int channelIndex)
        => _verifySamples.TryGetValue(channelIndex, out var q) ? q.ToList() : Array.Empty<double>();

    /// <summary>Preset layout: Y(t) + numeric, focus selected CH on plot, start live if possible.</summary>
    private void ApplySensorVerifySetup()
    {
        PlotMode = DisplayLayouts.Yt;
        MultiPanelMode = true;
        FollowLiveZoom = true;

        var idx = ResolveHwIndexFromUiSelector(SelectedChannelForSensor);
        if (idx is int i && i < Channels.Count)
        {
            var ch = Channels[i];
            ch.Enabled = true;
            ch.ShowOnPlot = true;
            SelectedChannelRow = ch;
            foreach (var other in Channels.Where(c => c.Index != i))
                other.ShowOnPlot = false;
        }

        RebuildPlotSeries();
        AutoscaleLivePlots();
        HelpPanelText =
            "Verificare senzor: canal selectat pe graf + citiri numerice. " +
            "Conectați, Apply senzor, Start (F5), apoi verificați rezultatul.";
    }

    private async Task RunSensorVerificationAsync(bool fromPreset = false)
    {
        var token = Interlocked.Increment(ref _verifyRunToken);
        ApplySensorVerifySetup();

        var idx = ResolveHwIndexFromUiSelector(SelectedChannelForSensor);
        if (idx is not int chIdx || chIdx >= Channels.Count)
        {
            Status = "Verificare: CH invalid — setați CH0–CH7.";
            MessageBox.Show(
                "Canal invalid. Introduceți CH# (0–7, ca în grilă) lângă Apply.",
                "Verificare senzor",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var ch = Channels[chIdx];
        ClearVerificationSamples(chIdx);
        SensorVerifyLines.Clear();
        SensorVerifyLines.Add($"Verific {ch.Name}…");

        if (IsConnected && !IsStreaming)
        {
            try { await StartAsync(); }
            catch (Exception ex) { Status = "Verificare: " + ex.Message; }
        }

        Status = $"Verificare senzor {ch.Name} — colectez {VerifyWaitMs / 1000}s date live…";
        await Task.Delay(VerifyWaitMs);
        if (token != _verifyRunToken) return;

        var samples = GetVerificationSamples(chIdx);
        var result = SensorVerification.Evaluate(ch, samples, IsStreaming, IsConnected);

        SensorVerifyLines.Clear();
        foreach (var line in result.Details.Split('\n'))
            SensorVerifyLines.Add(line.TrimEnd('\r'));

        Status = result.Verdict;
        HelpPanelText = result.Verdict + (result.RestZero
            ? " — la P15/bar, 0 V la repaus este normal; aplicați presiune pentru test."
            : "");

        _journal.Info($"Verificare CH{chIdx + 1}: {result.Verdict}");

        var icon = result.IsActive
            ? MessageBoxImage.Information
            : MessageBoxImage.Warning;
        MessageBox.Show(
            result.Details,
            result.Verdict,
            MessageBoxButton.OK,
            icon);
    }

    private void OnSensorVerifyPresetSelected()
    {
        ApplySensorVerifySetup();
        _ = RunSensorVerificationAsync(fromPreset: true);
    }
}
