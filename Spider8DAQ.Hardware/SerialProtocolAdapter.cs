using System.IO.Ports;
using System.Text;
using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Hardware;

/// <summary>
/// RS-232 adapter for Spider8 (factory defaults: 9600 baud, 8E1).
/// Uses ASCII command style documented for Spider8 (BDR/MSV/STP/TRG and channel setup).
/// Exact firmware dialects vary; prefer Spider32.dll when available. Simulator works offline.
/// </summary>
public sealed class SerialProtocolAdapter : ISpider8Device, IDeviceHealth
{
    private readonly string _portName;
    private readonly int _baudRate;
    private readonly List<ChannelConfig> _channels;
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private long _sequence;
    private DateTime? _lastSampleUtc;
    private string? _lastIdn;
    private long _readErrors;

    public SerialProtocolAdapter(string portName, int baudRate = 9600, int channelCount = 8)
    {
        _portName = portName;
        _baudRate = baudRate;
        _channels = Enumerable.Range(0, channelCount)
            .Select(i => new ChannelConfig { Index = i, Name = $"CH{i}", Unit = "mV/V" })
            .ToList();
    }

    public string DisplayName => $"Spider8 [COM {_portName}]";
    public DeviceConnectionState State { get; private set; } = DeviceConnectionState.Disconnected;
    public IReadOnlyList<ChannelConfig> Channels => _channels;
    public int SampleRateHz { get; set; } = 50;
    public DateTime? LastSampleUtc => _lastSampleUtc;
    public string? FirmwareHint => _lastIdn;

    public event EventHandler<SampleFrame>? SampleReceived;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? ConnectionLost;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        State = DeviceConnectionState.Connecting;
        Exception? last = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusChanged?.Invoke(this,
                attempt == 1
                    ? $"Deschid {_portName} @ {_baudRate} 8E1…"
                    : $"Reîncerc Serial {_portName} (backoff 400 ms)…");

            _port?.Dispose();
            _port = new SerialPort(_portName, _baudRate, Parity.Even, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                NewLine = "\r",
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                Encoding = Encoding.ASCII
            };

