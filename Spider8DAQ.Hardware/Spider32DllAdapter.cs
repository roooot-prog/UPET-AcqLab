using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Hardware;

/// <summary>
/// P/Invoke bridge to HBM Spider32.dll (32-bit) per official S8_* exports (s8exmpl / Spider8 Setup).
/// Place Spider32.dll + Intfac32.dll + Papo32.dll (+ Interlnk.dll) from HBM into vendor/.
/// USBHBM needs a Setup-era DLL that talks to usbhbm.sys; the 1999 examples DLL is COM/LPT/GPIB only.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Spider32DllAdapter : ISpider8Device, IDeviceHealth, IDigitalIoChannel
{
    private const int PortGpib = 0;
    private const int PortCom1 = 1;
    private const int PortCom2 = 2;
    private const int PortLpt1 = 90;
    private const int PortLpt2 = 91;
    // Observed in later Spider8 Setup builds that add USB IO (usbhbm); harmless if unsupported.
    private const int PortUsb = 100;

    private const int NibbleMode = 1;
    private const int Ids9600Baud = 1408;
    private const int S8Online = 0;

    private const int CsMeasValue = 0x0100;
    private const int CsChanType = 0x0002;
    private const int CsDigIo = 0x0800;
    private const int CsIsActive = 0x0010;
    private const int DsNumChan = 0x0001;
    private const int IdsS8DigIo = 5055;

    private readonly string? _dllPath;
    private readonly string? _portHint;
    private readonly List<ChannelConfig> _channels;
    private IntPtr _lib = IntPtr.Zero;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _sequence;
    private readonly double[] _lastRawValues;
    private bool _initialized;
    private int? _digitalChannelIndex;
    private string _dllResolvedPath = "";

    private S8_InitAllDelegate? _initAll;
    private S8_CloseDeviceDelegate? _closeDevice;
    private S8_ClosePortDelegate? _closePort;
    private S8_MeasOneValDelegate? _measOneVal;
    private S8_GetChanSettingsFloatDelegate? _getChanFloat;
    private S8_GetChanSettingsLongDelegate? _getChanLong;
    private S8_GetDevSettingsLongDelegate? _getDevLong;
    private S8_DoTaraDelegate? _doTara;
    private S8_TareSetupDelegate? _tareSetup;
    private S8_ACQSetupDelegate? _acqSetup;
    private S8_OpenDeviceDelegate? _openDevice;
    private S8_GetVersionDelegate? _getVersion;

    public Spider32DllAdapter(string? dllPath, string? portHint, int channelCount = 8)
    {
        _dllPath = dllPath;
        _portHint = portHint;
        var n = Math.Clamp(channelCount, 8, 10);
        _channels = Enumerable.Range(0, n)
            .Select(i => new ChannelConfig { Index = i, Name = $"CH{i}", Unit = "mV/V" })
            .ToList();
        _lastRawValues = new double[n];
        Array.Fill(_lastRawValues, double.NaN);
    }

    public string DisplayName
    {
        get
        {
            var hint = NormalizeUsbHint(_portHint);
            return string.IsNullOrEmpty(hint)
                ? "Spider8 [Spider32]"
                : $"Spider8 [USB {hint}]";
        }
    }

    private static string? NormalizeUsbHint(string? portHint)
    {
        if (string.IsNullOrWhiteSpace(portHint)) return null;
        var t = portHint.Trim();
        if (t.StartsWith("USB ", StringComparison.OrdinalIgnoreCase))
            t = t[4..].Trim();
        return t;
    }

    public DeviceConnectionState State { get; private set; } = DeviceConnectionState.Disconnected;
    public IReadOnlyList<ChannelConfig> Channels => _channels;
    public int SampleRateHz { get; set; } = 50;
    /// <summary>0-based index of digital/bitmask channel if API reported IDS_S8DigIO; otherwise null.</summary>
    public int? DigitalChannelIndex => _digitalChannelIndex;

    public event EventHandler<SampleFrame>? SampleReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? ConnectionLost;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = DeviceConnectionState.Connecting;
        var path = ResolveDllPath();
        if (path is null || !File.Exists(path))
        {
            State = DeviceConnectionState.Error;
            var msg =
                "Spider32.dll lipsește. Copiați-l din pachetul oficial HBM (s8exmpl.zip sau Spider8 Setup / sp32.zip) în vendor\\Spider32.dll " +
                "(împreună cu Intfac32.dll, Papo32.dll).";
            StatusChanged?.Invoke(this, msg);
            throw new FileNotFoundException(msg, path);
        }

        _dllResolvedPath = path;
        var vendorDir = Path.GetDirectoryName(path)!;
        // Ensure companion DLLs (Intfac32, Papo32, Interlnk) resolve next to Spider32.
        SetDefaultDllDirectories(vendorDir);

        try
        {
            _lib = NativeLibrary.Load(path);
        }
        catch (Exception ex)
        {
            State = DeviceConnectionState.Error;
            var msg = $"Nu s-a putut încărca Spider32.dll (necesar win-x86): {ex.Message}";
            StatusChanged?.Invoke(this, msg);
            throw new InvalidOperationException(msg, ex);
        }

        try
        {
            BindExports();
            if (_initAll is null)
            {
                State = DeviceConnectionState.Error;
                var msg = "Spider32.dll încărcat, dar exportul S8_InitAll lipsește. Verificați versiunea DLL.";
                StatusChanged?.Invoke(this, msg);
                throw new InvalidOperationException(msg);
            }

            if (_getVersion is not null)
            {
                var verBuf = new byte[64];
                try
                {
                    _getVersion(verBuf);
                    var ver = System.Text.Encoding.ASCII.GetString(verBuf).TrimEnd('\0', ' ');
                    if (!string.IsNullOrWhiteSpace(ver))
                        StatusChanged?.Invoke(this, $"Spider32 versiune: {ver}");
                }
                catch { /* ignore */ }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var (port, mode, label) = ResolvePortAndMode(_portHint);
            // Soft-close in case a previous process left the DLL/driver session half-open.
            try { _closeDevice?.Invoke(); } catch { /* ignore */ }
            try { _closePort?.Invoke(); } catch { /* ignore */ }
            StatusChanged?.Invoke(this, $"S8_InitAll({label})...");

            var rc = _initAll(port, mode);
            if (rc < 0)
            {
                // USBHBM: if hint was not PORT_USB, retry USB code once.
                if (HbmUsbDeviceScanner.IsHbmUsbTarget(_portHint) && port != PortUsb)
                {
                    StatusChanged?.Invoke(this, "S8_InitAll({label}) -> {DescribeError(rc)}; reincerc PORT_USB...");
                    try { _closePort?.Invoke(); } catch { /* ignore */ }
                    rc = _initAll(PortUsb, 0);
                    label = "PORT_USB";
                }
                else if (port == PortUsb && rc is -1 or -5)
                {
                    StatusChanged?.Invoke(this, "S8_InitAll({label}) -> {DescribeError(rc)}; reincerc dupa ClosePort...");
                    try { _closeDevice?.Invoke(); } catch { /* ignore */ }
                    try { _closePort?.Invoke(); } catch { /* ignore */ }
                    System.Threading.Thread.Sleep(250);
                    rc = _initAll(PortUsb, 0);
                }
            }

            if (rc < 0)
            {
                State = DeviceConnectionState.Error;
                var msg = BuildInitFailureMessage(rc, label);
                StatusChanged?.Invoke(this, msg);
                NativeLibrary.Free(_lib);
                _lib = IntPtr.Zero;
                throw new InvalidOperationException(msg);
            }

            if (_openDevice is not null)
            {
                var numChan = 0;
                var openRc = _openDevice(S8Online, ref numChan);
                if (openRc < 0)
                    StatusChanged?.Invoke(this, $"S8_OpenDevice: {DescribeError(openRc)} (continuăm).");
                else if (numChan > 0)
                    StatusChanged?.Invoke(this, $"Dispozitiv online, canale raportate: {numChan}");
            }

            ProbeDigitalChannel();
            SetupAcquisitionChannels();

            _initialized = true;
            State = DeviceConnectionState.Connected;
            StatusChanged?.Invoke(this, $"Conectat via Spider32 ({label}): {path}");
            return Task.CompletedTask;
        }
        catch (OperationCanceledException)
        {
            CleanupNative();
            State = DeviceConnectionState.Disconnected;
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CleanupNative();
            State = DeviceConnectionState.Error;
            var msg = $"Connect Spider32 eșuat: {ex.Message}";
            StatusChanged?.Invoke(this, msg);
            throw new InvalidOperationException(msg, ex);
        }
    }

    public async Task DisconnectAsync()
    {
        await StopStreamingAsync();
        try
        {
            _closeDevice?.Invoke();
            _closePort?.Invoke();
        }
        catch { /* ignore */ }

        CleanupNative();
        _initialized = false;
        _digitalChannelIndex = null;
        State = DeviceConnectionState.Disconnected;
        StatusChanged?.Invoke(this, "Spider32.dll descărcat.");
    }

    public Task StartStreamingAsync(CancellationToken cancellationToken = default)
    {
        if (State == DeviceConnectionState.Disconnected || !_initialized)
            throw new InvalidOperationException("Nu sunteți conectat la Spider32.");

        if (_measOneVal is null || _getChanFloat is null)
            throw new InvalidOperationException(
                "DLL fără S8_MeasOneVal / S8_GetChanSettings - nu se poate face polling. Verificați versiunea Spider32.dll.");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        State = DeviceConnectionState.Streaming;
        StatusChanged?.Invoke(this, "Streaming Spider32 (S8_MeasOneVal).");
        _loop = Task.Run(() => PollLoopAsync(_cts.Token));
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
        StatusChanged?.Invoke(this, "Streaming Spider32 oprit.");
    }

    public Task TareAsync(int? channelIndex = null)
    {
        if (_lib == IntPtr.Zero)
        {
            StatusChanged?.Invoke(this, "Tare local (DLL neîncărcat).");
            ApplyLocalTare(channelIndex);
            return Task.CompletedTask;
        }

        if (channelIndex is int idx && _tareSetup is not null)
        {
            var chans = new int[] { idx };
            var rc = _tareSetup(1, chans);
            StatusChanged?.Invoke(this, rc < 0
                ? $"S8_TareSetup: {DescribeError(rc)}"
                : $"S8_TareSetup canal {idx + 1} OK.");
        }
        else if (_doTara is not null)
        {
            var rc = _doTara();
            StatusChanged?.Invoke(this, rc < 0
                ? $"S8_DoTara: {DescribeError(rc)}"
                : "S8_DoTara OK.");
        }
        else
        {
            ApplyLocalTare(channelIndex);
            StatusChanged?.Invoke(this, "Export tare absent; zero aplicat local.");
        }

        return Task.CompletedTask;
    }

    public Task<double> ShuntCheckAsync(int channelIndex)
    {
        // Spider32.dll has S8_ACQShuntCal, not CHnSH1/OMB?0. Do not invent ~1 mV/V (fake PASS).
        double reading = double.NaN;
        if (channelIndex >= 0 && channelIndex < _lastRawValues.Length
            && !double.IsNaN(_lastRawValues[channelIndex])
            && Math.Abs(_lastRawValues[channelIndex]) > 1e-9)
            reading = _lastRawValues[channelIndex];

        if (channelIndex >= 0 && channelIndex < _channels.Count)
        {
            _channels[channelIndex].ShuntEnabled = true;
            if (!double.IsNaN(reading))
                _channels[channelIndex].LastShuntReading = reading;
        }

        StatusChanged?.Invoke(this, double.IsNaN(reading)
            ? $"Shunt CH{channelIndex + 1}: Spider32.dll fără OMB după SH — Start până se mișcă Citirea sau HBM USB (DEST)."
            : $"Shunt CH{channelIndex + 1}: {reading:G6} (live S8_MeasOneVal; DLL nu face CHnSH1/OMB).");
        return Task.FromResult(reading);
    }

    public Task ApplyChannelConfigAsync(IEnumerable<ChannelConfig> channels)
    {
        var list = channels.ToList();
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
        }
        // Re-bind S8_ACQSetup to the current Enabled set (Connect ran setup before UI Apply).
        SetupAcquisitionChannels();
        return Task.CompletedTask;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        var buffer = new double[_channels.Count];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var rc = _measOneVal!();
                if (rc >= 0)
                {
                    for (var i = 0; i < _channels.Count; i++)
                    {
                        float mv = 0;
                        var grc = _getChanFloat!(i, CsMeasValue, ref mv);
                        buffer[i] = grc >= 0 ? mv : double.NaN;
                        // Overflow sentinels from S8_dll.h
                        if (mv <= -1e37f || mv >= 1e37f)
                            buffer[i] = double.NaN;
                    }

                    if (_digitalChannelIndex is int di && di >= 0 && di < buffer.Length && _getChanLong is not null)
                    {
                        long dig = 0;
                        if (_getChanLong(di, CsDigIo, ref dig) >= 0)
                            buffer[di] = dig;
                    }

                    LastSampleUtc = DateTime.UtcNow;
                    var nCopy = Math.Min(buffer.Length, _lastRawValues.Length);
                    for (var i = 0; i < nCopy; i++)
                        _lastRawValues[i] = buffer[i];
                    SampleReceived?.Invoke(this, new SampleFrame
                    {
                        Timestamp = LastSampleUtc.Value,
                        Sequence = Interlocked.Increment(ref _sequence),
                        Values = buffer.ToArray()
                    });
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Eroare poll Spider32: {ex.Message}");
            }

            var delay = Math.Max(1, 1000 / Math.Max(1, SampleRateHz));
            try { await Task.Delay(delay, ct); } catch (OperationCanceledException) { break; }
        }
    }

    private void ProbeDigitalChannel()
    {
        _digitalChannelIndex = null;
        if (_getDevLong is null || _getChanLong is null) return;

        long numChan = _channels.Count;
        try { _getDevLong(DsNumChan, ref numChan); } catch { return; }
        if (numChan <= 0) return;

        var limit = (int)Math.Min(numChan, 10);
        EnsureChannelSlots(limit);

        for (var i = 0; i < limit; i++)
        {
            long chanType = 0;
            try
            {
                if (_getChanLong(i, CsChanType, ref chanType) < 0) continue;
            }
            catch { continue; }

            if (chanType == IdsS8DigIo)
            {
                _digitalChannelIndex = i;
                _channels[i].Name = i == 8 ? "CH8 DI" : $"CH{i} DI";
                _channels[i].Unit = "bitmask";
                _channels[i].Bridge = BridgeType.None;
                StatusChanged?.Invoke(this, $"Canal digital detectat: index {i} ({_channels[i].Name} / bitmask).");
                return;
            }
        }

        // Soft probe: DigIO readable on last channel without inventing a fake row if type unknown.
        if (limit >= 8)
        {
            long dig = 0;
            var last = limit - 1;
            try
            {
                if (_getChanLong(last, CsDigIo, ref dig) >= 0 && dig != 0)
                {
                    // Readable DigIO alone is not enough to claim a DI channel type - skip inventing.
                    StatusChanged?.Invoke(this, $"S8_CS_DigIO citibil pe canal {last} (val={dig}); tip canal ≠ DigIO - nu adăugăm CH DI fals.");
                }
            }
            catch { /* ignore */ }
        }
    }

    private void EnsureChannelSlots(int count)
    {
        while (_channels.Count < count)
        {
            var i = _channels.Count;
            _channels.Add(new ChannelConfig { Index = i, Name = $"CH{i}", Unit = "mV/V" });
        }
    }

    private void SetupAcquisitionChannels()
    {
        if (_acqSetup is null) return;
        var enabled = _channels.Where(c => c.Enabled && c.Index != _digitalChannelIndex).Select(c => c.Index).ToArray();
        if (enabled.Length == 0) return;
        try
        {
            var rc = _acqSetup(enabled.Length, enabled);
            if (rc < 0)
                StatusChanged?.Invoke(this, $"S8_ACQSetup: {DescribeError(rc)}");
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke(this, $"S8_ACQSetup eșuat: {ex.Message}");
        }
    }

    private void ApplyLocalTare(int? channelIndex)
    {
        if (channelIndex is int idx && idx >= 0 && idx < _channels.Count)
            _channels[idx].TareValue = 0;
        else
            foreach (var ch in _channels) ch.TareValue = 0;
    }

    private void BindExports()
    {
        _initAll = GetExport<S8_InitAllDelegate>("S8_InitAll");
        _closeDevice = GetExport<S8_CloseDeviceDelegate>("S8_CloseDevice");
        _closePort = GetExport<S8_ClosePortDelegate>("S8_ClosePort");
        _measOneVal = GetExport<S8_MeasOneValDelegate>("S8_MeasOneVal");
        _getChanFloat = GetExport<S8_GetChanSettingsFloatDelegate>("S8_GetChanSettings");
        _getChanLong = GetExport<S8_GetChanSettingsLongDelegate>("S8_GetChanSettings");
        _getDevLong = GetExport<S8_GetDevSettingsLongDelegate>("S8_GetDevSettings");
        _doTara = GetExport<S8_DoTaraDelegate>("S8_DoTara");
        _tareSetup = GetExport<S8_TareSetupDelegate>("S8_TareSetup");
        _acqSetup = GetExport<S8_ACQSetupDelegate>("S8_ACQSetup");
        _openDevice = GetExport<S8_OpenDeviceDelegate>("S8_OpenDevice");
        _getVersion = GetExport<S8_GetVersionDelegate>("S8_GetVersion");
    }

    private T? GetExport<T>(string name) where T : Delegate
    {
        if (_lib == IntPtr.Zero) return null;
        if (!NativeLibrary.TryGetExport(_lib, name, out var ptr) || ptr == IntPtr.Zero) return null;
        return Marshal.GetDelegateForFunctionPointer<T>(ptr);
    }

    private static (int Port, int Mode, string Label) ResolvePortAndMode(string? hint)
    {
        var t = NormalizeUsbHint(hint) ?? "";
        if (HbmUsbDeviceScanner.IsHbmUsbTarget(hint) || t.StartsWith("USBHBM", StringComparison.OrdinalIgnoreCase))
            return (PortUsb, 0, $"PORT_USB/{t}");

        if (t.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(t.AsSpan(3), out var comNum))
        {
            // Official constants only document COM1/COM2; pass number for COM3+ if present.
            var port = comNum switch
            {
                1 => PortCom1,
                2 => PortCom2,
                _ => comNum
            };
            return (port, Ids9600Baud, $"{t}@9600");
        }

        if (t.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(t.AsSpan(3), out var lpt))
        {
            var port = lpt == 2 ? PortLpt2 : PortLpt1;
            return (port, NibbleMode, $"{t}/Nibble");
        }

        // Default: prefer USB if an HBM device is plugged, else LPT1 (classic examples default).
        if (HbmUsbDeviceScanner.IsUsbHbmPresent())
            return (PortUsb, 0, "PORT_USB(auto)");

        return (PortLpt1, NibbleMode, "LPT1/Nibble(auto)");
    }

    private string BuildInitFailureMessage(int rc, string label)
    {
        var err = DescribeError(rc);
        var usb = HbmUsbDeviceScanner.IsHbmUsbTarget(_portHint) || HbmUsbDeviceScanner.IsUsbHbmPresent();
        var holders = ListUsbHolders();
        if (usb)
        {
            if (rc is -5)
            {
                var who = holders.Count > 0 ? string.Join(", ", holders) : "catman Easy / alta instanta UPET";
                return
                    $"S8_InitAll({label}) a esuat: {err}. " +
                    $"Dispozitivul USBHBM e deja deschis ({who}). Inchideti acele programe, deconectati-reconectati USB, apoi Connect.";
            }

            if (rc is -1)
            {
                if (holders.Count > 0)
                {
                    return
                        $"S8_InitAll({label}) a esuat: {err}. " +
                        $"Posibil blocat de: {string.Join(", ", holders)}. Inchideti-le, scoateti/bagati USB, apoi Connect.";
                }

                return
                    $"S8_InitAll({label}) a esuat: {err}. " +
                    "Spider32.dll nu poate deschide USBHBM modern (InitAll=-1 pe port liber) — catman Easy foloseste alt stack. " +
                    "Pentru masurare acum: backend Simulator → Connect → Start → Record. " +
                    "Hardware alternativ: Serial pe COM RS232 (adaptor), nu USBHBM via Spider32. " +
                    "DirectNT e blocat pe Win11 (1275, doar LPT). Nu reincercati USB Spider32 in bucla.";
            }

            if (rc is -9)
            {
                return
                    $"S8_InitAll({label}) a esuat: {err}. " +
                    "Aceasta Spider32.dll nu recunoaste PORT_USB (tipic DLL din s8exmpl.zip, 1999 = doar COM/LPT/GPIB). " +
                    "Pentru USBHBM: rulati Spider8 Setup din sp32.zip (HBM legacy), apoi copiati Spider32.dll nou in vendor\\ " +
                    "(pastrati Intfac32/Papo32 din catmanEasy daca sunt mai noi).";
            }

            return
                $"S8_InitAll({label}) a esuat: {err}. " +
                "USBHBM e vizibil in Windows, dar InitAll a esuat. Inchideti catman; deconectati USB; " +
                "daca persista cu port liber, Spider32 poate fi incompatibil cu acest USBHBM (catman poate functiona separat).";
        }

        return $"S8_InitAll({label}) a esuat: {err}. Verificati cablul/portul sau daca alt program tine dispozitivul deschis.";
    }

    private static List<string> ListUsbHolders()
    {
        var names = new[] { "catmanEASY", "catmanEasy", "catman", "UPETAcqLab", "Spider8DAQ", "UPET" };
        var found = new List<string>();
        try
        {
            var self = Process.GetCurrentProcess().Id;
            foreach (var n in names)
            {
                foreach (var p in Process.GetProcessesByName(n))
                {
                    try
                    {
                        if (p.Id == self) continue;
                        found.Add($"{p.ProcessName}(PID {p.Id})");
                    }
                    catch { /* ignore */ }
                    finally { p.Dispose(); }
                }
            }
        }
        catch { /* ignore */ }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string DescribeError(int rc) => rc switch
    {
        0 => "OK",
        -1 => "ERR_PORT_OPEN_FAILED (port ocupat, indisponibil, sau Spider32 nu poate deschide USBHBM)",
        -2 => "ERR_BUILD_DCB_FAILED",
        -3 => "ERR_SETPORT_FAILED",
        -4 => "ERR_PORT_NOT_OPEN",
        -5 => "ERR_PORT_ALREADY_OPEN (dispozitiv deja deschis - închideți catman/UPET)",
        -6 => "ERR_PORT_CLOSE_FAILED",
        -7 => "ERR_PORT_NOT_AVAILABLE",
        -8 => "ERR_BUFF_ALLOC_FAILED",
        -9 => "ERR_INVALID_PORT (cod port necunoscut pentru această DLL)",
        -10 => "ERR_TIMO / timeout",
        -108 => "S8_ErrTimo (timeout măsurare)",
        -109 => "S8_ErrMeas",
        -110 => "S8_ErrNoChan (niciun canal activ)",
        _ => $"cod {rc}"
    };

    private string? ResolveDllPath()
    {
        if (!string.IsNullOrWhiteSpace(_dllPath) && File.Exists(_dllPath))
            return _dllPath;

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            _dllPath,
            Path.Combine(baseDir, "vendor", "Spider32.dll"),
            Path.Combine(baseDir, "Spider32.dll"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "vendor", "Spider32.dll"))
        };
        return candidates.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p!));
    }

    private static void SetDefaultDllDirectories(string dir)
    {
        try
        {
            // Prefer AddDllDirectory when available; fall back to SetDllDirectory.
            var k32 = NativeLibrary.Load("kernel32.dll");
            if (NativeLibrary.TryGetExport(k32, "SetDllDirectoryW", out var setDll))
            {
                var fn = Marshal.GetDelegateForFunctionPointer<SetDllDirectoryWDelegate>(setDll);
                fn(dir);
            }
        }
        catch
        {
            /* best-effort */
        }
    }

    private void CleanupNative()
    {
        if (_lib != IntPtr.Zero)
        {
            try { NativeLibrary.Free(_lib); } catch { /* ignore */ }
            _lib = IntPtr.Zero;
        }
        _initAll = null;
        _closeDevice = null;
        _closePort = null;
        _measOneVal = null;
        _getChanFloat = null;
        _getChanLong = null;
        _getDevLong = null;
        _doTara = null;
        _tareSetup = null;
        _acqSetup = null;
        _openDevice = null;
        _getVersion = null;
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();

    public DateTime? LastSampleUtc { get; private set; }
    public string? FirmwareHint => "Spider32.dll";

    public Task<DeviceHealthReport> SelfTestAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<string>
        {
            $"DLL: {_dllResolvedPath}",
            $"Lib încărcată: {_lib != IntPtr.Zero}",
            $"Init: {_initialized}",
            $"Stare: {State}",
            $"Canale: {_channels.Count}",
            $"DI index: {_digitalChannelIndex?.ToString() ?? "-"}",
            $"Port hint: {_portHint ?? "-"}"
        };
        checks.Add($"S8_MeasOneVal: {_measOneVal is not null}");
        checks.Add($"S8_GetChanSettings: {_getChanFloat is not null}");
        var usb = HbmUsbDeviceScanner.GetDevices();
        checks.Add(usb.Count == 0
            ? "PnP USBHBM: lipsă"
            : "PnP USBHBM: " + string.Join(", ", usb.Select(d => d.Serial)));
        var age = LastSampleUtc is DateTime t ? (DateTime.UtcNow - t).TotalSeconds : (double?)null;
        var status = !_initialized ? HealthStatus.Fault
            : State == DeviceConnectionState.Streaming && age is < 3 ? HealthStatus.Healthy
            : State is DeviceConnectionState.Connected or DeviceConnectionState.Streaming ? HealthStatus.Degraded
            : HealthStatus.Offline;
        if (status == HealthStatus.Healthy && usb.Count == 0)
            status = HealthStatus.Degraded;
        return Task.FromResult(new DeviceHealthReport
        {
            Status = status,
            Summary = $"Spider32.dll: {status}",
            Checks = checks,
            LastSampleUtc = LastSampleUtc,
            SecondsSinceLastSample = age,
            PortOpen = _lib != IntPtr.Zero,
            FirmwareHint = "Spider32.dll"
        });
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_InitAllDelegate(int port, int mode);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_CloseDeviceDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_ClosePortDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_MeasOneValDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_GetChanSettingsFloatDelegate(int channel, int what, ref float value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_GetChanSettingsLongDelegate(int channel, int what, ref long value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_GetDevSettingsLongDelegate(int what, ref long value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_DoTaraDelegate();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_TareSetupDelegate(int num, int[] chans);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_ACQSetupDelegate(int num, int[] chans);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_OpenDeviceDelegate(int mode, ref int numChan);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int S8_GetVersionDelegate(byte[] vers);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    private delegate bool SetDllDirectoryWDelegate(string path);
}
