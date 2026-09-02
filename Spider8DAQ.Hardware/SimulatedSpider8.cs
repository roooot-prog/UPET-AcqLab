using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Simulation;

namespace Spider8DAQ.Hardware;

/// <summary>
/// Generates synthetic multi-channel signals for UI / lab demos without Spider8 hardware.
/// Contur compression: force peak → unload, stroke with plasticity, radial barreling + ovality.
/// </summary>
public sealed class SimulatedSpider8 : ISpider8Device, IDeviceHealth, ISimulatorScenarioSink
{
    private readonly List<ChannelConfig> _channels;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _sequence;
    private readonly object _sync = new();
    private DateTime? _lastSampleUtc;
    private SimulatorScenarioHint? _hint;
    private LabSignalModel _model;
    private SimChannelRole[] _roles;
    private SimulatorScenarioKind _kind = SimulatorScenarioKind.Generic;
    private long _clockSample;
    private bool _resetClock = true;
    private bool _oneShotHoldAnnounced;

    public SimulatedSpider8(int channelCount = 8, int deviceCount = 1)
    {
        deviceCount = Math.Clamp(deviceCount, 1, 8);
        channelCount = Math.Max(channelCount, deviceCount * 8);
        _channels = Enumerable.Range(0, channelCount)
            .Select(i => new ChannelConfig
            {
                Index = i,
                DeviceIndex = i / 8,
                Name = FormatAnalogChannelName(i, channelCount),
                Unit = i % 2 == 0 ? "mV/V" : "N",
                Enabled = true,
                Scale = 1.0
            })
            .ToList();
        DisplayName = deviceCount <= 1 ? "Simulator" : $"Simulator x{deviceCount}";
        _model = new LabSignalModel(channelCount);
        _roles = new SimChannelRole[channelCount];
        RefreshRolesUnlocked();
    }

    private static string FormatAnalogChannelName(int i, int channelCount)
    {
        var local = i % 8;
        var device = i / 8;
        return channelCount > 8 ? $"D{device + 1}.CH{local}" : $"CH{local}";
    }

    public string DisplayName { get; }
    public DeviceConnectionState State { get; private set; } = DeviceConnectionState.Disconnected;
    public IReadOnlyList<ChannelConfig> Channels => _channels;
    public int SampleRateHz { get; set; } = 50;
    public DateTime? LastSampleUtc => _lastSampleUtc;
    public string? FirmwareHint => "SIM-2.0";

    /// <summary>Active scenario after last hint / channel config refresh.</summary>
    public SimulatorScenarioKind ActiveKind
    {
        get { lock (_sync) return _kind; }
    }

    public event EventHandler<SampleFrame>? SampleReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? ConnectionLost;

    public void SetScenarioHint(SimulatorScenarioHint? hint)
    {
        lock (_sync)
        {
            _hint = hint;
            _resetClock = true;
            _oneShotHoldAnnounced = false;
            RefreshRolesUnlocked();
        }

        StatusChanged?.Invoke(this,
            hint is null
                ? "Simulator scenario: Auto"
                : $"Simulator scenario: {_kind}");
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = DeviceConnectionState.Connected;
        StatusChanged?.Invoke(this, "Simulator connected.");
        return Task.CompletedTask;
    }

    public async Task DisconnectAsync()
    {
        await StopStreamingAsync();
        State = DeviceConnectionState.Disconnected;
        StatusChanged?.Invoke(this, "Simulator disconnected.");
    }

