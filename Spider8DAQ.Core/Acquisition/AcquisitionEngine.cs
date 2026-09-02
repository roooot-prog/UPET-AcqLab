using Spider8DAQ.Core.Alarms;
using Spider8DAQ.Core.Compute;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.MathChannels;
using Spider8DAQ.Core.Metrology;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Acquisition;

public sealed class AcquisitionEngine : IAsyncDisposable
{
    private readonly MathChannelEngine _math = new();
    private readonly OnlineComputeEngine _compute = new();
    private readonly AdvancedTrigger _advTrigger = new();
    private readonly AlarmMonitor _alarms = new();
    private CsvRecordingWriter? _writer;
    private StatisticsJournal? _statsJournal;
    private readonly Queue<(SampleFrame frame, double[] combined)> _preBuffer = new();
    private long _recordedSamples;
    private long _framesReceived;
    private long _framesDroppedEstimate;
    private DateTime _lastFrameUtc = DateTime.UtcNow;
    private bool _recordingArmed;
    private bool _isRecording;
    private bool _triggerLatched;
    private DateTime? _postTriggerUntil;
    private int[] _recordIndices = Array.Empty<int>();
    private bool _peakMode;
    private double _peakIntervalSec = 1.0;
    private DateTime _peakWindowStart = DateTime.UtcNow;
    private double[]? _peakMin;
    private double[]? _peakMax;
    private int _peakCount;

    public ISpider8Device? Device { get; private set; }
    public List<MathChannelDefinition> MathChannels { get; } = new();
    public List<ComputeDefinition> ComputeChannels { get; } = new();
    public AdvancedTriggerSettings AdvancedTrigger
    {
        get => _advTrigger.Settings;
        set => _advTrigger.Settings = value;
    }
    public AlarmMonitor Alarms => _alarms;
    public RecordingSettings Recording { get; set; } = new();
    public bool IsRecording => _isRecording;
    public bool IsTriggerSatisfied => _triggerLatched || (!AdvancedTrigger.Enabled && _isRecording);
    public long RecordedSamples => _recordedSamples;
    public long FramesReceived => _framesReceived;
    public long FramesDroppedEstimate => _framesDroppedEstimate;
    public string? RecordingPath => _writer?.FilePath;
    public string? LastStatisticsJournalPath { get; private set; }
    public int? MaxSamples { get; set; }
    public TimeSpan? MaxDuration { get; set; }
    public bool StatisticsJournalEnabled { get; set; } = true;
    public double StatisticsIntervalSeconds { get; set; } = 1.0;
    public bool StopRecordingOnAlarm { get; set; }
    /// <summary>Stop recording when |physical[ThresholdChannelIndex]| >= ThresholdValue.</summary>
    public bool ThresholdStopEnabled { get; set; }
    public int ThresholdChannelIndex { get; set; }
    public double ThresholdValue { get; set; }
    private DateTime? _recordingStarted;

    // Backward-compatible simple trigger bridge
    public TriggerSettings Trigger
    {
        get => new()
        {
            Enabled = AdvancedTrigger.Enabled,
            ChannelIndex = AdvancedTrigger.ChannelA,
            Threshold = AdvancedTrigger.ThresholdA,
            RisingEdge = AdvancedTrigger.RisingA
        };
        set
        {
            AdvancedTrigger.Enabled = value.Enabled;
            AdvancedTrigger.ChannelA = value.ChannelIndex;
            AdvancedTrigger.ThresholdA = value.Threshold;
            AdvancedTrigger.RisingA = value.RisingEdge;
            AdvancedTrigger.Logic = TriggerLogic.Single;
        }
    }

    public event EventHandler<ProcessedSample>? SampleProcessed;
    public event EventHandler? RecordingStopped;
    public event EventHandler<string>? AlarmRaised;
    public event EventHandler<string>? SyncJournal;
    public event EventHandler<string>? StatisticsTick;
    public event EventHandler<ScaleSuspectAlert>? ScaleSuspect;

