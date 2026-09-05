using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Hardware;

/// <summary>
/// Spider8 over raw usbhbm.sys DEST pipes (Intfac OpenPort PORT_USB often returns -1 on this PC).
/// Connect = open + EST? reset-ack. Start SoftSetup (HBM ASCII):
///   ACT off unused → ACT+ASA+EXC per On CH → ICR → ASF142,{fc} → MSV0,1,6100,1 → poll OMB?0
/// Excitation: CHnEXv (bridge only; skipped for DcVoltage / P15).
/// Shunt check: pause OMB poller → ASSn,43 (HBM ASS.htm, same 0-based n as ACT/ASA/OMB) → wait + OMB/live raw → ASSn,42.
/// Bad cmds (EST 10005): ASF0,935 (missing filter char), bare MSV, ACT0,6201 on this unit.
/// EST 10000 = reset ACK (not hard). Hard errors 10001–10020 → Power-cycle Spider8 — LED ERROR.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HbmUsbSpider8Adapter : ISpider8Device, IDeviceHealth, IDigitalIoChannel
{
    // IDS_* from S8_dll.h / S32_dll.inc (catman / Spider32)
    private const int IdsVoBr = 350;      // full bridge
    private const int IdsHaBr = 351;      // half bridge
    private const int IdsViertelBr = 352; // quarter bridge
    private const int IdsDcVo = 420;
    private const int Ids3mV = 700;
    private const int Ids12mV = 701;
    private const int IdsSofort = 6100;   // MSV immediate
    // ACT0,6201 (IdsMeAkt) latches EST 10005 / ParOutL on this unit — never send.
    private const int IdsF005Hz = 935;
    private const int IdsF010Hz = 941;
    private const int IdsM50Hz = 6305;
    private const int IdsFilterBestTime = 142; // ASF characteristic (not channel!)
    private const int SpiderDigitsFullScale = 25000;

    private readonly string? _serialHint;
    private readonly List<ChannelConfig> _channels;
    private HbmUsbIo.SafeHbmUsbHandle? _usb;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private CancellationTokenSource? _linkCts;
    private Task? _linkLoop;
    private long _sequence;
    private DateTime? _lastSampleUtc;
    private string? _lastIdn;
    private long _readErrors;
    private long _emptyReads;
    private long _framesEmitted;
    private readonly StringBuilder _rx = new();
    private readonly object _rxLock = new();
    private int? _digitalChannelIndex;
    private int _activeAnalogCount = 1;
    private bool _deviceInError;
    private int? _lastEstCode;
    private long _lastEstClearTick;
    /// <summary>ASA measuring range actually sent (3 or 12 mV/V) — OMB ElectrValue must use this, not sensor JSON 2.</summary>
    private readonly double[] _asaRangeMvPerV = Enumerable.Repeat(3.0, 8).ToArray();
    private readonly double[] _lastRawValues = Enumerable.Repeat(double.NaN, 10).ToArray();
    private volatile bool _shuntBusy;
    private int _connectionLostRaised;
    private long _lastValidFrameTick;
    private int _consecutiveHardUsbErrors;

    public HbmUsbSpider8Adapter(string? serialHint, int channelCount = 8)
    {
        _serialHint = serialHint;
        var n = Math.Clamp(channelCount, 8, 10);
        _channels = Enumerable.Range(0, n)
            .Select(i => new ChannelConfig { Index = i, Name = $"CH{i}", Unit = "mV/V", Enabled = i == 0 })
            .ToList();
    }

    public string DisplayName
    {
        get
        {
            var s = HbmUsbIo.NormalizeSerial(_serialHint);
            return string.IsNullOrEmpty(s) ? "Spider8 [HBM USB]" : $"Spider8 [USB {s}]";
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

    /// <summary>Silence window before lost-comm (OMB can miss a packet at 50 Hz — do not trip on one timeout).</summary>
    internal const int LostCommunicationSilenceMs = 3000;
    internal const int LostCommunicationHardUsbErrors = 3;
    internal const int DestPresencePollMs = 1500;
    internal const int IdleLinkFailCount = 2;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = DeviceConnectionState.Connecting;
        _connectionLostRaised = 0;
        StatusChanged?.Invoke(this, "Deschid USBHBM DEST (fallback — preferați Intfac32)...");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            _usb = HbmUsbIo.Open(_serialHint);
            StatusChanged?.Invoke(this, $"USB deschis: {_usb.Path}");

            // FIRST command after open must be EST? (reset status 10000). No STP/DCL/ACT here.
            if (!AcknowledgeReset("la Connect"))
            {
                QueryIdnBestEffort();
                State = DeviceConnectionState.Connected;
                EnsureDigitalChannelSlot();
                StatusChanged?.Invoke(this,
                    "Power-cycle Spider8 — LED ERROR (EST hard la Connect).");
                StartLinkMonitor();
                return Task.CompletedTask;
            }

            QueryIdnBestEffort();
            State = DeviceConnectionState.Connected;
            _deviceInError = false;
            EnsureDigitalChannelSlot();
            var label = HbmUsbIo.NormalizeSerial(_serialHint);
            if (string.IsNullOrEmpty(label)) label = "USBHBM";
            StatusChanged?.Invoke(this,
                $"Conectat DEST ({label}). Start = SoftSetup CH0 half-bridge + MSV.");
            StartLinkMonitor();
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _usb?.Dispose();
            _usb = null;
            State = DeviceConnectionState.Error;
            var msg = "Connect HBM USB eșuat: " + ex.Message;
            StatusChanged?.Invoke(this, msg);
            throw new InvalidOperationException(msg, ex);
        }
    }

    public async Task DisconnectAsync()
    {
        StopLinkMonitor();
        await StopStreamingAsync();
        StopLinkMonitor();
        // No STP/DCL on DEST disconnect — writes latch ERROR on this unit.
        _usb?.Dispose();
        _usb = null;
        State = DeviceConnectionState.Disconnected;
        if (Volatile.Read(ref _connectionLostRaised) == 0)
            StatusChanged?.Invoke(this, "HBM USB deconectat.");
    }

    public Task StartStreamingAsync(CancellationToken cancellationToken = default)
    {
        if (_usb is not { IsOpen: true })
            throw new InvalidOperationException("Nu sunteți conectat la USBHBM.");

        StopLinkMonitor();

        // Re-probe EST? even if SoftSetup previously set _deviceInError — one-shot hard often clears.
        if (!EnsureDeviceHealthy("înainte de Start"))
        {
            State = DeviceConnectionState.Connected;
            StatusChanged?.Invoke(this, "Power-cycle Spider8 — LED ERROR");
            StartLinkMonitor();
            throw new InvalidOperationException("Power-cycle Spider8 — LED ERROR");
        }

        // Exact SoftSetup path that yielded CH0≈0.339 (DestMeasure2 / HybridSmoke).
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
            // Continuous + immediate: MSV0,1,6100,1 (HBM help). Bare "MSV" → EST 10005.
            WriteCmd($"MSV0,1,{IdsSofort},1", settleMs: 30);
            // HBM: EST? clears stored error + red LED without STP. Safe while MSV runs.
            ClearErrorLed("după MSV", allowHardContinue: true);
            State = DeviceConnectionState.Streaming;
            var estHint = _lastEstCode is int ec
                ? Spider8Est.UiLedMessage(ec, samplesOk: true)
                : $"Streaming DEST live: {_activeAnalogCount} canale @ {SampleRateHz} Hz (OMB?0).";
            StatusChanged?.Invoke(this, estHint.StartsWith("EST=", StringComparison.Ordinal)
                ? $"Streaming DEST live @ {SampleRateHz} Hz · {estHint}"
                : estHint);
        }
        catch (Exception ex)
        {
            _cts.Dispose();
            _cts = null;
            State = DeviceConnectionState.Connected;
            StatusChanged?.Invoke(this, "Setup MSV: " + ex.Message);
            StartLinkMonitor();
            throw;
        }

        _loop = Task.Run(() => PollLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopStreamingAsync()
    {
        // STP only when stopping an armed measure — never SoftClear before SoftSetup
        // (STP on idle unit can surface EST 10005 / ParOutL).
        try
        {
            if (_usb is { IsOpen: true } && State == DeviceConnectionState.Streaming)
                WriteCmd("STP", settleMs: 10);
        }
        catch { /* ignore */ }

        if (_cts is null)
        {
            if (State == DeviceConnectionState.Streaming)
                State = DeviceConnectionState.Connected;
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
                    ? $"Streaming HBM USB oprit ({_framesEmitted} cadre)."
                    : "Streaming HBM USB oprit (0 cadre — verificați ACT/ASA și că catman e închis).");
            if (_usb is { IsOpen: true } && State == DeviceConnectionState.Connected)
                StartLinkMonitor();
        }
    }

    public Task TareAsync(int? channelIndex = null)
    {
        EnsureUsb();
        if (channelIndex is int idx)
        {
            try { _usb!.Write(Encoding.ASCII.GetBytes($"TAV{idx},0.00000\n"), 500); } catch { /* ignore */ }
            try { _usb!.Write(Encoding.ASCII.GetBytes($"TAR{idx + 1}\n"), 500); } catch { /* ignore */ }
        }
        else
        {
            for (var i = 0; i < _channels.Count; i++)
            {
                if (_digitalChannelIndex == i) continue;
                try { _usb!.Write(Encoding.ASCII.GetBytes($"TAV{i},0.00000\n"), 400); } catch { /* ignore */ }
            }
            try { _usb!.Write(Encoding.ASCII.GetBytes("TAR\n"), 500); } catch { /* ignore */ }
        }
        StatusChanged?.Invoke(this, "Tare trimis pe USBHBM.");
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

        if (_usb is not { IsOpen: true })
        {
            StatusChanged?.Invoke(this, "Config canale stocată local (USB închis).");
            return Task.CompletedTask;
        }

        // Don't push ACT/ASA here — SoftSetup runs once at Start (avoids double latency + ERROR risk).
        var analog = _channels.Where(c => c.Enabled && _digitalChannelIndex != c.Index).ToList();
        var half = analog.Where(c => c.Bridge is BridgeType.Half or BridgeType.Quarter or BridgeType.Full).Select(c => c.Index);
        var dc = analog.Where(c => c.Bridge == BridgeType.DcVoltage).Select(c => c.Index);
        var n = analog.Count;
        var mode = dc.Any()
            ? $"half [{string.Join(",", half)}] · dc [{string.Join(",", dc)}]"
            : $"half [{string.Join(",", half)}]";
        StatusChanged?.Invoke(this,
            $"Config stocată: {mode} · active={n} (ASA/ACT la Start — reporniți dacă schimbați Bridge).");
        return Task.CompletedTask;
    }

    public Task<double> ShuntCheckAsync(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= _channels.Count)
            return Task.FromResult(double.NaN);
        // CH8 DI has no analog shunt (ACT.htm channels 8, 9, 18…).
        if (_digitalChannelIndex == channelIndex)
            return Task.FromResult(double.NaN);

        // HBM ASS.htm: channel 0…100 (same n as ACT/ASA), 43=on / 42=off. Not CHnSH (1-based).
        var n = channelIndex;
        double reading = double.NaN;

        if (_usb is { IsOpen: true })
        {
            _shuntBusy = true;
            try
            {
                // Pause the live OMB?0 poller so this snapshot is not stolen.
                Thread.Sleep(30);
                WriteCmd($"ASS{n},43", settleMs: 40);
                Thread.Sleep(280);
                for (var attempt = 0; attempt < 5 && double.IsNaN(reading); attempt++)
                {
                    reading = TrySnapshotChannelElectrical(channelIndex);
                    if (double.IsNaN(reading))
                        Thread.Sleep(80);
                }

                // Streaming frames keep electrical raw while shunt is on.
                if (double.IsNaN(reading)
                    && channelIndex < _lastRawValues.Length
                    && !double.IsNaN(_lastRawValues[channelIndex])
                    && Math.Abs(_lastRawValues[channelIndex]) > 1e-9)
                    reading = _lastRawValues[channelIndex];

                WriteCmd($"ASS{n},42", settleMs: 40);
                if (double.IsNaN(reading))
                {
                    Thread.Sleep(60);
                    reading = TrySnapshotChannelElectrical(channelIndex);
                    StatusChanged?.Invoke(this,
                        $"Shunt CH{channelIndex}: cmd ASS{n},43/42 OK, OMB fără valoare — Start (nu Stop) până se mișcă Citirea.");
                }
                else
                {
                    StatusChanged?.Invoke(this,
                        $"Shunt CH{channelIndex}: {reading:G6} (electric OMB/live cu ASS{n},43).");
                }
            }
            catch (Exception ex)
            {
                try { WriteCmd($"ASS{n},42", settleMs: 20); } catch { /* ignore */ }
                StatusChanged?.Invoke(this, $"Shunt CH{channelIndex} eșuat: " + Trunc(ex.Message, 80));
            }
            finally
            {
                _shuntBusy = false;
            }
        }

        _channels[channelIndex].ShuntEnabled = true;
        if (!double.IsNaN(reading))
            _channels[channelIndex].LastShuntReading = reading;
        return Task.FromResult(reading);
    }

    /// <summary>One-shot OMB?0 → electrical value for HW channel index (among Enabled ACT mask).</summary>
    private double TrySnapshotChannelElectrical(int channelIndex)
    {
        if (_usb is not { IsOpen: true }) return double.NaN;
        if (channelIndex < 0 || channelIndex >= _channels.Count) return double.NaN;
        if (!_channels[channelIndex].Enabled) return double.NaN;

        DrainInput(30);
        WriteCmd("OMB?0", settleMs: 5);
        Thread.Sleep(35);
        var buf = new byte[256];
        int n;
        try { n = _usb.Read(buf, 120); }
        catch { return double.NaN; }
        if (n <= 0) return double.NaN;

        var data = buf.AsSpan(0, n);
        var start = -1;
        for (var i = 0; i + 1 < data.Length; i++)
        {
            if (data[i] == (byte)'#' && data[i + 1] == (byte)'0')
            {
                start = i + 2;
                break;
            }
        }
        if (start < 0) return double.NaN;

        // Position among active analogs matches SoftSetup ACT order.
        var ai = 0;
        for (var i = 0; i < _channels.Count; i++)
        {
            if (_digitalChannelIndex == i) continue;
            if (!_channels[i].Enabled) continue;
            var off = start + ai * 2;
            if (off + 2 > data.Length) return double.NaN;
            if (i == channelIndex)
            {
                var raw = (short)(data[off] | (data[off + 1] << 8));
                var range = i < _asaRangeMvPerV.Length && _asaRangeMvPerV[i] > 0
                    ? _asaRangeMvPerV[i]
                    : OmbFallbackRange(_channels[i]);
                return range * raw / SpiderDigitsFullScale;
            }
            ai++;
        }
        return double.NaN;
    }

    private void TrySendExcitation(int channelIndex0, BridgeType bridge, double excitationV)
    {
        // DcVoltage / process transducers: no bridge EXC (often ExcitationV=0 in catalog).
        if (bridge is BridgeType.DcVoltage or BridgeType.None or BridgeType.Potentiometric)
            return;
        if (excitationV <= 0 || excitationV > 12.5)
            excitationV = 2.5;
        var ch1 = channelIndex0 + 1;
        var v = excitationV.ToString("0.###", CultureInfo.InvariantCulture);
        // Same dialect as SerialProtocolAdapter: CHnEXv
        WriteCmd($"CH{ch1}EX{v}", settleMs: 15);
    }

    public Task<DeviceHealthReport> SelfTestAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<string>();
        var open = _usb is { IsOpen: true };
        checks.Add(open ? "USBHBM DEST open: OK" : "USBHBM DEST open: FAIL");
        if (!open)
        {
            return Task.FromResult(new DeviceHealthReport
            {
                Status = HealthStatus.Offline,
                Summary = "USB închis",
                PortOpen = false,
                Checks = checks,
                LastSampleUtc = _lastSampleUtc
            });
        }

        try
        {
            var est = _usb!.TransactAscii("EST?", 1000);
            checks.Add(string.IsNullOrWhiteSpace(est) ? "EST?: empty" : "EST?: " + Trunc(est, 60));
        }
        catch (Exception ex)
        {
            checks.Add("EST? error: " + ex.Message);
        }

        checks.Add($"Frames emitted: {_framesEmitted}");
        var age = _lastSampleUtc is DateTime t ? (DateTime.UtcNow - t).TotalSeconds : (double?)null;
        var status = !open ? HealthStatus.Offline
            : age is > 3 or null ? HealthStatus.Degraded
            : HealthStatus.Healthy;
        if (_readErrors > 20) status = HealthStatus.Degraded;

        return Task.FromResult(new DeviceHealthReport
        {
            Status = status,
            Summary = $"HBM USB: {status}",
            PortOpen = true,
            Checks = checks,
            LastSampleUtc = _lastSampleUtc,
            SecondsSinceLastSample = age,
            FirmwareHint = _lastIdn
        });
    }

    /// <summary>IDN? after EST? — model string only; firmware does not report Rsh ohms.</summary>
    private void QueryIdnBestEffort()
    {
        if (_usb is not { IsOpen: true }) return;
        try
        {
            var raw = CleanAsciiProbe(_usb.TransactAscii("IDN?", 400));
            if (string.IsNullOrWhiteSpace(raw) || raw == "?" || raw.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                return;
            _lastIdn = raw.Trim().Trim('"');
            StatusChanged?.Invoke(this, "IDN: " + Trunc(_lastIdn, 80));
        }
        catch
        {
            /* optional — model table needs IDN when present */
        }
    }

    /// <summary>Stop measure only — DCL SoftClear has latched ERROR LED; avoid it.</summary>
    private void SoftClearDevice()
    {
        if (_usb is not { IsOpen: true }) return;
        try { WriteCmd("STP", settleMs: 10); } catch { /* ignore */ }
        Thread.Sleep(15);
    }

    /// <summary>
    /// After power-on / USB open, EST? must be first. 10000 = reset ACK (clears LED).
    /// Hard 10001–10020 that persist across consecutive EST? → power-cycle.
    /// A single hard reply that clears on the next EST? is recoverable (SoftSetup/ACT glitch).
    /// </summary>
    private bool AcknowledgeReset(string when)
    {
        if (_usb is not { IsOpen: true }) return false;
        DrainInput(40);
        var ok = Spider8Est.TryReachHealthy(
            () => _usb!.TransactAscii("EST?", 700),
            msg => StatusChanged?.Invoke(this, msg),
            when,
            out var code,
            out _);
        _lastEstCode = code;
        _lastEstClearTick = Environment.TickCount64;
        _deviceInError = !ok;
        return ok;
    }

    private bool EnsureDeviceHealthy(string when) => AcknowledgeReset(when);

    private bool EstCheckAfter(string cmd)
    {
        // Drain leftovers from ACT/ASA before EST? — stale "10005" bytes otherwise look sticky
        // and abort SoftSetup (silences P15/DcVoltage even when the amp was programmed).
        DrainInput(40);
        var ok = Spider8Est.TryReachHealthy(
            () =>
            {
                DrainInput(15);
                return _usb!.TransactAscii("EST?", 600);
            },
            msg => StatusChanged?.Invoke(this, msg),
            "după " + cmd,
            out var code,
            out _,
            maxReads: 5);
        _lastEstCode = code;
        _lastEstClearTick = Environment.TickCount64;
        if (!ok)
            _deviceInError = true;
        return ok;
    }

    /// <summary>
    /// SoftSetup (HBM command help): ACT + ASA per active CH, then ICR, then ASF142,fc
    /// (ASF char=140/141/142 + cutoff — NOT ASF{ch},{fc} which is EST 10005).
    /// Measure arming is MSV0,n,6100,1 in StartStreaming (never bare MSV).
    /// </summary>
    private bool SoftSetupAcquisition()
    {
        if (_usb is not { IsOpen: true }) return false;

        if (!AcknowledgeReset("înainte SoftSetup"))
            return false;

        _activeAnalogCount = 0;
        var filterHz = 5.0;
        Array.Clear(_asaRangeMvPerV);

        // Align HW ACT mask with Enabled. Leftover ACT=1 on unused slots prepends values in OMB
        // and swaps channels (pressure→force crosstalk). Send ACT off WITHOUT EST? after each
        // (EST storms latch ERROR and kill DcVoltage / P15).
        for (var i = 0; i < _channels.Count; i++)
        {
            if (_digitalChannelIndex == i) continue;
            if (_channels[i].Enabled) continue;
            WriteCmd($"ACT{i},0", settleMs: 8);
        }
        DrainInput(50);
        ClearErrorLed("după ACT off", allowHardContinue: true);

        // Single SoftSetup pass — never double ASF/ACT (latches ERROR on this unit).
        for (var i = 0; i < _channels.Count; i++)
        {
            if (_digitalChannelIndex == i) continue;
            if (!_channels[i].Enabled) continue;

            WriteCmd($"ACT{i},1", settleMs: 20);
            // Do not abort SoftSetup on one-shot EST after ACT — drain+retry; keep going for P15.
            if (!EstCheckAfter($"ACT{i},1"))
            {
                ClearErrorLed($"ACT{i},1 (retry)", allowHardContinue: true);
                if (_deviceInError)
                    StatusChanged?.Invoke(this, $"CH{i}: EST după ACT — continui SoftSetup (P15/DC).");
            }

            var bridge = ResolveAcquisitionBridge(_channels[i]);
            if (bridge != _channels[i].Bridge)
            {
                _channels[i].Bridge = bridge;
                StatusChanged?.Invoke(this,
                    $"CH{i}: ASA DcVoltage (P15 / 0…10 V) — nu bridge strain.");
            }

            var rangeElectrical = ResolveProgrammedElectricalFs(_channels[i], bridge);
            var asaType = BridgeToAsa(bridge);
            var asaRange = RangeToAsa(bridge, rangeElectrical);
            if (i < _asaRangeMvPerV.Length)
                _asaRangeMvPerV[i] = ProgrammedFsFromAsaCode(asaRange, rangeElectrical);
            WriteCmd($"ASA{i},{asaType},{asaRange}", settleMs: 20);
            if (!EstCheckAfter($"ASA{i}"))
            {
                // ASA may have applied; sticky false-positive must not silence DcVoltage/P15.
                ClearErrorLed($"ASA{i} (retry)", allowHardContinue: true);
                StatusChanged?.Invoke(this,
                    bridge == BridgeType.DcVoltage
                        ? $"CH{i}: ASA DcVoltage trimis (EST noisy) — continui stream."
                        : $"CH{i}: ASA trimis (EST noisy) — continui SoftSetup.");
            }

            _channels[i].RangeMvPerV = _asaRangeMvPerV[i];

            // Excitation SoftSetup (Serial dialect CHnEXv) — skip DC amp / 0 V process transducers.
            TrySendExcitation(i, bridge, _channels[i].ExcitationV);

            filterHz = _channels[i].FilterHz;
            _activeAnalogCount++;
            if (_activeAnalogCount >= 8) break;
        }

        if (_activeAnalogCount == 0)
        {
            WriteCmd("ACT0,1", settleMs: 20);
            _ = EstCheckAfter("ACT0,1");
            WriteCmd($"ASA0,{IdsHaBr},{Ids3mV}", settleMs: 20);
            _ = EstCheckAfter("ASA0");
            _asaRangeMvPerV[0] = 3.0;
            _channels[0].RangeMvPerV = 3.0;
            _channels[0].Enabled = true;
            _activeAnalogCount = 1;
            filterHz = 5;
            StatusChanged?.Invoke(this, "Niciun canal On — forțez CH0 half-bridge (catman TEN default).");
        }

        // ICR before ASF (ASF influenced by ICR per HBM help).
        WriteCmd($"ICR{RateToIcr(SampleRateHz)}", settleMs: 25);
        _ = EstCheckAfter("ICR");

        WriteCmd($"ASF{IdsFilterBestTime},{FilterToAsf(filterHz)}", settleMs: 25);
        _ = EstCheckAfter("ASF");
        Thread.Sleep(40);

        // Prefer live MSV over abort: one-shot hard after SoftSetup often clears mid-stream.
        ClearErrorLed("după SoftSetup", allowHardContinue: true);
        if (_deviceInError)
        {
            StatusChanged?.Invoke(this,
                "SoftSetup: EST sticky după ACT/ASA — pornesc MSV oricum (power-cycle dacă LED rămâne).");
            _deviceInError = false;
        }

        return true;
    }

    /// <summary>
    /// HBM EST.htm: reading EST? clears the last error and turns the red ERROR LED off.
    /// Does not send STP/DCL — MSV / OMB keep running. Safe mid-stream.
    /// Stuck hard (2× consecutive) → _deviceInError unless allowHardContinue (samples may still flow).
    /// </summary>
    private int? ClearErrorLed(string when, bool allowHardContinue)
    {
        if (_usb is not { IsOpen: true }) return null;

        var ok = Spider8Est.TryReachHealthy(
            () =>
            {
                DrainInput(30);
                return _usb!.TransactAscii("EST?", 500);
            },
            msg => StatusChanged?.Invoke(this, msg),
            when,
            out var code,
            out var recovered,
            maxReads: 4);

        _lastEstCode = code;
        _lastEstClearTick = Environment.TickCount64;

        if (!ok)
        {
            if (allowHardContinue)
            {
                // Keep streaming — user case: LED red briefly, date OK / P15 SoftSetup.
                _deviceInError = false;
                StatusChanged?.Invoke(this, Spider8Est.UiLedMessage(code, samplesOk: true));
                return code;
            }

            _deviceInError = true;
            return code;
        }

        _deviceInError = false;
        if (code is 0 || code is null)
        {
            if (when.Contains("MSV", StringComparison.OrdinalIgnoreCase)
                || when.Contains("stream", StringComparison.OrdinalIgnoreCase)
                || recovered)
                StatusChanged?.Invoke(this, Spider8Est.UiLedMessage(0, samplesOk: _framesEmitted > 0));
            return 0;
        }

        return code;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var buf = new byte[2048];
        // Faster than sample period floor was 15–100 ms — aim ≤ sample period, min ~8 ms for fluid UI.
        var interval = Math.Clamp(1000 / Math.Max(1, SampleRateHz), 8, 40);
        var lastOmb = Environment.TickCount64;
        var lastEstPoll = Environment.TickCount64;
        var lastDestPoll = Environment.TickCount64;
        _lastValidFrameTick = Environment.TickCount64;
        while (!ct.IsCancellationRequested && _usb is { IsOpen: true }
               && Volatile.Read(ref _connectionLostRaised) == 0)
        {
            try
            {
                if (!_shuntBusy
                    && Environment.TickCount64 - lastDestPoll >= DestPresencePollMs)
                {
                    lastDestPoll = Environment.TickCount64;
                    if (!HbmUsbDeviceScanner.IsDestInterfacePresent(_serialHint))
                    {
                        DeclareConnectionLost("DEST USB absent");
                        break;
                    }
                }

                // OMB?0 = newest row for active channels (HBM binary #0 + int16 LE).
                if (!_shuntBusy && Environment.TickCount64 - lastOmb >= interval)
                {
                    lastOmb = Environment.TickCount64;
                    if (!TryWriteCmd("OMB?0", settleMs: 0))
                    {
                        _consecutiveHardUsbErrors++;
                        if (_consecutiveHardUsbErrors >= LostCommunicationHardUsbErrors)
                        {
                            DeclareConnectionLost("scriere USB eșuată");
                            break;
                        }
                    }
                }

                // Periodic EST? while samples flow — clears sticky ERROR LED without STP.
                if (!_shuntBusy
                    && _framesEmitted > 5
                    && Environment.TickCount64 - lastEstPoll > 2500
                    && Environment.TickCount64 - _lastEstClearTick > 2000)
                {
                    lastEstPoll = Environment.TickCount64;
                    ClearErrorLed("în streaming", allowHardContinue: true);
                }

                var n = _usb.Read(buf, Math.Max(40, interval + 40));
                if (n > 0)
                {
                    _emptyReads = 0;
                    TryParseIncoming(buf.AsSpan(0, n));
                }
                else
                {
                    _emptyReads++;
                    if (_framesEmitted == 0 && _emptyReads == 30)
                        StatusChanged?.Invoke(this,
                            "USB: fără date după Start — verificați On/Graf, ASA DcVoltage (Stop→Start după Apply), catman închis.");
                    await Task.Delay(Math.Max(4, interval / 2), ct);
                }

                if (!_shuntBusy
                    && State == DeviceConnectionState.Streaming
                    && Environment.TickCount64 - _lastValidFrameTick >= LostCommunicationSilenceMs)
                {
                    DeclareConnectionLost($"fără cadru OMB {LostCommunicationSilenceMs / 1000}s");
                    break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _readErrors++;
                _consecutiveHardUsbErrors++;
                if (_readErrors == 1 || _readErrors % 25 == 0)
                    StatusChanged?.Invoke(this, $"USB read ({_readErrors}x): " + ex.Message);
                if (_consecutiveHardUsbErrors >= LostCommunicationHardUsbErrors)
                {
                    DeclareConnectionLost("erori USB consecutive");
                    break;
                }
                try { await Task.Delay(80, ct); } catch { break; }
            }
        }

        if (!ct.IsCancellationRequested && Volatile.Read(ref _connectionLostRaised) == 0)
            DeclareConnectionLost("USB închis / fără date în streaming");
    }

    private void StartLinkMonitor()
    {
        if (Volatile.Read(ref _connectionLostRaised) != 0) return;
        if (_usb is not { IsOpen: true }) return;
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

    /// <summary>
    /// Connected-but-idle: DEST can stay enumerated on USB 5V after the Spider8 box is powered off.
    /// EST?/DEST absence is the only live check until OMB streaming starts.
    /// </summary>
    private async Task LinkMonitorLoopAsync(CancellationToken ct)
    {
        var fails = 0;
        var startedMs = Environment.TickCount64;
        while (!ct.IsCancellationRequested
               && State == DeviceConnectionState.Connected
               && _usb is { IsOpen: true }
               && Volatile.Read(ref _connectionLostRaised) == 0)
        {
            try
            {
                await Task.Delay(DestPresencePollMs, ct);
                if (State != DeviceConnectionState.Connected) break;
                if (!HbmUsbDeviceScanner.IsDestInterfacePresent(_serialHint))
                {
                    DeclareConnectionLost("DEST USB absent");
                    break;
                }

                // Skip EST during Connect+ApplyChannelConfig (EST mid-ACT latches 10005).
                if (Environment.TickCount64 - startedMs < 4000)
                    continue;

                if (!TryIdleLinkProbe())
                {
                    fails++;
                    if (fails >= IdleLinkFailCount)
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
                if (fails >= IdleLinkFailCount)
                {
                    DeclareConnectionLost("USB deschis dar fără răspuns Spider8");
                    break;
                }
            }
        }
    }

    private bool TryIdleLinkProbe()
    {
        if (_usb is not { IsOpen: true }) return false;
        try
        {
            var est = _usb.TransactAscii("EST?", 800);
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
        try { _usb?.Dispose(); } catch { /* ignore */ }
        _usb = null;
        var msg = "Comunicare pierdută — " + reason;
        try { StatusChanged?.Invoke(this, msg); } catch { /* ignore */ }
        try { ConnectionLost?.Invoke(this, EventArgs.Empty); } catch { /* ignore */ }
    }

    private void TryParseIncoming(ReadOnlySpan<byte> data)
    {
        // Prefer OMB binary (#0 + int16 LE per channel) — primary live path.
        if (TryParseOmbBinary(data))
            return;

        lock (_rxLock)
        {
            _rx.Append(Encoding.ASCII.GetString(data));
            TryConsumeRx();
        }

        if (TryParseFloat32Block(data))
            return;

        TryParseInt16Block(data);
    }

    /// <summary>
    /// OMB? response: "#0" + N×int16 LE (signed digits) + CR LF.
    /// ElectrValue = rangeMvPerV * digits / 25000 (HBM converting-binary help).
    /// </summary>
    private bool TryParseOmbBinary(ReadOnlySpan<byte> data)
    {
        var start = -1;
        for (var i = 0; i + 1 < data.Length; i++)
        {
            if (data[i] == (byte)'#' && data[i + 1] == (byte)'0')
            {
                start = i + 2;
                break;
            }
        }
        if (start < 0) return false;

        var nActive = Math.Clamp(_activeAnalogCount, 1, 8);
        var need = nActive * 2;
        if (data.Length < start + need) return false;

        var nums = new List<double>(nActive);
        var ai = 0;
        for (var i = 0; i < _channels.Count && ai < nActive; i++)
        {
            if (_digitalChannelIndex == i) continue;
            if (!_channels[i].Enabled) continue;
            var off = start + ai * 2;
            if (off + 2 > data.Length) break;
            var raw = (short)(data[off] | (data[off + 1] << 8));
            // ElectrValue = ASA_range × digits / 25000 (HBM help) — use programmed ASA, not JSON 2.
            var range = i < _asaRangeMvPerV.Length && _asaRangeMvPerV[i] > 0
                ? _asaRangeMvPerV[i]
                : OmbFallbackRange(_channels[i]);
            nums.Add(range * raw / SpiderDigitsFullScale);
            ai++;
        }
        if (nums.Count == 0) return false;
        // Valid OMB frame — zero volts / 0 bar is a real reading (P15RVA DcVoltage at rest).
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
                return;
            }

            var line = text[..idx].Trim();
            _rx.Remove(0, idx + 1);
            if (line.Length == 0) continue;
            if (line.StartsWith('#'))
            {
                // Binary OMB payload may have been split across reads — try full buffer path only.
                continue;
            }
            if (line.StartsWith("HBM,", StringComparison.OrdinalIgnoreCase)) continue;
            if (line is "?" or "0" && line.Length < 2) continue;
            ParseAsciiMeasurementLine(line);
        }
    }

    private void ParseAsciiMeasurementLine(string line)
    {
        // Skip obvious non-measurement chatter
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
        // Never treat Spider EST error codes (10000+) as CH readings.
        if (nums.Any(v => v >= 10000 && v < 11000))
            return;
        EmitActive(nums);
    }

    private bool TryParseFloat32Block(ReadOnlySpan<byte> data)
    {
        var nActive = Math.Clamp(_activeAnalogCount, 1, 8);
        var need = nActive * 4;
        if (data.Length < need) return false;

        // Skip clearly ASCII payloads
        var ascii = 0;
        for (var i = 0; i < Math.Min(data.Length, 32); i++)
            if (data[i] is >= 32 and <= 126 or 10 or 13) ascii++;
        if (ascii > 20) return false;

        // Find offset where first nActive floats look like plausible bridge readings
        for (var off = 0; off + need <= data.Length && off < 16; off += 2)
        {
            var nums = new List<double>(nActive);
            var ok = true;
            for (var i = 0; i < nActive; i++)
            {
                var f = BitConverter.ToSingle(data.Slice(off + i * 4, 4));
                if (float.IsNaN(f) || float.IsInfinity(f) || Math.Abs(f) > 1e7)
                {
                    ok = false;
                    break;
                }
                nums.Add(f);
            }
            if (!ok) continue;
            EmitActive(nums);
            return true;
        }
        return false;
    }

    private void TryParseInt16Block(ReadOnlySpan<byte> data)
    {
        var nActive = Math.Clamp(_activeAnalogCount, 1, 8);
        if (data.Length < nActive * 2) return;
        var ascii = 0;
        for (var i = 0; i < Math.Min(data.Length, 32); i++)
            if (data[i] is >= 32 and <= 126 or 10 or 13) ascii++;
        if (ascii > 20) return;

        var nums = new List<double>(nActive);
        for (var i = 0; i < nActive; i++)
        {
            var raw = BitConverter.ToInt16(data.Slice(i * 2, 2));
            nums.Add(raw / 10000.0); // rough mV/V if device sends scaled ints
        }
        EmitActive(nums);
    }

    /// <summary>Map active-only sample list onto full channel vector (disabled → NaN).</summary>
    private void EmitActive(List<double> activeValues)
    {
        var expected = _channels.Count(c => c.Enabled && _digitalChannelIndex != c.Index);
        if (expected > 0 && activeValues.Count != expected && _framesEmitted < 3)
        {
            StatusChanged?.Invoke(this,
                $"OMB: {activeValues.Count} valori vs {expected} canale On — verificare ACT mask (Stop→Start).");
        }

        var values = new double[_channels.Count];
        Array.Fill(values, double.NaN);
        var ai = 0;
        for (var i = 0; i < _channels.Count; i++)
        {
            if (_digitalChannelIndex == i) continue;
            if (!_channels[i].Enabled && ai >= activeValues.Count) continue;
            if (!_channels[i].Enabled) continue;
            if (ai >= activeValues.Count) break;
            // Raw engineering units from device; AcquisitionEngine applies Scale/Offset/Tare.
            values[i] = activeValues[ai++];
        }

        // If device returned a dense 8-wide block while only CH0 enabled, still take index 0.
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
        var nCopy = Math.Min(values.Length, _lastRawValues.Length);
        for (var i = 0; i < nCopy; i++)
            _lastRawValues[i] = values[i];
        SampleReceived?.Invoke(this, new SampleFrame
        {
            Timestamp = _lastSampleUtc.Value,
            Sequence = Interlocked.Increment(ref _sequence),
            Values = values
        });
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
        // DigIO slot exists but leave Enabled as UI decides (default off to avoid ACT confusion).
        if (!_channels[di].Enabled)
            _channels[di].Enabled = false;
    }

    private void WriteCmd(string cmd, int settleMs = 15) => TryWriteCmd(cmd, settleMs);

    private bool TryWriteCmd(string cmd, int settleMs = 15)
    {
        if (_usb is not { IsOpen: true }) return false;
        // Validated 0.339 path used LF terminators (same as HbmUsbIo.TransactAscii).
        var payload = cmd.EndsWith('\n') || cmd.EndsWith('\r') ? cmd : cmd + "\n";
        try { _usb.Write(Encoding.ASCII.GetBytes(payload), 600); }
        catch { return false; }
        if (settleMs > 0)
            Thread.Sleep(settleMs);
        return true;
    }

    private void DrainInput(int budgetMs)
    {
        if (_usb is not { IsOpen: true }) return;
        var buf = new byte[512];
        var t0 = Environment.TickCount64;
        while (Environment.TickCount64 - t0 < budgetMs)
        {
            try
            {
                var n = _usb.Read(buf, 40);
                if (n <= 0) break;
            }
            catch
            {
                break; // pipe glitch after EST — ignore, keep connection
            }
        }
        lock (_rxLock) _rx.Clear();
    }

    private void EnsureUsb()
    {
        if (_usb is not { IsOpen: true })
            throw new InvalidOperationException("USBHBM nu este deschis.");
    }

    private static int BridgeToAsa(BridgeType b) => Spider8MeasuringRange.BridgeToAsaType(b);

    private static int RangeToAsa(BridgeType bridge, double electricalFs)
        => Spider8MeasuringRange.ToAsaRangeCode(bridge, electricalFs);

    private static BridgeType ResolveAcquisitionBridge(ChannelConfig ch)
        => Spider8MeasuringRange.ResolveAcquisitionBridge(ch);

    private static double ResolveProgrammedElectricalFs(ChannelConfig ch, BridgeType bridge)
        => Spider8MeasuringRange.ResolveProgrammedElectricalFs(ch, bridge);

    private static double ProgrammedFsFromAsaCode(int asaRange, double fallback)
        => Spider8MeasuringRange.ProgrammedFsFromAsaCode(asaRange, fallback);

    private static double OmbFallbackRange(ChannelConfig ch)
    {
        var bridge = ResolveAcquisitionBridge(ch);
        if (bridge == BridgeType.DcVoltage)
            return ResolveProgrammedElectricalFs(ch, bridge);
        var hint = ch.RangeMvPerV > 0 ? ch.RangeMvPerV : 3.0;
        return hint <= 3.5 ? 3.0 : 12.0;
    }

    private static int FilterToAsf(double hz)
    {
        // nearest common Spider8 ASF codes
        if (hz <= 2.5) return 932;       // 2.5 Hz
        if (hz <= 5) return IdsF005Hz;   // 5 Hz (catman BE 5)
        if (hz <= 10) return IdsF010Hz;  // 10 Hz
        if (hz <= 20) return 945;
        if (hz <= 40) return 948;
        return IdsF010Hz;
    }

    private static int RateToIcr(int hz) => hz switch
    {
        <= 1 => 6300,
        <= 2 => 6301,
        <= 5 => 6302,
        <= 10 => 6303,
        <= 25 => 6304,
        <= 50 => IdsM50Hz,
        <= 60 => 6306,
        <= 75 => 6307,
        <= 100 => 6308,
        _ => IdsM50Hz
    };

    private static string CleanAsciiProbe(string? s)
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