            try
            {
                _port.Open();
                await SendCommandAsync("IDN?", cancellationToken);
                await Task.Delay(150, cancellationToken);
                try
                {
                    var idn = _port.ReadExisting();
                    if (!string.IsNullOrWhiteSpace(idn))
                    {
                        _lastIdn = idn.Trim();
                        StatusChanged?.Invoke(this, $"IDN: {_lastIdn}");
                    }
                }
                catch { /* ignore */ }
                State = DeviceConnectionState.Connected;
                StatusChanged?.Invoke(this, $"Conectat pe {_portName}.");
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                StatusChanged?.Invoke(this, $"Serial Connect #{attempt}: {ex.Message}");
                try { if (_port.IsOpen) _port.Close(); } catch { /* ignore */ }
                _port.Dispose();
                _port = null;
                if (attempt < 2)
                    await Task.Delay(400, cancellationToken);
            }
        }

        State = DeviceConnectionState.Error;
        var msg = "Serial Connect eșuat: " + (last?.Message ?? "necunoscut");
        StatusChanged?.Invoke(this, msg);
        throw new InvalidOperationException(msg, last);
    }

    public async Task DisconnectAsync()
    {
        await StopStreamingAsync();
        if (_port is { IsOpen: true })
            _port.Close();
        _port?.Dispose();
        _port = null;
        State = DeviceConnectionState.Disconnected;
        StatusChanged?.Invoke(this, "Serial disconnected.");
    }

    public Task StartStreamingAsync(CancellationToken cancellationToken = default)
    {
        EnsurePort();
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        State = DeviceConnectionState.Streaming;
        // Parameterized MSV only — bare "MSV" → EST 10005 / LED ERROR (same as USB SoftSetup).
        StatusChanged?.Invoke(this, "Streaming (serial MSV0,1,6100,1).");
        _ = SendCommandAsync("MSV0,1,6100,1", cancellationToken);
        _loop = Task.Run(() => ReadLoopAsync(_cts.Token));
        return Task.CompletedTask;
    }

    public async Task StopStreamingAsync()
    {
        try { await SendCommandAsync("STP"); } catch { /* ignore */ }
        if (_cts is null) return;
        _cts.Cancel();
        try { if (_loop is not null) await _loop; } catch { /* ignore */ }
        _cts.Dispose();
        _cts = null;
        _loop = null;
        if (State == DeviceConnectionState.Streaming)
            State = DeviceConnectionState.Connected;
        StatusChanged?.Invoke(this, "Serial streaming stopped.");
    }

    public async Task TareAsync(int? channelIndex = null)
    {
        if (channelIndex is int idx)
            await SendCommandAsync($"TAR{idx + 1}");
        else
            await SendCommandAsync("TAR");
        StatusChanged?.Invoke(this, "Tare command sent.");
    }

    public async Task<double> ShuntCheckAsync(int channelIndex)
    {
        // Official RS-232 is ASS<n>,43/42 with the same 0-based n as ACT/ASA (not CHnSH1).
        var n = channelIndex;
        double reading = double.NaN;
        try
        {
            await SendCommandAsync($"ASS{n},43");
            await Task.Delay(120);
            // Best-effort read of measurement line while shunt is engaged.
            if (_port is { IsOpen: true })
            {
                try
                {
                    var chunk = _port.ReadExisting();
                    reading = TryParseFirstNumber(chunk);
                }
                catch { /* ignore */ }
            }
            await SendCommandAsync($"ASS{n},42");
        }
        catch
        {
            // fall through
        }

        if (channelIndex >= 0 && channelIndex < _channels.Count)
        {
            _channels[channelIndex].ShuntEnabled = true;
            if (!double.IsNaN(reading))
                _channels[channelIndex].LastShuntReading = reading;
        }
        StatusChanged?.Invoke(this, double.IsNaN(reading)
            ? $"Shunt CH{n}: ASS{n},43/42 trimis (fără citire pe Serial)."
            : $"Shunt CH{n}: {reading:0.000}");
        return reading;
    }

    private static double TryParseFirstNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return double.NaN;
        var parts = text.Replace(';', ' ').Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            if (double.TryParse(p, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v)
                && v < 10000)
                return v;
        }
        return double.NaN;
    }

    public async Task ApplyChannelConfigAsync(IEnumerable<ChannelConfig> channels)
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
            _channels[i].AlarmEnabled = cfg.AlarmEnabled;
            _channels[i].AlarmLow = cfg.AlarmLow;
            _channels[i].AlarmHigh = cfg.AlarmHigh;
        }

        // Hardware setup requires an open port (Connect first).
        if (_port is not { IsOpen: true })
        {
            StatusChanged?.Invoke(this, "Channel config stored locally (port closed).");
            return;
        }

        for (var i = 0; i < Math.Min(list.Count, _channels.Count); i++)
        {
            var cfg = list[i];
            var ch = i + 1;
            var bridge = Spider8MeasuringRange.ResolveAcquisitionBridge(cfg);
            if (bridge != cfg.Bridge)
            {
                _channels[i].Bridge = bridge;
                cfg = _channels[i];
            }
            var rangeFs = Spider8MeasuringRange.ResolveProgrammedElectricalFs(cfg, bridge);
            _channels[i].RangeMvPerV = rangeFs;

            await SendCommandAsync($"CH{ch}EN{(cfg.Enabled ? 1 : 0)}");
            // Prefer HBM ASA codes when firmware accepts them (DcVoltage 420 + 10 V = 712).
            var asaType = Spider8MeasuringRange.BridgeToAsaType(bridge);
            var asaRange = Spider8MeasuringRange.ToAsaRangeCode(bridge, rangeFs);
            try
            {
                await SendCommandAsync($"ACT{i},{(cfg.Enabled ? 1 : 0)}");
                await SendCommandAsync($"ASA{i},{asaType},{asaRange}");
            }
            catch { /* some serial dialects ignore ACT/ASA */ }

            await SendCommandAsync($"CH{ch}BR{(int)bridge}");
            await SendCommandAsync($"CH{ch}RG{rangeFs.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            await SendCommandAsync($"CH{ch}FI{cfg.FilterHz.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            await SendCommandAsync($"CH{ch}SR{cfg.ChannelSampleRateHz}");
            if (bridge is not BridgeType.DcVoltage and not BridgeType.None and not BridgeType.Potentiometric)
            {
                var exc = cfg.ExcitationV > 0 && cfg.ExcitationV <= 12.5 ? cfg.ExcitationV : 2.5;
                await SendCommandAsync($"CH{ch}EX{exc.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }
        }
        StatusChanged?.Invoke(this, "Channel setup (ACT/ASA/EN — P15=DcVoltage 0…10 V).");
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new StringBuilder();
        while (!ct.IsCancellationRequested && _port is { IsOpen: true })
        {
            try
            {
                var chunk = _port.ReadExisting();
                if (!string.IsNullOrEmpty(chunk))
                {
                    buffer.Append(chunk);
                    while (true)
                    {
                        var text = buffer.ToString();
                        var idx = text.IndexOfAny(['\r', '\n']);
                        if (idx < 0) break;
                        var line = text[..idx].Trim();
                        buffer.Remove(0, idx + 1);
                        if (line.Length == 0) continue;
                        ParseMeasurementLine(line);
                    }
                }
                else
                {
                    await Task.Delay(5, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (TimeoutException) { }
            catch (Exception ex)
            {
                _readErrors++;
                StatusChanged?.Invoke(this, $"Serial read error: {ex.Message}");
                await Task.Delay(100, ct);
            }
        }
    }

    private void ParseMeasurementLine(string line)
    {
        // Expected flexible formats: "1.23;2.34;..." or "CH 1.23 2.34 ..."
        var parts = line.Replace(';', ' ').Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var nums = new List<double>();
        foreach (var p in parts)
        {
            if (double.TryParse(p, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                nums.Add(v);
        }

        if (nums.Count == 0) return;

        var values = new double[_channels.Count];
        for (var i = 0; i < values.Length; i++)
            values[i] = i < nums.Count ? nums[i] : double.NaN;

        _lastSampleUtc = DateTime.UtcNow;
        SampleReceived?.Invoke(this, new SampleFrame
        {
            Timestamp = _lastSampleUtc.Value,
            Sequence = Interlocked.Increment(ref _sequence),
            Values = values
        });
    }

    public async Task<DeviceHealthReport> SelfTestAsync(CancellationToken cancellationToken = default)
    {
        var checks = new List<string>();
        var portOpen = _port is { IsOpen: true };
        checks.Add(portOpen ? "COM port open: OK" : "COM port open: FAIL");
        if (!portOpen)
        {
            return new DeviceHealthReport
            {
                Status = HealthStatus.Offline,
                Summary = "Port closed",
                PortOpen = false,
                Checks = checks,
                LastSampleUtc = _lastSampleUtc
            };
        }

        try
        {
            await SendCommandAsync("IDN?", cancellationToken);
            await Task.Delay(120, cancellationToken);
            var resp = _port!.ReadExisting();
            if (!string.IsNullOrWhiteSpace(resp))
            {
                _lastIdn = resp.Trim();
                checks.Add("IDN response: OK (" + Trunc(_lastIdn, 60) + ")");
            }
            else checks.Add("IDN response: empty (device may still work for MSV)");

            await SendCommandAsync("ERR?", cancellationToken);
            await Task.Delay(80, cancellationToken);
            var err = _port.ReadExisting();
            checks.Add(string.IsNullOrWhiteSpace(err) ? "ERR query: no data" : "ERR: " + Trunc(err.Trim(), 60));
        }
        catch (Exception ex)
        {
            checks.Add("Self-test IO error: " + ex.Message);
            return new DeviceHealthReport
            {
                Status = HealthStatus.Fault,
                Summary = ex.Message,
                PortOpen = true,
                Checks = checks,
                LastSampleUtc = _lastSampleUtc,
                FirmwareHint = _lastIdn
            };
        }

        var age = _lastSampleUtc is DateTime t ? (DateTime.UtcNow - t).TotalSeconds : (double?)null;
        var status = age is null ? HealthStatus.Degraded
            : age > 3 ? HealthStatus.Degraded
            : HealthStatus.Healthy;
        if (_readErrors > 20) status = HealthStatus.Degraded;
        checks.Add($"Read errors cumulative: {_readErrors}");
        checks.Add(age is null ? "Last sample: none yet" : $"Last sample: {age:0.00}s ago");

        return new DeviceHealthReport
        {
            Status = status,
            Summary = $"Serial {_portName}: {status}",
            PortOpen = true,
            Checks = checks,
            LastSampleUtc = _lastSampleUtc,
            SecondsSinceLastSample = age,
            FirmwareHint = _lastIdn
        };
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n] + "...";

    private async Task SendCommandAsync(string command, CancellationToken ct = default)
    {
        EnsurePort();
        var payload = command.EndsWith("\r") ? command : command + "\r";
        var bytes = Encoding.ASCII.GetBytes(payload);
        await _port!.BaseStream.WriteAsync(bytes, ct);
        await _port.BaseStream.FlushAsync(ct);
    }

    private void EnsurePort()
    {
        if (_port is not { IsOpen: true })
            throw new InvalidOperationException("Serial port is not open.");
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
