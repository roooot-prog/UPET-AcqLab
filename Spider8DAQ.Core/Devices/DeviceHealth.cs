namespace Spider8DAQ.Core.Devices;

public enum HealthStatus
{
    Unknown,
    Healthy,
    Degraded,
    Offline,
    Fault
}

public sealed class DeviceHealthReport
{
    public HealthStatus Status { get; init; } = HealthStatus.Unknown;
    public string Summary { get; init; } = "";
    public DateTime CheckedUtc { get; init; } = DateTime.UtcNow;
    public DateTime? LastSampleUtc { get; init; }
    public double? SecondsSinceLastSample { get; init; }
    public bool PortOpen { get; init; }
    public string? FirmwareHint { get; init; }
    public List<string> Checks { get; init; } = new();
}

public interface IDeviceHealth
{
    DateTime? LastSampleUtc { get; }
    /// <summary>Last IDN?/firmware string when the adapter queried it; null if unknown.</summary>
    /// <remarks>
    /// EST? 10000 = reset ACK. 10001–10020 = hard LED (10003 amplifier/channel, 10005 bad param).
    /// Streaming can still show EST=10003 + «date OK»; sticky hard → power-cycle USB.
    /// </remarks>
    string? FirmwareHint { get; }
    Task<DeviceHealthReport> SelfTestAsync(CancellationToken cancellationToken = default);
}

public sealed class DeviceWatchdog : IAsyncDisposable
{
    private readonly Func<Task<bool>> _reconnectAsync;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pollInterval;
    private readonly Func<bool>? _isDevicePresent;
    private readonly Func<bool>? _monitorSamples;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTime _lastSampleUtc = DateTime.UtcNow;
    private int _reconnectAttempts;

    public DeviceWatchdog(
        Func<Task<bool>> reconnectAsync,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        Func<bool>? isDevicePresent = null,
        Func<bool>? monitorSamples = null)
    {
        _reconnectAsync = reconnectAsync;
        _timeout = timeout ?? TimeSpan.FromSeconds(3);
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(500);
        _isDevicePresent = isDevicePresent;
        _monitorSamples = monitorSamples;
    }

    public bool Enabled { get; set; } = true;
    public int MaxReconnectAttempts { get; set; } = 5;
    public DateTime LastSampleUtc => _lastSampleUtc;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? ConnectionLost;

    public void NotifySample() => _lastSampleUtc = DateTime.UtcNow;

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
        _reconnectAttempts = 0;
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_pollInterval, ct);
                if (!Enabled) continue;

                if (_isDevicePresent is not null && !_isDevicePresent())
                {
                    StatusChanged?.Invoke(this, "Watchdog: DEST USB absent — comunicare pierdută.");
                    ConnectionLost?.Invoke(this, EventArgs.Empty);
                    break;
                }

                if (_monitorSamples is not null && !_monitorSamples())
                    continue;

                var age = DateTime.UtcNow - _lastSampleUtc;
                if (age <= _timeout) continue;

                if (_reconnectAttempts >= MaxReconnectAttempts)
                {
                    StatusChanged?.Invoke(this, $"Watchdog: offline >{_timeout.TotalSeconds:0.#}s, max reconnects reached.");
                    ConnectionLost?.Invoke(this, EventArgs.Empty);
                    break;
                }

                _reconnectAttempts++;
                StatusChanged?.Invoke(this, $"Watchdog: no samples for {age.TotalSeconds:0.0}s — reconnect attempt {_reconnectAttempts}/{MaxReconnectAttempts}...");
                bool ok;
                try
                {
                    ok = await _reconnectAsync();
                }
                catch (Exception ex)
                {
                    StatusChanged?.Invoke(this, $"Watchdog error: {ex.Message}");
                    ok = false;
                }

                if (ok)
                {
                    _lastSampleUtc = DateTime.UtcNow;
                    _reconnectAttempts = 0;
                    StatusChanged?.Invoke(this, "Watchdog: reconnect finished.");
                }
                // Failed/early reconnect must NOT reset _lastSampleUtc (masks offline).
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Watchdog error: {ex.Message}");
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }
}