    /// <summary>
    /// Metrology: software LPF (Bessel/Butterworth) + step windows + scale suspect.
    /// Filtered physical values are used for display, Record CSV, and official step readings.
    /// </summary>
    public MetrologyPipeline Metrology { get; } = new();

    /// <summary>When true (default), apply per-channel FilterHz as software LPF on the live/record path.</summary>
    public bool SoftwareFilterEnabled { get; set; } = true;

    public void Attach(ISpider8Device device)
    {
        Detach();
        Device = device;
        device.SampleReceived += OnSample;
        _alarms.AlarmFired += (_, e) => AlarmRaised?.Invoke(this, $"{e.ChannelName}: {e.Message}");
    }

    public void Detach()
    {
        if (Device is not null)
            Device.SampleReceived -= OnSample;
        Device = null;
    }

    /// <summary>
    /// Arms recording. <paramref name="headers"/> must match the packed layout produced by
    /// <see cref="PackForStorage"/> (selected physical/math/compute columns; PeakInterval expands to _min/_max).
    /// <paramref name="recordIndices"/> indexes into the combined physical+math+compute vector.
    /// </summary>
    public async Task ArmRecordingAsync(
        string path,
        IEnumerable<string> headers,
        IEnumerable<int>? recordIndices = null,
        IEnumerable<string>? metaComments = null)
    {
        await StopRecordingAsync(disposeOnly: true);
        var headerList = headers.ToList();
        var preSamples = Recording.PreTriggerSamples;
        if (AdvancedTrigger.PreTriggerMs > 0 && Device is not null)
            preSamples = Math.Max(preSamples, AdvancedTrigger.PreTriggerMs * Math.Max(1, Device.SampleRateHz) / 1000);

        Recording.PreTriggerSamples = preSamples;
        _recordIndices = recordIndices?.ToArray() ?? Array.Empty<int>();
        _peakMode = string.Equals(Recording.StorageMode, "PeakInterval", StringComparison.OrdinalIgnoreCase);
        _peakIntervalSec = Math.Max(0.05, Recording.PeakIntervalSeconds);
        ResetPeakWindow(DateTime.UtcNow);

        _writer = new CsvRecordingWriter(path, append: Recording.AppendMode && File.Exists(path));
        if (Device is not null)
            _writer.SetRateHzSet(Device.SampleRateHz);
        if (!Recording.AppendMode || !File.Exists(path) || new FileInfo(path).Length == 0)
        {
            if (metaComments is not null)
                _writer.WriteMetaComments(metaComments);
            _writer.WriteHeader(headerList);
        }
        _recordedSamples = 0;
        _recordingArmed = true;
        _isRecording = !AdvancedTrigger.Enabled;
        _triggerLatched = _isRecording;
        _advTrigger.Reset();
        _preBuffer.Clear();
        _postTriggerUntil = null;
        _recordingStarted = DateTime.UtcNow;
        _math.Reset();
        _compute.Reset();
        LastStatisticsJournalPath = null;
        _statsJournal = StatisticsJournalEnabled
            ? new StatisticsJournal(headerList, StatisticsIntervalSeconds)
            : null;
    }

