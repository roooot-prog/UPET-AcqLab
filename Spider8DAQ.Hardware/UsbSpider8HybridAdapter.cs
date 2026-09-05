using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Hardware;

/// <summary>
/// USBHBM entry: try Intfac32 (integer PORT_USB=100) first; on failure fall back to DEST SoftSetup measure.
/// OpenPort=-1 is common on this PC — do not thrash Intfac 3× (wedges usbhbm); release + DEST quickly.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class UsbSpider8HybridAdapter : ISpider8Device, IDeviceHealth, IDigitalIoChannel
{
    private readonly string? _serialHint;
    private readonly int _channelCount;
    private ISpider8Device? _inner;
    private string _mode = "none";

    public UsbSpider8HybridAdapter(string? serialHint, int channelCount = 8)
    {
        _serialHint = serialHint;
        _channelCount = channelCount;
    }

    public string DisplayName => _inner?.DisplayName
        ?? (string.IsNullOrEmpty(HbmUsbIo.NormalizeSerial(_serialHint))
            ? "Spider8 [USB]"
            : $"Spider8 [USB {HbmUsbIo.NormalizeSerial(_serialHint)}]");

    public DeviceConnectionState State => _inner?.State ?? DeviceConnectionState.Disconnected;
    public IReadOnlyList<ChannelConfig> Channels =>
        _inner?.Channels ?? Array.Empty<ChannelConfig>();
    public int SampleRateHz
    {
        get => _inner?.SampleRateHz ?? 50;
        set { if (_inner is not null) _inner.SampleRateHz = value; }
    }

    public DateTime? LastSampleUtc =>
        _inner is IDeviceHealth h ? h.LastSampleUtc : null;

    public string? FirmwareHint =>
        _inner is IDeviceHealth h ? h.FirmwareHint : null;

    public int? DigitalChannelIndex => (_inner as IDigitalIoChannel)?.DigitalChannelIndex;

    public event EventHandler<SampleFrame>? SampleReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? ConnectionLost;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Exception? intfacEx = null;

        if (Intfac32Native.IsCatmanEasyRunning())
        {
            StatusChanged?.Invoke(this,
                "catman Easy rulează — USBHBM exclusiv. Închideți catmanEASY.exe, apoi Connect. Trec pe DEST oricum…");
        }

        if (Intfac32Native.IsAvailable())
        {
            // Soft busy hint — another process may hold Intfac handles.
            try
            {
                var n = Intfac32Native.GetNumOpenHandles();
                if (n > 0)
                    StatusChanged?.Invoke(this,
                        $"Intfac: {n} handle(s) deschise — ClosePort înainte de OpenPort…");
            }
            catch { /* ignore */ }

            // Max 2 Intfac attempts. On PORT_USB=-1 stop immediately → DEST (3× Wake thrash wedges pipe).
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StatusChanged?.Invoke(this,
                    attempt == 1
                        ? "Încerc Intfac32 (ClosePort → OpenPort)…"
                        : "Reîncerc Intfac32 o dată după ClosePort…");

                Intfac32Native.ReleaseUsbStack(closeLib: attempt > 1);
                await Task.Delay(120 * attempt, cancellationToken);

                var intfac = new IntfacSpider8Adapter(_serialHint, _channelCount);
                Wire(intfac);
                try
                {
                    await intfac.ConnectAsync(cancellationToken);
                    _inner = intfac;
                    _mode = "intfac";
                    StatusChanged?.Invoke(this, "Conectat via Intfac32.");
                    return;
                }
                catch (Exception ex)
                {
                    intfacEx = ex;
                    var openBusy = IsOpenPortBusy(ex);
                    StatusChanged?.Invoke(this,
                        openBusy
                            ? $"Intfac PORT_USB ocupat (−1) — #{attempt}. Nu e eroare fatală; trec pe DEST SoftSetup…"
                            : $"Intfac indisponibil #{attempt}: {Trunc(ex.Message, 90)}");
                    try { await intfac.DisposeAsync(); } catch { /* ignore */ }
                    Unwire(intfac);
                    Intfac32Native.ReleaseUsbStack(closeLib: true);

                    // OpenPort=-1 / catman lock: further Intfac retries rarely help and block DEST.
                    if (openBusy || Intfac32Native.IsCatmanEasyRunning())
                        break;

                    if (attempt < 2)
                        await Task.Delay(250, cancellationToken);
                }
            }

            StatusChanged?.Invoke(this, "Intfac nefolosit — fallback DEST SoftSetup (USBHBM).");
        }
        else
        {
            StatusChanged?.Invoke(this, "Intfac32.dll absent — DEST SoftSetup fallback.");
        }

        // Ensure Intfac released the stack before raw DEST CreateFile.
        if (Intfac32Native.IsAvailable())
        {
            Intfac32Native.ReleaseUsbStack(closeLib: true);
            await Task.Delay(200, cancellationToken);
        }

        var dest = new HbmUsbSpider8Adapter(_serialHint, _channelCount);
        Wire(dest);
        try
        {
            await dest.ConnectAsync(cancellationToken);
            _inner = dest;
            _mode = "dest";
            StatusChanged?.Invoke(this,
                "DEST (fără Intfac) — USB deschis, nu confirmă alimentarea Spider8. Start verifică OMB.");
        }
        catch (Exception ex)
        {
            try { await dest.DisposeAsync(); } catch { /* ignore */ }
            Unwire(dest);
            var tip = Intfac32Native.IsCatmanEasyRunning()
                ? " Închideți catman Easy, apoi Connect."
                : " Deconectați USB 5 s, stingeți Spider8 (LED ERROR), reporniți UPET, Connect. Sau Simulator.";
            throw new InvalidOperationException(
                "USB Connect eșuat (Intfac și DEST). " +
                Trunc(intfacEx?.Message ?? "", 120) + " | " + Trunc(ex.Message, 120) + tip,
                ex);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_inner is null) return;
        await _inner.DisconnectAsync();
        if (Intfac32Native.IsAvailable())
            Intfac32Native.ReleaseUsbStack(closeLib: true);
    }

    public Task StartStreamingAsync(CancellationToken cancellationToken = default)
    {
        if (_inner is null)
            throw new InvalidOperationException("Nu sunteți conectat.");
        return _inner.StartStreamingAsync(cancellationToken);
    }

    public Task StopStreamingAsync() =>
        _inner?.StopStreamingAsync() ?? Task.CompletedTask;

    public Task TareAsync(int? channelIndex = null) =>
        _inner?.TareAsync(channelIndex) ?? Task.CompletedTask;

    public Task ApplyChannelConfigAsync(IEnumerable<ChannelConfig> channels) =>
        _inner?.ApplyChannelConfigAsync(channels) ?? Task.CompletedTask;

    public Task<double> ShuntCheckAsync(int channelIndex) =>
        _inner?.ShuntCheckAsync(channelIndex) ?? Task.FromResult(double.NaN);

    public Task<DeviceHealthReport> SelfTestAsync(CancellationToken cancellationToken = default)
    {
        if (_inner is IDeviceHealth h)
            return h.SelfTestAsync(cancellationToken);
        return Task.FromResult(new DeviceHealthReport
        {
            Status = HealthStatus.Offline,
            Summary = "Hybrid: neconectat",
            PortOpen = false,
            Checks = new List<string> { "mode=" + _mode }
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_inner is null)
        {
            if (Intfac32Native.IsAvailable())
                Intfac32Native.ReleaseUsbStack(closeLib: true);
            return;
        }
        Unwire(_inner);
        await _inner.DisposeAsync();
        _inner = null;
        _mode = "none";
        if (Intfac32Native.IsAvailable())
            Intfac32Native.ReleaseUsbStack(closeLib: true);
    }

    private void Wire(ISpider8Device d)
    {
        d.SampleReceived += OnSample;
        d.StatusChanged += OnStatus;
        d.ConnectionLost += OnConnectionLost;
    }

    private void Unwire(ISpider8Device d)
    {
        d.SampleReceived -= OnSample;
        d.StatusChanged -= OnStatus;
        d.ConnectionLost -= OnConnectionLost;
    }

    private void OnSample(object? sender, SampleFrame e) => SampleReceived?.Invoke(this, e);
    private void OnStatus(object? sender, string e) => StatusChanged?.Invoke(this, e);
    private void OnConnectionLost(object? sender, EventArgs e) => ConnectionLost?.Invoke(this, e);

    private static bool IsOpenPortBusy(Exception ex)
    {
        var m = ex.Message ?? "";
        return m.Contains("-1", StringComparison.Ordinal)
            || m.Contains("ERR_PORT_OPEN_FAILED", StringComparison.OrdinalIgnoreCase)
            || m.Contains("PORT_USB", StringComparison.OrdinalIgnoreCase)
            || m.Contains("OpenPort", StringComparison.OrdinalIgnoreCase)
            || m.Contains("catman", StringComparison.OrdinalIgnoreCase);
    }

    private static string Trunc(string s, int n) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= n ? s : s[..n] + "…";
}
