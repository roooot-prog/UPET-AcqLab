using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Hardware;

/// <summary>
/// Aggregates multiple Spider8 backends (one COM / simulator each) into a single channel map.
/// </summary>
public sealed class CascadedSpider8 : ISpider8Device, IDeviceHealth
{
    private readonly List<ISpider8Device> _devices;
    private readonly List<ChannelConfig> _channels = new();
    private readonly List<(ISpider8Device device, int localIndex)> _map = new();
    private readonly Dictionary<ISpider8Device, double[]> _latest = new();
    private long _sequence;
    private DateTime? _lastSampleUtc;

    public CascadedSpider8(IEnumerable<ISpider8Device> devices)
    {
        _devices = devices.ToList();
        if (_devices.Count == 0) throw new ArgumentException("Need at least one device.");
        RebuildMap();
        foreach (var d in _devices)
        {
            d.SampleReceived += OnChildSample;
            d.StatusChanged += (_, msg) => StatusChanged?.Invoke(this, $"[{d.DisplayName}] {msg}");
            d.ConnectionLost += OnChildConnectionLost;
        }
    }

    public string DisplayName => $"Cascade x{_devices.Count}";
    public DeviceConnectionState State { get; private set; } = DeviceConnectionState.Disconnected;
    public IReadOnlyList<ChannelConfig> Channels => _channels;
    public int SampleRateHz
    {
        get => _devices[0].SampleRateHz;
        set { foreach (var d in _devices) d.SampleRateHz = value; }
    }

    public event EventHandler<SampleFrame>? SampleReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? ConnectionLost;

    public string DeviceInfo
    {
        get
        {
            var parts = _devices.Select((d, i) => $"#{i + 1} {d.DisplayName} state={d.State} ch={d.Channels.Count}");
            return string.Join("; ", parts);
        }
    }

