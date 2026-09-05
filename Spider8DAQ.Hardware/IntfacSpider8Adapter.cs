using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Hardware;

/// <summary>
/// Spider8 over catman-compatible <c>Intfac32.dll</c> (HBM_OpenPort USB + WritePort/ReadPort).
/// Live values require SoftSetup (ACT/ASA) + MSV0,1,6100,1 + poll <c>OMB?0</c> (HBM MSV.htm / OMB0.htm).
/// v2.9.6 OpenPort success without OMB poll left LiveValues flat — DEST already polled OMB.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class IntfacSpider8Adapter : ISpider8Device, IDeviceHealth, IDigitalIoChannel
{
    private const int IdsHaBr = 351;
    private const int Ids3mV = 700;
    private const int SpiderDigitsFullScale = 25000;

    private readonly string? _serialHint;
    private readonly List<ChannelConfig> _channels;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private CancellationTokenSource? _linkCts;
    private Task? _linkLoop;
    private long _sequence;
    private DateTime? _lastSampleUtc;
    private string? _lastIdn;
    private long _framesEmitted;
    private long _emptyReads;
    private long _readErrors;
    private readonly StringBuilder _rx = new();
    private readonly object _rxLock = new();
    private int? _digitalChannelIndex;
    private int _activeAnalogCount = 1;
    private bool _portOpen;
    private bool _deviceInError;
    private string? _openedAs;
    private bool _measureArmed;
    private int _connectionLostRaised;
    private long _lastValidFrameTick;
    private int _consecutiveHardUsbErrors;
    private double[] _lastRawValues = Array.Empty<double>();
    /// <summary>ASA FS actually programmed — OMB ElectrValue must use this (parity with DEST).</summary>
    private readonly double[] _asaRangeMvPerV = Enumerable.Repeat(3.0, 8).ToArray();

    public IntfacSpider8Adapter(string? serialHint, int channelCount = 8)
    {
        _serialHint = serialHint;
        var n = Math.Clamp(channelCount, 8, 10);
        _channels = Enumerable.Range(0, n)
            .Select(i => new ChannelConfig { Index = i, Name = $"CH{i}", Unit = "mV/V", Enabled = i == 0 })
            .ToList();
        _lastRawValues = new double[n];
        Array.Fill(_lastRawValues, double.NaN);
    }

    public string DisplayName
    {
        get
        {
            var s = HbmUsbIo.NormalizeSerial(_serialHint);
            return string.IsNullOrEmpty(s) ? "Spider8 [Intfac USB]" : $"Spider8 [Intfac {s}]";
        }
    }

    public DeviceConnectionState State { get; private set; } = DeviceConnectionState.Disconnected;
    public IReadOnlyList<ChannelConfig> Channels => _channels;
    public int SampleRateHz { get; set; } = 50;
    public DateTime? LastSampleUtc => _lastSampleUtc;
    public string? FirmwareHint => _lastIdn;
    public int? DigitalChannelIndex => _digitalChannelIndex;

    public event EventHandler<SampleFrame>? SampleReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? ConnectionLost;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = DeviceConnectionState.Connecting;
        _connectionLostRaised = 0;
        StatusChanged?.Invoke(this, "Deschid USBHBM via Intfac32 (stack catman)...");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Intfac32Native.IsAvailable())
                throw new InvalidOperationException(
                    "Intfac32.dll indisponibil. Copiați din catmanEasy în vendor\\Intfac32.dll.");

            Intfac32Native.EnsureLoaded();
            StatusChanged?.Invoke(this, "Intfac32: " + (Intfac32Native.LoadedFrom ?? "?"));

            if (Intfac32Native.IsCatmanEasyRunning())
            {
                throw new InvalidOperationException(
                    "catman Easy ține USBHBM deschis — închideți catmanEASY.exe, apoi Connect din nou (sau lăsați fallback DEST).");
            }

            // Stale ClosePort before OpenPort — leftover handles → PORT_USB=-1.
            Intfac32Native.ReleaseUsbStack(closeLib: true);
            var openLib = Intfac32Native.OpenInterfaceLib();
            var maxUsb = Intfac32Native.GetMaxUsbDevice();
            var handles = Intfac32Native.GetNumOpenHandles();
            StatusChanged?.Invoke(this, $"OpenInterfaceLib={openLib} MaxUsb={maxUsb} handles={handles}");

            if (maxUsb <= 0)
            {
                Intfac32Native.ReleaseUsbStack(closeLib: true);
                throw new InvalidOperationException(
                    "Intfac: niciun dispozitiv USBHBM (MaxUsb=0). Verificați cablul/driverul usbhbm, apoi Connect.");
            }

            _ = Intfac32Native.SelectDevice(0);
            _ = Intfac32Native.ActivateInterpreter();
            _ = Intfac32Native.Spider8WakeUp();
            Thread.Sleep(350);

            var opened = false;
            Exception? last = null;
            int lastRc = -99;
            foreach (var name in Intfac32Native.CandidateUsbPortNames(_serialHint))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Intfac32Native.ReleaseUsbStack(closeLib: false);
                // Re-arm after ClosePort — otherwise OpenPort stays -1 after a soft release.
                _ = Intfac32Native.OpenInterfaceLib();
                if (maxUsb > 0)
                {
                    _ = Intfac32Native.SelectDevice(0);
                    _ = Intfac32Native.ActivateInterpreter();
                }
                var rc = Intfac32Native.TryOpenPort(name);
                lastRc = rc;
                StatusChanged?.Invoke(this, $"HBM_OpenPort(\"{name}\") rc={rc}");
                if (rc >= 0)
                {
                    _openedAs = name;
                    opened = true;
                    break;
                }
                // -1 = recognized port but open failed (busy / locked) — stop thrashing strings.
                if (rc == -1)
                {
                    last = new InvalidOperationException(
                        "PORT_USB=-1 (ERR_PORT_OPEN_FAILED — USB ocupat sau Intfac nu poate deschide pe acest PC)");
                    break;
                }
                last = new InvalidOperationException($"OpenPort {name} => {rc}");
            }

            if (!opened)
            {
                Intfac32Native.ReleaseUsbStack(closeLib: true);
                var tip = Intfac32Native.IsCatmanEasyRunning()
                    ? "Închideți catman Easy. "
                    : handles > 0
                        ? "Handles Intfac rămase — reporniți UPET AcqLab. "
                        : "Trec pe DEST SoftSetup. ";
                throw new InvalidOperationException(
                    $"Intfac OpenPort eșuat (rc={lastRc}). {tip}" + (last?.Message ?? ""),
                    last);
            }

            Intfac32Native.SetTimeoutMs(500);
            try { Intfac32Native.FlushInQueue(); } catch { /* optional */ }

            // FIRST command after open must be EST? (reset 10000). No STP/DCL/ACT on Connect.
            // One-shot hard (10005 after prior SoftSetup) often clears on the next EST? — only sticky
            // consecutive hard replies mean real LED ERROR / power-cycle.
            var healthy = Spider8Est.TryReachHealthy(
                () => Intfac32Native.Transact("EST?", 120, 3),
                msg => StatusChanged?.Invoke(this, msg),
                "Connect",
                out _,
                out _);
            _deviceInError = !healthy;
            if (!healthy)
                StatusChanged?.Invoke(this, "Power-cycle Spider8 — LED ERROR");

            try
            {
                var idn = CleanAscii(Intfac32Native.Transact("IDN?", 100, 2));
                if (!string.IsNullOrWhiteSpace(idn) && idn != "?")
                {
                    _lastIdn = idn;
                    StatusChanged?.Invoke(this, "IDN: " + Trunc(idn, 80));
                }
            }
            catch { /* optional */ }

            _portOpen = true;
            State = DeviceConnectionState.Connected;
            EnsureDigitalChannelSlot();
            StatusChanged?.Invoke(this,
                $"Conectat via Intfac32 ({_openedAs}). CH0–CH7 analog; EST? ACK pe Connect.");
            StartLinkMonitor();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SafeClosePort();
            State = DeviceConnectionState.Error;
            var msg = "Connect Intfac eșuat: " + ex.Message;
            StatusChanged?.Invoke(this, msg);
            throw new InvalidOperationException(msg, ex);
        }
    }

    public async Task DisconnectAsync()
    {
        StopLinkMonitor();
        await StopStreamingAsync();
        StopLinkMonitor();
        SafeClosePort();
        State = DeviceConnectionState.Disconnected;
        if (Volatile.Read(ref _connectionLostRaised) == 0)
            StatusChanged?.Invoke(this, "Intfac USB deconectat.");
    }

    public Task StartStreamingAsync(CancellationToken cancellationToken = default)
    {
        if (!_portOpen)
            throw new InvalidOperationException("Nu sunteți conectat via Intfac.");

        StopLinkMonitor();

        // Re-probe EST? even if a prior SoftSetup left _deviceInError — reading often clears LED.
        if (!EnsureHealthy("înainte de Start"))
        {
            State = DeviceConnectionState.Connected;
            StatusChanged?.Invoke(this, "Power-cycle Spider8 — LED ERROR");
            StartLinkMonitor();
            throw new InvalidOperationException("Power-cycle Spider8 — LED ERROR");
        }

        // Minimal Intfac dialect: ACT+ASA only on enabled analogs, then MSV. CR terminators.
        if (!SoftSetupAcquisition())
        {
            State = DeviceConnectionState.Connected;
            StatusChanged?.Invoke(this, "Power-cycle Spider8 — LED ERROR");
            StartLinkMonitor();
            throw new InvalidOperationException("Power-cycle Spider8 — LED ERROR");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _framesEmitted = 0;
        _emptyReads = 0;
        _readErrors = 0;
        _consecutiveHardUsbErrors = 0;
        _lastValidFrameTick = Environment.TickCount64;
        _connectionLostRaised = 0;
        lock (_rxLock) _rx.Clear();

        try
        {
            // Continuous MSV (number=0) — values come from OMB?0, not ASCII push (HBM MSV.htm).
            Intfac32Native.Write("MSV0,1,6100,1");
            _measureArmed = true;
            // EST? clears ERROR LED without stopping MSV (HBM EST.htm).
            try
            {
                var est = Intfac32Native.Transact("EST?", 100, 3);
                if (Spider8Est.IsResetStatus(est))
                    est = Intfac32Native.Transact("EST?", 100, 2);
                var code = Spider8Est.PrimaryCode(est);
                StatusChanged?.Invoke(this,
                    $"Streaming Intfac live: {_activeAnalogCount} canale @ {SampleRateHz} Hz (OMB?0) · {Spider8Est.UiLedMessage(code, samplesOk: true)}");
            }
            catch
            {
                StatusChanged?.Invoke(this,
                    $"Streaming Intfac live: {_activeAnalogCount} canale @ {SampleRateHz} Hz (OMB?0).");
            }
            State = DeviceConnectionState.Streaming;
        }
        catch (Exception ex)
        {
            TryStopMeasure();
            _cts.Dispose();
            _cts = null;
            State = DeviceConnectionState.Connected;
            StatusChanged?.Invoke(this, "MSV Intfac: " + ex.Message);
            StartLinkMonitor();
            throw;
        }

        _loop = Task.Run(() => PollLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopStreamingAsync()
    {
        TryStopMeasure();
        if (_cts is null)
        {
            if (State == DeviceConnectionState.Streaming)
                State = DeviceConnectionState.Connected;
            if (Volatile.Read(ref _connectionLostRaised) == 0 && _portOpen
                && State == DeviceConnectionState.Connected)
                StartLinkMonitor();
            return;
        }
        _cts.Cancel();
        try { if (_loop is not null) await _loop; } catch { /* ignore */ }
        _cts.Dispose();
        _cts = null;
        _loop = null;
        if (State == DeviceConnectionState.Streaming)
            State = DeviceConnectionState.Connected;
        if (Volatile.Read(ref _connectionLostRaised) == 0)
        {
            StatusChanged?.Invoke(this,
                _framesEmitted > 0
                    ? $"Streaming Intfac oprit ({_framesEmitted} cadre)."
                    : "Streaming Intfac oprit (0 cadre — CH0 Half + senzor, catman închis).");
            if (_portOpen && State == DeviceConnectionState.Connected)
                StartLinkMonitor();
        }
    }

    public Task TareAsync(int? channelIndex = null)
    {
        if (!_portOpen) return Task.CompletedTask;
        if (channelIndex is int idx)
        {
            try { Intfac32Native.Write($"TAV{idx},0.00000"); } catch { /* ignore */ }
            try { Intfac32Native.Write($"TAR{idx + 1}"); } catch { /* ignore */ }
        }
        else
        {
            try { Intfac32Native.Write("TAR"); } catch { /* ignore */ }
        }
        StatusChanged?.Invoke(this, "Tare trimis via Intfac.");
        return Task.CompletedTask;
    }

    public Task ApplyChannelConfigAsync(IEnumerable<ChannelConfig> channels)
    {
        var list = channels.ToList();
        for (var i = 0; i < Math.Min(list.Count, _channels.Count); i++)
        {
            var cfg = list[i];
            _channels[i].Name = cfg.Name;
            _channels[i].Unit = cfg.Unit;
            _channels[i].Enabled = cfg.Enabled;
            _channels[i].Scale = cfg.Scale;
            _channels[i].Offset = cfg.Offset;
            _channels[i].TareValue = cfg.TareValue;
            _channels[i].Bridge = cfg.Bridge;
            _channels[i].RangeMvPerV = cfg.RangeMvPerV;
            _channels[i].FilterHz = cfg.FilterHz;
            _channels[i].ChannelSampleRateHz = cfg.ChannelSampleRateHz;
            _channels[i].ExcitationV = cfg.ExcitationV;
            _channels[i].Capacity = cfg.Capacity;
            _channels[i].SensorCategory = cfg.SensorCategory;
            _channels[i].SensorName = cfg.SensorName;
            _channels[i].SensorId = cfg.SensorId;
        }
        var n = _channels.Count(c => c.Enabled && _digitalChannelIndex != c.Index);
        StatusChanged?.Invoke(this, $"Config stocată (Intfac): active={n} (aplicat la Start).");
        return Task.CompletedTask;
    }

    public Task<double> ShuntCheckAsync(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= _channels.Count)
            return Task.FromResult(double.NaN);
        if (_digitalChannelIndex == channelIndex)
            return Task.FromResult(double.NaN);

        double reading = double.NaN;
        // HBM ASS.htm: 0-based n matching ACT/ASA/OMB. CHnSH1 was off-by-one (CH0 → CH1SH1).
        var n = channelIndex;
        if (_portOpen)
        {
            try
            {
                Intfac32Native.FlushInQueue();
                Intfac32Native.Write($"ASS{n},43");
                Thread.Sleep(150);

                // 1) ASCII OMB reply (if interpreter returns floats)
                foreach (var q in new[] { "OMB?0", "OMB?", "OMB?1" })
                {
                    if (!double.IsNaN(reading)) break;
                    try
                    {
                        var reply = Intfac32Native.Transact(q, 120, 3);
                        reading = TryParseOmbAsciiChannel(reply, channelIndex);
                        if (double.IsNaN(reading))
                            reading = TryParseOmbBinaryFromAsciiBlob(reply, channelIndex);
                    }
                    catch { /* try next */ }
                }

                // 1b) Raw drain after SH1 (some dialects emit #0 blob without OMB?)
                if (double.IsNaN(reading))
                {
                    try
                    {
                        Thread.Sleep(40);
                        var raw = Intfac32Native.Read(4096);
                        reading = TryParseOmbAsciiChannel(raw, channelIndex);
                        if (double.IsNaN(reading))
                            reading = TryParseOmbBinaryFromAsciiBlob(raw, channelIndex);
                    }
                    catch { /* ignore */ }
                }

                // 2) While streaming: wait for live frames with shunt engaged
                if (double.IsNaN(reading) && State == DeviceConnectionState.Streaming)
                {
                    var before = _framesEmitted;
                    var t0 = Environment.TickCount64;
                    while (Environment.TickCount64 - t0 < 350 && _framesEmitted <= before)
                        Thread.Sleep(20);
                    if (channelIndex < _lastRawValues.Length
                        && !double.IsNaN(_lastRawValues[channelIndex]))
                        reading = _lastRawValues[channelIndex];
                }

                // 3) Not streaming: one-shot kick MSV + drain ASCII numbers
                if (double.IsNaN(reading) && State != DeviceConnectionState.Streaming)
                {
                    try
                    {
                        Intfac32Native.Write("MSV0,1,6100,1");
                        Thread.Sleep(80);
                        var chunk = Intfac32Native.Read(4096);
                        reading = TryParseOmbAsciiChannel(chunk, channelIndex);
                        if (double.IsNaN(reading))
                            reading = TryParseOmbBinaryFromAsciiBlob(chunk, channelIndex);
                        Intfac32Native.Write("STP");
                    }
                    catch { /* ignore */ }
                }

                Intfac32Native.Write($"ASS{n},42");
                Thread.Sleep(40);
                StatusChanged?.Invoke(this, double.IsNaN(reading)
                    ? $"Shunt CH{channelIndex}: ASS{n},43/42 OK (fără valoare — Start măsurare, apoi Shunt)."
                    : $"Shunt CH{channelIndex}: {reading:G6} (ASS{n},43).");
            }
            catch (Exception ex)
            {
                try { Intfac32Native.Write($"ASS{n},42"); } catch { /* ignore */ }
                StatusChanged?.Invoke(this, $"Shunt Intfac: {ex.Message}");
            }
        }

        _channels[channelIndex].ShuntEnabled = true;
        if (!double.IsNaN(reading))
            _channels[channelIndex].LastShuntReading = reading;
        return Task.FromResult(reading);
    }

    /// <summary>Parse OMB binary (#0 + int16 LE) even when ReadPort stuffed bytes into a string.</summary>
    private double TryParseOmbBinaryFromAsciiBlob(string? blob, int channelIndex)
    {
        if (string.IsNullOrEmpty(blob)) return double.NaN;
        var bytes = Encoding.Latin1.GetBytes(blob);
        var start = -1;
        for (var i = 0; i + 1 < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'#' && bytes[i + 1] == (byte)'0')
            {
                start = i + 2;
                break;
            }
        }
        if (start < 0) return double.NaN;

        var ai = 0;
        for (var i = 0; i < _channels.Count; i++)
        {
            if (_digitalChannelIndex == i) continue;
            if (!_channels[i].Enabled) continue;
            var off = start + ai * 2;
            if (off + 2 > bytes.Length) return double.NaN;
            if (i == channelIndex)
            {
                var raw = (short)(bytes[off] | (bytes[off + 1] << 8));
                var range = i < _asaRangeMvPerV.Length && _asaRangeMvPerV[i] > 0
                    ? _asaRangeMvPerV[i]
                    : OmbFallbackRange(_channels[i]);
                return range * raw / SpiderDigitsFullScale;
            }
            ai++;
        }
        return double.NaN;
    }

    private double TryParseOmbAsciiChannel(string? reply, int channelIndex)
    {
        if (string.IsNullOrWhiteSpace(reply)) return double.NaN;
        // Accept "1.23;2.34" or space-separated floats matching active order.
        var parts = reply.Replace(',', ' ').Replace(';', ' ')
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var nums = new List<double>();
        foreach (var p in parts)
        {
            if (double.TryParse(p, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)
                && v < 10000)
                nums.Add(v);
        }
        var ai = 0;
        for (var i = 0; i < _channels.Count; i++)
        {
            if (_digitalChannelIndex == i) continue;
            if (!_channels[i].Enabled) continue;
            if (ai >= nums.Count) break;
            if (i == channelIndex) return nums[ai];
            ai++;
        }
        return channelIndex == 0 && nums.Count > 0 ? nums[0] : double.NaN;
    }

    public Task<DeviceHealthReport> SelfTestAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<string>
        {
            _portOpen ? "Intfac port: OK" : "Intfac port: FAIL",
            "OpenedAs: " + (_openedAs ?? "-"),
            $"Frames: {_framesEmitted}"
        };
        if (_portOpen)
        {
            try
            {
                var est = Intfac32Native.Transact("EST?", 100, 2);
                checks.Add(string.IsNullOrWhiteSpace(est) ? "EST?: empty" : "EST?: " + Trunc(est, 60));
            }
            catch (Exception ex)
            {
                checks.Add("EST? err: " + ex.Message);
            }
        }

        var age = _lastSampleUtc is DateTime t ? (DateTime.UtcNow - t).TotalSeconds : (double?)null;
        var status = !_portOpen ? HealthStatus.Offline
            : age is > 3 or null ? HealthStatus.Degraded
            : HealthStatus.Healthy;

        return Task.FromResult(new DeviceHealthReport
        {
            Status = status,
            Summary = $"Intfac USB: {status}",
            PortOpen = _portOpen,
            Checks = checks,
            LastSampleUtc = _lastSampleUtc,
            SecondsSinceLastSample = age,
            FirmwareHint = _lastIdn
        });
    }

    private bool SoftSetupAcquisition()
    {
        try { Intfac32Native.FlushInQueue(); } catch { /* ignore */ }

        _activeAnalogCount = 0;
        Array.Clear(_asaRangeMvPerV);

        // Align ACT mask (no EST? after each off — same ERROR latch risk as HBM USB path).
        for (var i = 0; i < _channels.Count; i++)
        {
            if (_digitalChannelIndex == i) continue;
            if (_channels[i].Enabled) continue;
            try { Intfac32Native.Write($"ACT{i},0"); } catch { /* ignore */ }
            Thread.Sleep(8);
        }
        try { Intfac32Native.FlushInQueue(); } catch { /* ignore */ }
        // Clear any 10005 from ACT off without aborting SoftSetup (same as DEST allowHardContinue).
        _ = Spider8Est.TryReachHealthy(
            () => Intfac32Native.Transact("EST?", 80, 2),
            msg => StatusChanged?.Invoke(this, msg),
            "după ACT off",
            out _,
            out _,
            maxReads: 4);

        for (var i = 0; i < _channels.Count; i++)
        {
            if (_digitalChannelIndex == i) continue;
            if (!_channels[i].Enabled) continue;

            Intfac32Native.Write($"ACT{i},1");
            Thread.Sleep(30);
            var bridge = ResolveAcquisitionBridge(_channels[i]);
            if (bridge != _channels[i].Bridge)
            {
                _channels[i].Bridge = bridge;
                StatusChanged?.Invoke(this, $"CH{i}: ASA DcVoltage (P15 / 0…10 V).");
            }

            var rangeElectrical = ResolveProgrammedElectricalFs(_channels[i], bridge);
            var asaType = BridgeToAsa(bridge);
            var asaRange = RangeToAsa(bridge, rangeElectrical);
            Intfac32Native.Write($"ASA{i},{asaType},{asaRange}");
            Thread.Sleep(30);
            var programmedFs = ProgrammedFsFromAsaCode(asaRange, rangeElectrical);
            if (i < _asaRangeMvPerV.Length)
                _asaRangeMvPerV[i] = programmedFs;
            _channels[i].RangeMvPerV = programmedFs;

            // Excitation SoftSetup (bridge only) — CHnEXv dialect.
            if (bridge is not BridgeType.DcVoltage and not BridgeType.None and not BridgeType.Potentiometric)
            {
                var exc = _channels[i].ExcitationV;
                if (exc <= 0 || exc > 12.5) exc = 2.5;
                try
                {
                    Intfac32Native.Write($"CH{i + 1}EX{exc.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
                    Thread.Sleep(15);
                }
                catch { /* firmware may ignore */ }
            }

            _activeAnalogCount++;
            if (_activeAnalogCount >= 8) break;
        }

        if (_activeAnalogCount == 0)
        {
            Intfac32Native.Write("ACT0,1");
            Thread.Sleep(30);
            Intfac32Native.Write($"ASA0,{IdsHaBr},{Ids3mV}");
            Thread.Sleep(30);
            _asaRangeMvPerV[0] = 3.0;
            _channels[0].RangeMvPerV = 3.0;
            // Keep HW streaming even if UI left all Off — mark CH0 On so EmitActive + Engine apply Scale.
            _channels[0].Enabled = true;
            _activeAnalogCount = 1;
            StatusChanged?.Invoke(this, "Niciun canal On — forțez CH0 half-bridge.");
        }

        // ICR + ASF (same SoftSetup order as DEST / HBM help) — improves rate/filter parity with Easy.
        var filterHz = _channels.Where(c => c.Enabled).Select(c => c.FilterHz).DefaultIfEmpty(5).Max();
        try
        {
            Intfac32Native.Write($"ICR{RateToIcr(SampleRateHz)}");
            Thread.Sleep(25);
            Intfac32Native.Write($"ASF{142},{FilterToAsf(filterHz)}");
            Thread.Sleep(25);
        }
        catch { /* optional on some Intfac dialects */ }

        Thread.Sleep(40);
        // SoftSetup may leave a one-shot 10005 — clear via EST?; do not abort Start if amp was set
        // (same DEST policy: prefer MSV so P15/DcVoltage still streams).
        if (!Spider8Est.TryReachHealthy(
                () => Intfac32Native.Transact("EST?", 120, 3),
                msg => StatusChanged?.Invoke(this, msg),
                "după setup Intfac",
                out var code,
                out _))
        {
            StatusChanged?.Invoke(this,
                "Intfac SoftSetup: EST sticky — pornesc MSV oricum (power-cycle dacă LED rămâne).");
            _deviceInError = false;
            return _activeAnalogCount > 0;
        }

        if (code is not null and not 0)
            StatusChanged?.Invoke(this, Spider8Est.UiLedMessage(code, samplesOk: false));

        _deviceInError = false;
        return true;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var lastKick = Environment.TickCount64;
        var lastOmb = Environment.TickCount64;
        var lastDestPoll = Environment.TickCount64;
        // Match DEST: OMB?0 is the live path (MSV fills buffer; ASCII push is rare on Spider8).
        var interval = Math.Clamp(1000 / Math.Max(1, SampleRateHz), 8, 40);
        _lastValidFrameTick = Environment.TickCount64;
        while (!ct.IsCancellationRequested && _portOpen
               && Volatile.Read(ref _connectionLostRaised) == 0)
        {
            try
            {
                if (Environment.TickCount64 - lastDestPoll >= HbmUsbSpider8Adapter.DestPresencePollMs)
                {
                    lastDestPoll = Environment.TickCount64;
                    if (!HbmUsbDeviceScanner.IsDestInterfacePresent(_serialHint))
                    {
                        DeclareConnectionLost("DEST USB absent");
                        break;
                    }
                }

                if (_framesEmitted == 0 && _emptyReads >= 60
                    && Environment.TickCount64 - lastKick > 3000 && _measureArmed)
                {
                    try { Intfac32Native.Write("MSV0,1,6100,1"); }
                    catch
                    {
                        _consecutiveHardUsbErrors++;
                    }
                    lastKick = Environment.TickCount64;
                    _emptyReads = 0;
                }

                if (Environment.TickCount64 - lastOmb >= interval)
                {
                    lastOmb = Environment.TickCount64;
                    try { Intfac32Native.Write("OMB?0"); }
                    catch
                    {
                        _consecutiveHardUsbErrors++;
                        if (_consecutiveHardUsbErrors >= HbmUsbSpider8Adapter.LostCommunicationHardUsbErrors)
                        {
                            DeclareConnectionLost("scriere Intfac eșuată");
                            break;
                        }
                    }
                }

                var chunk = Intfac32Native.Read(4096);
                if (!string.IsNullOrEmpty(chunk))
                {
                    _emptyReads = 0;
                    TryParseIncoming(chunk);
                }
                else
                {
                    _emptyReads++;
                    if (_framesEmitted == 0 && _emptyReads == 40)
                        StatusChanged?.Invoke(this,
                            "Intfac: fără date după Start — On/Graf, ASA (Stop→Start după Apply), catman închis.");
                    await Task.Delay(Math.Max(4, interval / 2), ct);
                }

                if (State == DeviceConnectionState.Streaming
                    && Environment.TickCount64 - _lastValidFrameTick >= HbmUsbSpider8Adapter.LostCommunicationSilenceMs)
                {
                    DeclareConnectionLost($"fără cadru OMB {HbmUsbSpider8Adapter.LostCommunicationSilenceMs / 1000}s");
                    break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _readErrors++;
                _consecutiveHardUsbErrors++;
                if (_readErrors == 1 || _readErrors % 20 == 0)
                    StatusChanged?.Invoke(this, $"Intfac read ({_readErrors}x): " + ex.Message);
                if (_consecutiveHardUsbErrors >= HbmUsbSpider8Adapter.LostCommunicationHardUsbErrors)
                {
                    DeclareConnectionLost("erori Intfac consecutive");
                    break;
                }
                try { await Task.Delay(30, ct); } catch { break; }
            }
        }

        if (!ct.IsCancellationRequested && Volatile.Read(ref _connectionLostRaised) == 0)
            DeclareConnectionLost("Intfac închis / fără date în streaming");
    }

    private void StartLinkMonitor()
    {
        if (Volatile.Read(ref _connectionLostRaised) != 0) return;
        if (!_portOpen) return;
        if (State != DeviceConnectionState.Connected) return;
        StopLinkMonitor();
        _linkCts = new CancellationTokenSource();
        _linkLoop = Task.Run(() => LinkMonitorLoopAsync(_linkCts.Token));
    }

    private void StopLinkMonitor()
    {
        try { _linkCts?.Cancel(); } catch { /* ignore */ }
        try { _linkCts?.Dispose(); } catch { /* ignore */ }
        _linkCts = null;
        _linkLoop = null;
    }

    private async Task LinkMonitorLoopAsync(CancellationToken ct)
    {
        var fails = 0;
        var startedMs = Environment.TickCount64;
        while (!ct.IsCancellationRequested
               && State == DeviceConnectionState.Connected
               && _portOpen
               && Volatile.Read(ref _connectionLostRaised) == 0)
        {
            try
            {
                await Task.Delay(HbmUsbSpider8Adapter.DestPresencePollMs, ct);
                if (State != DeviceConnectionState.Connected) break;
                if (!HbmUsbDeviceScanner.IsDestInterfacePresent(_serialHint))
                {
                    DeclareConnectionLost("DEST USB absent");
                    break;
                }

                if (Environment.TickCount64 - startedMs < 4000)
                    continue;

                if (!TryIdleLinkProbe())
                {
                    fails++;
                    if (fails >= HbmUsbSpider8Adapter.IdleLinkFailCount)
                    {
                        DeclareConnectionLost("USB deschis dar fără răspuns Spider8");
                        break;
                    }
                }
                else
                    fails = 0;
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                fails++;
                if (fails >= HbmUsbSpider8Adapter.IdleLinkFailCount)
                {
                    DeclareConnectionLost("USB deschis dar fără răspuns Spider8");
                    break;
                }
            }
        }
    }

    private bool TryIdleLinkProbe()
    {
        if (!_portOpen) return false;
        try
        {
            var est = Intfac32Native.Transact("EST?", 120, 2);
            return !string.IsNullOrWhiteSpace(est);
        }
        catch
        {
            return false;
        }
    }

    private void DeclareConnectionLost(string reason)
    {
        if (Interlocked.Exchange(ref _connectionLostRaised, 1) != 0) return;
        State = DeviceConnectionState.Error;
        try { _linkCts?.Cancel(); } catch { /* ignore */ }
        try { _cts?.Cancel(); } catch { /* ignore */ }
        try { SafeClosePort(); } catch { /* ignore */ }
        var msg = "Comunicare pierdută — " + reason;
        try { StatusChanged?.Invoke(this, msg); } catch { /* ignore */ }
        try { ConnectionLost?.Invoke(this, EventArgs.Empty); } catch { /* ignore */ }
    }

    /// <summary>
    /// Primary: OMB binary (#0 + int16 LE) stuffed through ReadPort as Latin1.
    /// Fallback: ASCII measurement lines (rare on this unit).
    /// </summary>
    private void TryParseIncoming(string chunk)
    {
        if (TryParseOmbBinaryFromAsciiBlob(chunk))
            return;

        lock (_rxLock)
        {
            _rx.Append(chunk);
            TryConsumeRx();
        }
    }

    /// <summary>True when an OMB frame was decoded and emitted (full row for active ACT mask).</summary>
    private bool TryParseOmbBinaryFromAsciiBlob(string? blob)
    {
        if (string.IsNullOrEmpty(blob)) return false;
        var bytes = Encoding.Latin1.GetBytes(blob);
        var start = -1;
        for (var i = 0; i + 1 < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'#' && bytes[i + 1] == (byte)'0')
            {
                start = i + 2;
                break;
            }
        }
        if (start < 0) return false;

        var nActive = Math.Clamp(_activeAnalogCount, 1, 8);
        var need = nActive * 2;
        if (bytes.Length < start + need) return false;

        var nums = new List<double>(nActive);
        var ai = 0;
        for (var i = 0; i < _channels.Count && ai < nActive; i++)
        {
            if (_digitalChannelIndex == i) continue;
            if (!_channels[i].Enabled) continue;
            var off = start + ai * 2;
            if (off + 2 > bytes.Length) break;
            var raw = (short)(bytes[off] | (bytes[off + 1] << 8));
            var range = i < _asaRangeMvPerV.Length && _asaRangeMvPerV[i] > 0
                ? _asaRangeMvPerV[i]
                : OmbFallbackRange(_channels[i]);
            nums.Add(range * raw / SpiderDigitsFullScale);
            ai++;
        }
        if (nums.Count == 0) return false;
        EmitActive(nums);
        return true;
    }

    private void TryConsumeRx()
    {
        while (true)
        {
            var text = _rx.ToString();
            var idx = text.IndexOfAny(['\r', '\n']);
            if (idx < 0)
            {
                if (_rx.Length > 8192) _rx.Clear();
                // Also try space-separated float dump without newline
                if (_rx.Length > 8)
                    TryParseLoose(_rx.ToString());
                return;
            }

            var line = text[..idx].Trim();
            _rx.Remove(0, idx + 1);
            if (line.Length == 0) continue;
            // Binary OMB handled in TryParseOmbBinaryFromAsciiBlob — skip framing leftovers here.
            if (line.StartsWith('#')) continue;
            if (line.StartsWith("HBM,", StringComparison.OrdinalIgnoreCase)) continue;
            ParseAsciiMeasurementLine(line);
        }
    }

    private void TryParseLoose(string text)
    {
        var parts = text.Replace(';', ' ').Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
        var nums = new List<double>();
        foreach (var p in parts)
        {
            if (double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && !double.IsNaN(v) && !double.IsInfinity(v) && v < 10000)
                nums.Add(v);
        }
        if (nums.Count == 0) return;
        _rx.Clear();
        EmitActive(nums);
    }

    private void ParseAsciiMeasurementLine(string line)
    {
        if (line.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            && !line.Contains('E', StringComparison.OrdinalIgnoreCase)
            && !line.Contains('e'))
            return;

        var parts = line.Replace(';', ' ').Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
        var nums = new List<double>();
        foreach (var p in parts)
        {
            if (double.TryParse(p, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && !double.IsNaN(v) && !double.IsInfinity(v))
                nums.Add(v);
        }
        if (nums.Count == 0) return;
        // Never treat Spider EST codes (10000+) as CH readings.
        if (nums.Any(v => v >= 10000 && v < 11000))
            return;
        EmitActive(nums);
    }

    private static double OmbFallbackRange(ChannelConfig ch)
    {
        var bridge = ResolveAcquisitionBridge(ch);
        if (bridge == BridgeType.DcVoltage)
            return ResolveProgrammedElectricalFs(ch, bridge);
        var hint = ch.RangeMvPerV > 0 ? ch.RangeMvPerV : 3.0;
        return hint <= 3.5 ? 3.0 : 12.0;
    }

    private void EmitActive(List<double> activeValues)
    {
        var values = new double[_channels.Count];
        Array.Fill(values, double.NaN);
        var ai = 0;
        for (var i = 0; i < _channels.Count; i++)
        {
            if (_digitalChannelIndex == i) continue;
            if (!_channels[i].Enabled) continue;
            if (ai >= activeValues.Count) break;
            values[i] = activeValues[ai++];
        }

        if (ai == 0 && activeValues.Count > 0)
        {
            for (var i = 0; i < Math.Min(activeValues.Count, values.Length); i++)
            {
                if (_digitalChannelIndex == i) continue;
                values[i] = activeValues[i];
            }
        }

        _lastSampleUtc = DateTime.UtcNow;
        _lastValidFrameTick = Environment.TickCount64;
        _consecutiveHardUsbErrors = 0;
        _framesEmitted++;
        if (values.Length == _lastRawValues.Length)
            Array.Copy(values, _lastRawValues, values.Length);
        SampleReceived?.Invoke(this, new SampleFrame
        {
            Timestamp = _lastSampleUtc.Value,
            Sequence = Interlocked.Increment(ref _sequence),
            Values = values
        });
    }

    private bool EnsureHealthy(string when)
    {
        var ok = Spider8Est.TryReachHealthy(
            () => Intfac32Native.Transact("EST?", 100, 2),
            msg => StatusChanged?.Invoke(this, msg),
            when,
            out _,
            out _);
        _deviceInError = !ok;
        return ok;
    }

    private void TryStopMeasure()
    {
        _measureArmed = false;
        if (!_portOpen) return;
        try { Intfac32Native.Write("STP"); } catch { /* ignore */ }
        Thread.Sleep(20);
    }

    private void SafeClosePort()
    {
        TryStopMeasure();
        Intfac32Native.ReleaseUsbStack(closeLib: false);
        _portOpen = false;
        _openedAs = null;
    }

    private void EnsureDigitalChannelSlot()
    {
        const int di = 8;
        while (_channels.Count <= di)
        {
            var i = _channels.Count;
            _channels.Add(new ChannelConfig
            {
                Index = i,
                Name = $"CH{i}",
                Unit = "mV/V",
                Enabled = false,
                Bridge = BridgeType.None
            });
        }
        _digitalChannelIndex = di;
        _channels[di].Name = "CH8 DI";
        _channels[di].Unit = "bitmask";
        _channels[di].Bridge = BridgeType.None;
        _channels[di].Enabled = false;
    }

    private static bool IsEstError(string? est) => Spider8Est.IsHardError(est);

    private static int BridgeToAsa(BridgeType b) => Spider8MeasuringRange.BridgeToAsaType(b);

    private static int RangeToAsa(BridgeType bridge, double electricalFs)
        => Spider8MeasuringRange.ToAsaRangeCode(bridge, electricalFs);

    private static BridgeType ResolveAcquisitionBridge(ChannelConfig ch)
        => Spider8MeasuringRange.ResolveAcquisitionBridge(ch);

    private static double ResolveProgrammedElectricalFs(ChannelConfig ch, BridgeType bridge)
        => Spider8MeasuringRange.ResolveProgrammedElectricalFs(ch, bridge);

    private static double ProgrammedFsFromAsaCode(int asaRange, double fallback)
        => Spider8MeasuringRange.ProgrammedFsFromAsaCode(asaRange, fallback);

    private static int FilterToAsf(double hz)
    {
        if (hz <= 2.5) return 932;
        if (hz <= 5) return 935;
        if (hz <= 10) return 940;
        if (hz <= 20) return 945;
        if (hz <= 40) return 948;
        return 940;
    }

    private static int RateToIcr(int hz) => hz switch
    {
        <= 1 => 6300,
        <= 2 => 6301,
        <= 5 => 6302,
        <= 10 => 6303,
        <= 25 => 6304,
        <= 50 => 6305,
        <= 60 => 6306,
        <= 75 => 6307,
        <= 100 => 6308,
        _ => 6305
    };

    private static string CleanAscii(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            if ((ch >= 32 && ch <= 126) || ch is '\r' or '\n' or '\t')
                sb.Append(ch);
        }
        return sb.ToString().Trim();
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "...";

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