    public async Task StopRecordingAsync(bool disposeOnly = false)
    {
        if (_recordingArmed && _isRecording && _peakMode && _peakCount > 0)
            FlushPeakWindow(DateTime.UtcNow);

        _recordingArmed = false;
        _isRecording = false;
        if (_statsJournal is not null && _writer is not null)
        {
            _statsJournal.FlushFinal(DateTime.UtcNow);
            var statsPath = Path.ChangeExtension(_writer.FilePath, null) + "_stats.csv";
            try
            {
                _statsJournal.Save(statsPath);
                LastStatisticsJournalPath = statsPath;
            }
            catch { /* ignore disk errors */ }
            _statsJournal = null;
        }
        if (_writer is not null)
        {
            try { _writer.WriteEffectiveRateFooter(); } catch { /* ignore */ }
            await _writer.DisposeAsync();
            _writer = null;
            if (!disposeOnly)
                RecordingStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Insert a # MARK comment into the open CSV while recording (Easy-like event mark).</summary>
    public bool TryWriteRecordingMark(string text)
    {
        if (!_isRecording || _writer is null) return false;
        _writer.WriteComment(text);
        return true;
    }

    private void OnSample(object? sender, SampleFrame frame)
    {
        if (Device is null) return;
        _framesReceived++;
        var now = DateTime.UtcNow;
        var dt = (now - _lastFrameUtc).TotalSeconds;
        var expected = 1.0 / Math.Max(1, Device.SampleRateHz);
        if (dt > expected * 2.5)
        {
            _framesDroppedEstimate++;
            SyncJournal?.Invoke(this, $"Sync gap {dt * 1000:0} ms (expected ~{expected * 1000:0} ms)");
        }
        _lastFrameUtc = now;

        var physical = new double[Device.Channels.Count];
        var alarmFiredThisFrame = false;
        for (var i = 0; i < Device.Channels.Count; i++)
        {
            var ch = Device.Channels[i];
            var raw = i < frame.Values.Length ? frame.Values[i] : double.NaN;
            physical[i] = ch.Enabled ? ch.Apply(raw) : double.NaN;
        }

        // Software anti-alias LPF (FilterHz column) — same values for UI + Record + Citire treaptă.
        // ConfigureFilters is called from the VM on Start / filter change; here we only process.
        if (SoftwareFilterEnabled)
        {
            Metrology.ProcessPhysicalFrame(frame, physical, Device.Channels);

            var suspect = Metrology.CheckScaleSuspect(Device.Channels, idleAfterZero: false);
            if (suspect is not null)
                ScaleSuspect?.Invoke(this, suspect);
        }

        for (var i = 0; i < Device.Channels.Count; i++)
        {
            var ch = Device.Channels[i];
            if (!ch.Enabled) continue;
            var before = _alarms.Events.Count;
            _alarms.Evaluate(i, ch.Name, physical[i], ch.AlarmEnabled, ch.AlarmLow, ch.AlarmHigh);
            if (_alarms.Events.Count > before)
                alarmFiredThisFrame = true;
        }

        var math = _math.Evaluate(MathChannels, physical);
        var compute = _compute.Evaluate(ComputeChannels, physical, Math.Max(1e-6, dt));
        var combined = physical.Concat(math).Concat(compute).ToArray();

        if (_recordingArmed)
        {
            _preBuffer.Enqueue((frame, combined));
            while (_preBuffer.Count > Math.Max(0, Recording.PreTriggerSamples))
                _preBuffer.Dequeue();

            if (!_isRecording)
            {
                if (_advTrigger.ShouldStart(physical))
                {
                    _isRecording = true;
                    _triggerLatched = true;
                    _recordingStarted = DateTime.UtcNow;
                    ResetPeakWindow(DateTime.UtcNow);
                    if (AdvancedTrigger.PostTriggerMs > 0)
                        _postTriggerUntil = DateTime.UtcNow.AddMilliseconds(AdvancedTrigger.PostTriggerMs);
                    foreach (var item in _preBuffer)
                        WriteStored(item.frame, item.combined);
                    _preBuffer.Clear();
                }
            }
            else if (_writer is not null)
            {
                WriteStored(frame, combined);

                if (StopRecordingOnAlarm && alarmFiredThisFrame)
                    _ = StopRecordingAsync();
                else if (_postTriggerUntil is DateTime until && DateTime.UtcNow >= until)
                    _ = StopRecordingAsync();
                else if (ThresholdStopEnabled && ThresholdChannelIndex >= 0
                         && ThresholdChannelIndex < physical.Length
                         && !double.IsNaN(physical[ThresholdChannelIndex])
                         && Math.Abs(physical[ThresholdChannelIndex]) >= Math.Abs(ThresholdValue)
                         && Math.Abs(ThresholdValue) > 0)
                    _ = StopRecordingAsync();
                else
                {
                    var durationExceeded = MaxDuration is TimeSpan d && _recordingStarted is DateTime start &&
                                           DateTime.UtcNow - start >= d;
                    var samplesExceeded = MaxSamples is int max && _recordedSamples >= max;
                    if (durationExceeded || samplesExceeded)
                        _ = StopRecordingAsync();
                }
            }
        }

        SampleProcessed?.Invoke(this, new ProcessedSample(frame, physical, math, compute, combined));
    }

    private void WriteStored(SampleFrame frame, double[] combined)
    {
        if (_writer is null) return;

        if (_peakMode)
        {
            AccumulatePeak(combined);
            if ((frame.Timestamp - _peakWindowStart).TotalSeconds >= _peakIntervalSec)
                FlushPeakWindow(frame.Timestamp);
            return;
        }

        var packed = PackForStorage(combined);
        _writer.WriteSample(frame, packed);
        _statsJournal?.Add(frame.Timestamp, packed);
        if (_statsJournal?.LastSummary is string tick)
        {
            StatisticsTick?.Invoke(this, tick);
            _statsJournal.LastSummary = null;
        }
        _recordedSamples++;
    }

    private void AccumulatePeak(double[] combined)
    {
        if (_peakMin is null || _peakMax is null)
            ResetPeakWindow(DateTime.UtcNow);

        var n = _recordIndices.Length > 0 ? _recordIndices.Length : combined.Length;
        for (var i = 0; i < n; i++)
        {
            var src = _recordIndices.Length > 0 ? _recordIndices[i] : i;
            if (src < 0 || src >= combined.Length) continue;
            var v = combined[src];
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            if (double.IsNaN(_peakMin![i]) || v < _peakMin[i]) _peakMin[i] = v;
            if (double.IsNaN(_peakMax![i]) || v > _peakMax[i]) _peakMax[i] = v;
        }
        _peakCount++;
    }

    private void FlushPeakWindow(DateTime stamp)
    {
        if (_writer is null || _peakMin is null || _peakMax is null || _peakCount <= 0)
        {
            ResetPeakWindow(stamp);
            return;
        }

        var n = _peakMin.Length;
        var packed = new double[n * 2];
        for (var i = 0; i < n; i++)
        {
            packed[i * 2] = _peakMin[i];
            packed[i * 2 + 1] = _peakMax[i];
        }

        var frame = new SampleFrame { Timestamp = stamp, Sequence = _recordedSamples, Values = Array.Empty<double>() };
        _writer.WriteSample(frame, packed);
        _statsJournal?.Add(stamp, packed);
        if (_statsJournal?.LastSummary is string tick)
        {
            StatisticsTick?.Invoke(this, tick);
            _statsJournal.LastSummary = null;
        }
        _recordedSamples++;
        ResetPeakWindow(stamp);
    }

    private void ResetPeakWindow(DateTime stamp)
    {
        _peakWindowStart = stamp;
        _peakCount = 0;
        var n = _recordIndices.Length;
        if (n <= 0 && Device is not null)
            n = Device.Channels.Count + MathChannels.Count + ComputeChannels.Count;
        if (n <= 0) n = 1;
        _peakMin = Enumerable.Repeat(double.NaN, n).ToArray();
        _peakMax = Enumerable.Repeat(double.NaN, n).ToArray();
    }

    private double[] PackForStorage(double[] combined)
    {
        if (_recordIndices.Length == 0)
            return combined;
        var packed = new double[_recordIndices.Length];
        for (var i = 0; i < _recordIndices.Length; i++)
        {
            var src = _recordIndices[i];
            packed[i] = src >= 0 && src < combined.Length ? combined[src] : double.NaN;
        }
        return packed;
    }

    public async ValueTask DisposeAsync() => await StopRecordingAsync(disposeOnly: true);
}

public sealed record ProcessedSample(
    SampleFrame Frame,
    double[] Physical,
    double[] Math,
    double[] Compute,
    double[] Combined);