    public Task StartStreamingAsync(CancellationToken cancellationToken = default)
    {
        if (State is DeviceConnectionState.Disconnected)
            throw new InvalidOperationException("Not connected.");

        lock (_sync)
        {
            RefreshRolesUnlocked();
            _model.ResetCycle();
            _sequence = 0;
            _clockSample = 0;
            _resetClock = false;
            _oneShotHoldAnnounced = false;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        State = DeviceConnectionState.Streaming;
        StatusChanged?.Invoke(this, $"Streaming (simulator · {_kind}).");
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopStreamingAsync()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { if (_loop is not null) await _loop; } catch { /* ignore */ }
        _cts.Dispose();
        _cts = null;
        _loop = null;
        if (State == DeviceConnectionState.Streaming)
            State = DeviceConnectionState.Connected;
        StatusChanged?.Invoke(this, "Streaming stopped.");
    }

    public Task TareAsync(int? channelIndex = null)
    {
        // Soft Zero in the App adjusts TareValue via ApplyChannelConfig; HW tare stays a baseline no-op.
        lock (_sync)
        {
            if (channelIndex is int idx && idx >= 0 && idx < _channels.Count)
                _channels[idx].TareValue = 0;
            else
            {
                foreach (var ch in _channels)
                    ch.TareValue = 0;
            }
        }

        StatusChanged?.Invoke(this, channelIndex is null ? "Tare all channels." : $"Tare CH{channelIndex + 1}.");
        return Task.CompletedTask;
    }

    public Task<double> ShuntCheckAsync(int channelIndex)
    {
        var reading = 0.97 + Random.Shared.NextDouble() * 0.06;
        if (channelIndex >= 0 && channelIndex < _channels.Count)
        {
            _channels[channelIndex].ShuntEnabled = true;
            _channels[channelIndex].LastShuntReading = reading;
        }
        StatusChanged?.Invoke(this, $"Simulated shunt CH{channelIndex + 1}: {reading:0.000}");
        return Task.FromResult(reading);
    }

    public Task ApplyChannelConfigAsync(IEnumerable<ChannelConfig> channels)
    {
        var list = channels.ToList();
        lock (_sync)
        {
            for (var i = 0; i < Math.Min(list.Count, _channels.Count); i++)
            {
                _channels[i].Name = list[i].Name;
                _channels[i].Unit = list[i].Unit;
                _channels[i].Enabled = list[i].Enabled;
                _channels[i].Scale = list[i].Scale;
                _channels[i].Offset = list[i].Offset;
                _channels[i].TareValue = list[i].TareValue;
                _channels[i].Bridge = list[i].Bridge;
                _channels[i].RangeMvPerV = list[i].RangeMvPerV;
                _channels[i].FilterHz = list[i].FilterHz;
                _channels[i].ChannelSampleRateHz = list[i].ChannelSampleRateHz;
                _channels[i].ExcitationV = list[i].ExcitationV;
                _channels[i].AlarmEnabled = list[i].AlarmEnabled;
                _channels[i].AlarmLow = list[i].AlarmLow;
                _channels[i].AlarmHigh = list[i].AlarmHigh;
                _channels[i].Capacity = list[i].Capacity;
                _channels[i].SensorCategory = list[i].SensorCategory;
                _channels[i].SensorName = list[i].SensorName;
                _channels[i].SensorId = list[i].SensorId;
                _channels[i].RecordEnabled = list[i].RecordEnabled;
            }

            if (_roles.Length != _channels.Count)
            {
                _model = new LabSignalModel(_channels.Count);
                _roles = new SimChannelRole[_channels.Count];
            }

            RefreshRolesUnlocked();
        }

        return Task.CompletedTask;
    }

    private void RefreshRolesUnlocked()
    {
        _kind = SimChannelClassifier.ResolveKind(_hint, _channels);
        _roles = SimChannelClassifier.ClassifyAll(_channels, _hint, _kind);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        var rand = new Random(42);
        var engineering = new double[_channels.Count];
        var values = new double[_channels.Count];

        while (!ct.IsCancellationRequested)
        {
            int rate;
            bool announceHold = false;
            string? holdMsg = null;
            lock (_sync)
            {
                if (_resetClock)
                {
                    _clockSample = 0;
                    _resetClock = false;
                    _oneShotHoldAnnounced = false;
                    _model.ResetCycle();
                }

                rate = Math.Max(1, SampleRateHz);
                if (engineering.Length != _channels.Count)
                {
                    engineering = new double[_channels.Count];
                    values = new double[_channels.Count];
                }

                var t = _clockSample / (double)rate;
                _model.Evaluate(t, _roles, _hint?.RadialAnglesDeg, _hint, _kind, engineering, rand);

                for (var i = 0; i < values.Length; i++)
                {
                    var scale = i < _channels.Count ? _channels[i].Scale : 1.0;
                    const double lsb = 1e-5;
                    values[i] = LabSignalModel.ToRaw(engineering[i], scale, lsb);
                }

                var duration = _hint is { OneShotRamp: true, CycleSeconds: > 0 }
                    ? _hint.CycleSeconds
                    : _hint is { OneShotRamp: true } ? ContourSimAssignment.DurationSeconds : 0;
                if (duration > 0 && t + 1e-12 >= duration && !_oneShotHoldAnnounced)
                {
                    _oneShotHoldAnnounced = true;
                    announceHold = true;
                    holdMsg = "Simulator: rampă contur 10 s încheiată — ținte menținute (stop).";
                }

                _clockSample++;
            }

            var frame = new SampleFrame
            {
                Timestamp = DateTime.UtcNow,
                Sequence = Interlocked.Increment(ref _sequence),
                Values = (double[])values.Clone()
            };
            _lastSampleUtc = frame.Timestamp;
            SampleReceived?.Invoke(this, frame);
            if (announceHold && holdMsg is not null)
                StatusChanged?.Invoke(this, holdMsg);
            var delay = Math.Max(1, 1000 / Math.Max(1, rate));
            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public Task<DeviceHealthReport> SelfTestAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<string>
        {
            $"Simulator state: {State}",
            $"Channels: {_channels.Count}",
            $"Scenario: {_kind}",
            _lastSampleUtc is null
                ? "Last sample: none"
                : $"Last sample age: {(DateTime.UtcNow - _lastSampleUtc.Value).TotalSeconds:0.00}s"
        };
        var age = _lastSampleUtc is DateTime t ? (DateTime.UtcNow - t).TotalSeconds : (double?)null;
        var status = State == DeviceConnectionState.Streaming && age is < 2
            ? HealthStatus.Healthy
            : State is DeviceConnectionState.Connected or DeviceConnectionState.Streaming
                ? HealthStatus.Degraded
                : HealthStatus.Offline;
        return Task.FromResult(new DeviceHealthReport
        {
            Status = status,
            Summary = $"Simulator: {status} ({_kind})",
            Checks = checks,
            LastSampleUtc = _lastSampleUtc,
            SecondsSinceLastSample = age,
            PortOpen = State != DeviceConnectionState.Disconnected,
            FirmwareHint = "SIM-2.0"
        });
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