    private void RebuildMap()
    {
        _channels.Clear();
        _map.Clear();
        var global = 0;
        for (var di = 0; di < _devices.Count; di++)
        {
            var d = _devices[di];
            for (var li = 0; li < d.Channels.Count; li++)
            {
                var src = d.Channels[li];
                _channels.Add(new ChannelConfig
                {
                    Index = global,
                    DeviceIndex = di,
                    Name = $"D{di + 1}.{src.Name}",
                    Unit = src.Unit,
                    Enabled = src.Enabled,
                    Scale = src.Scale,
                    Offset = src.Offset,
                    Bridge = src.Bridge,
                    RangeMvPerV = src.RangeMvPerV,
                    FilterHz = src.FilterHz,
                    ChannelSampleRateHz = src.ChannelSampleRateHz,
                    ExcitationV = src.ExcitationV
                });
                _map.Add((d, li));
                global++;
            }
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = DeviceConnectionState.Connecting;
        foreach (var d in _devices)
            await d.ConnectAsync(cancellationToken);
        State = DeviceConnectionState.Connected;
        StatusChanged?.Invoke(this, $"Cascade connected ({_devices.Count} devices, {_channels.Count} channels).");
    }

    public async Task DisconnectAsync()
    {
        await StopStreamingAsync();
        foreach (var d in _devices)
            await d.DisconnectAsync();
        State = DeviceConnectionState.Disconnected;
    }

    public async Task StartStreamingAsync(CancellationToken cancellationToken = default)
    {
        foreach (var d in _devices)
            await d.StartStreamingAsync(cancellationToken);
        State = DeviceConnectionState.Streaming;
    }

    public async Task StopStreamingAsync()
    {
        foreach (var d in _devices)
            await d.StopStreamingAsync();
        if (State == DeviceConnectionState.Streaming)
            State = DeviceConnectionState.Connected;
    }

    public async Task TareAsync(int? channelIndex = null)
    {
        if (channelIndex is int g && g >= 0 && g < _map.Count)
        {
            var (dev, local) = _map[g];
            await dev.TareAsync(local);
        }
        else
        {
            foreach (var d in _devices)
                await d.TareAsync(null);
        }
    }

    public async Task<double> ShuntCheckAsync(int channelIndex)
    {
        if (channelIndex >= 0 && channelIndex < _map.Count)
        {
            var (dev, local) = _map[channelIndex];
            return await dev.ShuntCheckAsync(local);
        }
        return double.NaN;
    }

    public async Task ApplyChannelConfigAsync(IEnumerable<ChannelConfig> channels)
    {
        var list = channels.ToList();
        var byDevice = new Dictionary<ISpider8Device, List<ChannelConfig>>();
        for (var i = 0; i < Math.Min(list.Count, _map.Count); i++)
        {
            var (dev, local) = _map[i];
            if (!byDevice.TryGetValue(dev, out var bucket))
            {
                bucket = Enumerable.Range(0, dev.Channels.Count).Select(li => new ChannelConfig { Index = li, Name = $"CH{li}" }).ToList();
                byDevice[dev] = bucket;
            }
            var cfg = list[i];
            bucket[local] = new ChannelConfig
            {
                Index = local,
                Name = cfg.Name,
                Unit = cfg.Unit,
                Enabled = cfg.Enabled,
                Scale = cfg.Scale,
                Offset = cfg.Offset,
                TareValue = cfg.TareValue,
                Bridge = cfg.Bridge,
                RangeMvPerV = cfg.RangeMvPerV,
                FilterHz = cfg.FilterHz,
                ChannelSampleRateHz = cfg.ChannelSampleRateHz,
                ExcitationV = cfg.ExcitationV,
                AlarmEnabled = cfg.AlarmEnabled,
                AlarmLow = cfg.AlarmLow,
                AlarmHigh = cfg.AlarmHigh
            };
            _channels[i] = cfg;
            _channels[i].Index = i;
        }

        foreach (var kv in byDevice)
            await kv.Key.ApplyChannelConfigAsync(kv.Value);
    }

    private void OnChildSample(object? sender, SampleFrame frame)
    {
        if (sender is not ISpider8Device dev) return;
        _latest[dev] = frame.Values;
        if (_latest.Count < _devices.Count) return;

        var values = new double[_channels.Count];
        for (var i = 0; i < _map.Count; i++)
        {
            var (d, local) = _map[i];
            values[i] = _latest.TryGetValue(d, out var arr) && local < arr.Length ? arr[local] : double.NaN;
        }

        _lastSampleUtc = DateTime.UtcNow;
        SampleReceived?.Invoke(this, new SampleFrame
        {
            Timestamp = _lastSampleUtc.Value,
            Sequence = Interlocked.Increment(ref _sequence),
            Values = values
        });
    }

    private void OnChildConnectionLost(object? sender, EventArgs e)
        => ConnectionLost?.Invoke(this, EventArgs.Empty);

    public DateTime? LastSampleUtc => _lastSampleUtc;

    public string? FirmwareHint
    {
        get
        {
            var parts = _devices
                .OfType<IDeviceHealth>()
                .Select(h => h.FirmwareHint)
                .Where(s => !string.IsNullOrWhiteSpace(s));
            var joined = string.Join(" ", parts);
            return string.IsNullOrWhiteSpace(joined) ? DeviceInfo : joined;
        }
    }

    public async Task<DeviceHealthReport> SelfTestAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<string> { $"Cascade devices: {_devices.Count}", $"Mapped channels: {_channels.Count}", $"State: {State}" };
        var worst = HealthStatus.Healthy;
        foreach (var d in _devices)
        {
            if (d is IDeviceHealth h)
            {
                var r = await h.SelfTestAsync(cancellationToken);
                checks.Add($"{d.DisplayName}: {r.Status} — {r.Summary}");
                if ((int)r.Status > (int)worst) worst = r.Status;
            }
            else
            {
                checks.Add($"{d.DisplayName}: state={d.State} (no IDeviceHealth)");
                if (d.State == DeviceConnectionState.Disconnected && worst < HealthStatus.Degraded)
                    worst = HealthStatus.Degraded;
            }
        }

        var age = _lastSampleUtc is DateTime t ? (DateTime.UtcNow - t).TotalSeconds : (double?)null;
        if (State == DeviceConnectionState.Disconnected) worst = HealthStatus.Offline;
        else if (State == DeviceConnectionState.Streaming && age is > 5) worst = HealthStatus.Degraded;

        return new DeviceHealthReport
        {
            Status = worst,
            Summary = $"Cascade x{_devices.Count}: {worst}",
            Checks = checks,
            LastSampleUtc = _lastSampleUtc,
            SecondsSinceLastSample = age,
            PortOpen = _devices.Any(d => d.State is DeviceConnectionState.Connected or DeviceConnectionState.Streaming),
            FirmwareHint = DeviceInfo
        };
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var d in _devices)
        {
            d.SampleReceived -= OnChildSample;
            await d.DisposeAsync();
        }
    }
}
