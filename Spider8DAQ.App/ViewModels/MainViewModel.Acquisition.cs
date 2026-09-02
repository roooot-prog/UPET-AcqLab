using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Acquisition;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Display;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.MathChannels;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Sensors;
using Spider8DAQ.Core.Simulation;
using Spider8DAQ.Hardware;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    public void AttachPlots(ScottPlot.WPF.WpfPlot plotA, ScottPlot.WPF.WpfPlot plotB, ScottPlot.WPF.WpfPlot plotAnalysis)
    {
        _plotA = plotA;
        _plotB = plotB;
        _plotAnalysis = plotAnalysis;
        ApplyEngineeringPlotStyle(plotA.Plot, "Y(t) — live", "t [s]", "Y");
        ApplyEngineeringPlotStyle(plotB.Plot, "Y(t) B / FFT", "t [s]", "Y");
        ApplyEngineeringPlotStyle(plotAnalysis.Plot, "Analiză offline", "t [s]", "Y");
        plotA.UserInputProcessor.IsEnabled = true;
        plotB.UserInputProcessor.IsEnabled = true;
        plotAnalysis.UserInputProcessor.IsEnabled = true;
        plotA.MouseMove += (_, e) => UpdateLiveCursor(plotA, e);
        plotB.MouseMove += (_, e) => UpdateLiveCursor(plotB, e);
        plotA.MouseLeftButtonDown += (_, e) => CaptureLiveCursorRef(plotA, e);
        plotB.MouseLeftButtonDown += (_, e) => CaptureLiveCursorRef(plotB, e);
        plotAnalysis.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(plotAnalysis);
            var pos = plotAnalysis.Plot.GetCoordinates((float)p.X, (float)p.Y);
            if (_crosshairA is not null) { _crosshairA.IsVisible = true; _crosshairA.Position = new ScottPlot.Coordinates(pos.X, pos.Y); }
            var delta = string.IsNullOrWhiteSpace(DeltaText) ? "" : $"  {DeltaText}";
            CursorText = $"Cursor: X={pos.X:0.###}  Y={pos.Y:0.###}{delta}";
            plotAnalysis.Refresh();
        };
        RebuildPlotSeries();
    }

    private bool _liveCursorHasRef;
    private double _liveCursorRefX;
    private double _liveCursorRefY;

    private void CaptureLiveCursorRef(ScottPlot.WPF.WpfPlot plot, System.Windows.Input.MouseButtonEventArgs e)
    {
        var p = e.GetPosition(plot);
        var pos = plot.Plot.GetCoordinates((float)p.X, (float)p.Y);
        _liveCursorRefX = pos.X;
        _liveCursorRefY = pos.Y;
        _liveCursorHasRef = true;
        CursorText = $"Cursor A: X={pos.X:0.###}  Y={pos.Y:0.####}  (mută mouse → Δt/ΔY)";
    }

    private void UpdateLiveCursor(ScottPlot.WPF.WpfPlot plot, System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(plot);
        var pos = plot.Plot.GetCoordinates((float)p.X, (float)p.Y);
        if (_liveCursorHasRef)
        {
            var dt = pos.X - _liveCursorRefX;
            var dy = pos.Y - _liveCursorRefY;
            CursorText = $"Cursor: X={pos.X:0.###}  Y={pos.Y:0.####}  Δt={dt:0.####}  ΔY={dy:0.####}";
        }
        else
        {
            CursorText = $"Cursor: X={pos.X:0.###}  Y={pos.Y:0.####}";
        }
    }

    /// <summary>Oscilloscope-style axes: major/minor grid, unit labels, compact legend chrome.</summary>
    private static void ApplyEngineeringPlotStyle(ScottPlot.Plot plot, string title, string xLabel, string yLabel)
    {
        plot.Title(title);
        plot.Axes.Bottom.Label.Text = xLabel;
        plot.Axes.Left.Label.Text = yLabel;
        plot.Axes.Bottom.Label.FontSize = 12;
        plot.Axes.Left.Label.FontSize = 12;
        plot.Axes.Title.Label.FontSize = 12;
        plot.Axes.Bottom.TickLabelStyle.FontSize = 10;
        plot.Axes.Left.TickLabelStyle.FontSize = 10;
        plot.Axes.Bottom.TickLabelStyle.FontName = "Consolas";
        plot.Axes.Left.TickLabelStyle.FontName = "Consolas";
        plot.Grid.MajorLineWidth = 1.1f;
        plot.Grid.MinorLineWidth = 0.55f;
        plot.Legend.FontSize = 9;
        plot.Legend.OutlineWidth = 1;
        plot.Legend.ShadowColor = ScottPlot.Colors.Transparent;
        plot.Legend.Alignment = ScottPlot.Alignment.UpperRight;
        ApplyPlotChrome(plot);
    }

    private static void ApplyPlotChrome(ScottPlot.Plot plot)
    {
        var dark = LabUiPrefs.DarkTheme;
        if (dark)
        {
            plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#7A8490");
            plot.Grid.MinorLineColor = ScottPlot.Color.FromHex("#3E444C");
            plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1E2228");
            plot.DataBackground.Color = ScottPlot.Color.FromHex("#2A2F36");
            plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#7A8490");
            plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#343A42");
            var ink = ScottPlot.Color.FromHex("#F4F0E8");
            plot.Axes.Bottom.TickLabelStyle.ForeColor = ink;
            plot.Axes.Left.TickLabelStyle.ForeColor = ink;
            plot.Axes.Bottom.Label.ForeColor = ink;
            plot.Axes.Left.Label.ForeColor = ink;
            plot.Axes.Title.Label.ForeColor = ink;
        }
        else
        {
            plot.Grid.MajorLineColor = ScottPlot.Color.FromHex("#9AA7B5");
            plot.Grid.MinorLineColor = ScottPlot.Color.FromHex("#D5DCE4");
            plot.FigureBackground.Color = ScottPlot.Color.FromHex("#F7F9FB");
            plot.DataBackground.Color = ScottPlot.Color.FromHex("#FFFFFF");
            plot.Legend.OutlineColor = ScottPlot.Color.FromHex("#B8C2CE");
            plot.Legend.BackgroundColor = ScottPlot.Color.FromHex("#F0F4F8");
            var ink = ScottPlot.Color.FromHex("#10141A");
            plot.Axes.Bottom.TickLabelStyle.ForeColor = ink;
            plot.Axes.Left.TickLabelStyle.ForeColor = ink;
            plot.Axes.Bottom.Label.ForeColor = ink;
            plot.Axes.Left.Label.ForeColor = ink;
            plot.Axes.Title.Label.ForeColor = ink;
        }
    }

    internal void RestyleAttachedPlotsForTheme()
    {
        void One(ScottPlot.WPF.WpfPlot? p)
        {
            if (p is null) return;
            ApplyPlotChrome(p.Plot);
            p.Refresh();
        }

        One(_plotA);
        One(_plotB);
        One(_plotAnalysis);
        One(_plotDataViewer);
        One(_plotDefect);
        foreach (var runtime in _channelPlotRuntimes.Values)
        {
            if (runtime.Plot is null) continue;
            ApplyPlotChrome(runtime.Plot.Plot);
            runtime.Plot.Refresh();
        }
    }

    public void AutoscaleLivePlots()
    {
        FollowLiveZoom = true;
        ResetCatmanLiveAxis();
        if (_latestUiSample is not null)
            FeedCatmanLiveWindow(_latestUiSample);
        ApplyCatmanLiveAxis(force: true);
        if (double.IsNaN(_liveYMinA) || double.IsNaN(_liveYMaxA))
        {
            _plotA?.Plot.Axes.AutoScale();
            _plotB?.Plot.Axes.AutoScale();
        }
        _plotA?.Refresh();
        _plotB?.Refresh();
        foreach (var panel in ChannelPlots.ToList())
            AutoscaleChannelPlot(panel);
        Status = ShowLivePlotCard
            ? "Autoscale — Grafic live + panouri canal; urmărire live ON."
            : "Autoscale — panouri canal; urmărire live ON.";
    }

    public void ResetLivePlotZoom()
    {
        FollowLiveZoom = true;
        ResetCatmanLiveAxis();
        if (_latestUiSample is not null)
            FeedCatmanLiveWindow(_latestUiSample);
        ApplyCatmanLiveAxis(force: true);
        if (double.IsNaN(_liveYMinA) || double.IsNaN(_liveYMaxA))
        {
            _plotA?.Plot.Axes.AutoScale();
            _plotB?.Plot.Axes.AutoScale();
        }
        _plotA?.Refresh();
        _plotB?.Refresh();
        ResetAllChannelPlotScales();
        foreach (var panel in ChannelPlots.ToList())
            AutoscaleChannelPlot(panel);
        Status = ShowLivePlotCard
            ? "Zoom reset — Grafic live + panouri canal; urmărire live ON."
            : "Zoom reset — panouri canal; urmărire live ON.";
    }

    private void OnChannelRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChannelRow.Scale) or nameof(ChannelRow.TareValue)
            or nameof(ChannelRow.Offset) or nameof(ChannelRow.RangeMvPerV)
            or nameof(ChannelRow.Bridge) or nameof(ChannelRow.FilterHz))
        {
            // Autorange locks Scale + ASA range for the duration of Record (no mid-Rec AGC).
            if (_autorangeLocked
                && e.PropertyName is nameof(ChannelRow.Scale) or nameof(ChannelRow.RangeMvPerV))
                return;
            if (e.PropertyName == nameof(ChannelRow.Scale)
                && sender is ChannelRow scaled
                && IsStrainOrTimbruChannel(scaled))
            {
                var formula = ResolveTimbruFormulaScale(scaled);
                if (StrainScale.LooksAbsurdTimbruScale(scaled.Scale, formula))
                {
                    Status = ShuntCheck.ScaleInvalidLabel + " — "
                             + StrainScale.SuggestedTimbruScaleLabel(formula)
                             + ". Autorange/Capacity nu aparține în Scale.";
                    try { RefreshLabAdvisorFromStatus(Status); } catch { /* ignore */ }
                }
            }
            SchedulePushChannelScalarsToDevice();
            if (e.PropertyName == nameof(ChannelRow.FilterHz))
                ApplyMetrologyFilterConfig(resetState: false);
        }
        if (e.PropertyName is nameof(ChannelRow.ShowOnPlot) or nameof(ChannelRow.Enabled)
            or nameof(ChannelRow.Name) or nameof(ChannelRow.Unit)
            or nameof(ChannelRow.Capacity) or nameof(ChannelRow.SensorCategory))
        {
            ScheduleRebuildPlotSeries();
            if (e.PropertyName is nameof(ChannelRow.Name) or nameof(ChannelRow.Unit))
                RefreshAllChannelPlotLabels();
            if (e.PropertyName is nameof(ChannelRow.Capacity) or nameof(ChannelRow.Unit)
                or nameof(ChannelRow.SensorCategory) or nameof(ChannelRow.Name))
            {
                RefreshLiveGaugeChannelLabel();
                ResetAllLiveGaugeScales();
            }
            if (e.PropertyName == nameof(ChannelRow.Enabled))
            {
                // On checkbox must refresh LiveValues slots and push Enabled to the device;
                // SoftSetup (ACT/ASA) only runs at Start / reapply — otherwise engine keeps NaN.
                RefreshLiveValueHeaders();
                ResetAllLiveGaugeScales();
                SchedulePushEnabledChannelsToDevice();
                OnPropertyChanged(nameof(DominantLiveChannel));
                OnPropertyChanged(nameof(HasDominantLiveChannel));
            }
        }
    }

    /// <summary>
    /// Push Scale/Tare/Offset/Range to device channels (engine reads Device.Channels on each frame).
    /// </summary>
    private void SchedulePushChannelScalarsToDevice()
    {
        if (_suppressChannelConfigPush) return;
        if (_device is null || !IsConnected) return;
        if (_channelScalarsPushQueued) return;
        _channelScalarsPushQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            _channelScalarsPushQueued = false;
            if (_suppressChannelConfigPush || _device is null || !IsConnected) return;
            try
            {
                await _device.ApplyChannelConfigAsync(Channels.Select(ToConfig));
            }
            catch (Exception ex)
            {
                _journal.Warn("Sync Scale/Tare: " + ex.Message);
            }
        });
    }

    /// <summary>
    /// Coalesce rapid On toggles into one ApplyChannelConfig (+ SoftSetup restart if streaming).
    /// </summary>
    private void SchedulePushEnabledChannelsToDevice()
    {
        if (_suppressChannelConfigPush) return;
        if (_device is null || !IsConnected) return;
        if (_channelConfigPushQueued) return;
        _channelConfigPushQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, async () =>
        {
            _channelConfigPushQueued = false;
            if (_suppressChannelConfigPush) return;
            if (_device is null || !IsConnected) return;
            var n = Channels.Count(c => c.Enabled && !c.Name.Contains("DI", StringComparison.OrdinalIgnoreCase));
            await PushChannelConfigAsync(reapplyAcquisition: IsStreaming);
            if (IsStreaming)
                Status = $"Canale On: {n} — SoftSetup reaplicat (toate canalele active streaming).";
            else
                Status = $"Canale On: {n} — config stocată; apăsați Start (F5) pentru ACT/ASA pe toate.";
            RefreshOperatorStatus();
        });
    }

    private void ScheduleRebuildPlotSeries()
    {
        if (_suppressPlotRebuild) return;
        if (_plotRebuildQueued) return;
        _plotRebuildQueued = true;
        _dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            _plotRebuildQueued = false;
            if (_suppressPlotRebuild) return;
            RebuildPlotSeries();
        });
    }

    /// <summary>
    /// Catman-style labels: CH0…CH7 analog (0-based HW index). Multi-device: D1.CH0…
    /// DigIO uses <c>CH8 DI</c> when the backend reports it (not seeded here).
    /// </summary>
    private static string FormatAnalogChannelName(int globalIndex, int totalCount)
    {
        var local = globalIndex % 8;
        var device = globalIndex / 8;
        return totalCount > 8 ? $"D{device + 1}.CH{local}" : $"CH{local}";
    }

    private void SeedDefaultChannels(int count)
    {
        foreach (var existing in Channels)
            existing.PropertyChanged -= OnChannelRowPropertyChanged;
        Channels.Clear();
        LiveValues.Clear();
        for (var i = 0; i < count; i++)
        {
            // Lab default: catman Easy „U2B 5kN” (1-U2B/5KN) Full bridge on UI row CH2 (HW index 2).
            // Grid labels are 0-based (CH0…CH7); Apply CH# box uses the same index (CH2 → type 2).
            var isU2b = i == 2;
            var row = new ChannelRow
            {
                Index = i,
                DeviceIndex = i / 8,
                Name = FormatAnalogChannelName(i, count),
                Unit = isU2b ? "N" : i == 0 ? "µm/m" : "mV/V",
                Enabled = isU2b,
                ShowOnPlot = isU2b,
                Scale = isU2b ? 2500 : i == 0 ? StrainScale.FromGaugeFactor(BridgeType.Half, 2.0) : 1,
                Bridge = isU2b ? nameof(BridgeType.Full) : i == 0 ? nameof(BridgeType.Half) : nameof(BridgeType.Full),
                RangeMvPerV = isU2b ? 2 : 3,
                FilterHz = isU2b ? 5 : 5,
                ChannelSampleRateHz = SampleRateHz,
                ExcitationV = 2.5,
                TareValue = 0,
                SensorId = isU2b ? "u2b-5kn" : null,
                SensorName = isU2b ? "U2B 5kN" : null,
                SensorCategory = isU2b ? "Forță / Celule de sarcină" : null,
                Capacity = isU2b ? 5000 : 0,
                AlarmEnabled = isU2b,
                AlarmLow = isU2b ? -5250 : -1e9,
                AlarmHigh = isU2b ? 5250 : 1e9
            };
            row.PropertyChanged += OnChannelRowPropertyChanged;
            Channels.Add(row);
            LiveValues.Add(new LiveValueRow { Name = row.Name, Unit = row.Unit, Display = "—" });
        }
        SelectedChannelRow = Channels.FirstOrDefault(c => c.Enabled) ?? Channels.FirstOrDefault();
    }

    private void RefreshPorts()
    {
        var previous = SelectedPort;
        Ports.Clear();
        var com = ComPortScanner.GetPorts();
        var hbm = HbmUsbDeviceScanner.GetDevices();
        foreach (var p in HbmUsbDeviceScanner.GetConnectionTargets())
            Ports.Add(p);

        if (!string.IsNullOrEmpty(previous) && Ports.Contains(previous))
            SelectedPort = previous;
        else if (Ports.Count > 0)
        {
            var hbmPort = Ports.FirstOrDefault(p => HbmUsbDeviceScanner.IsHbmUsbTarget(p));
            SelectedPort = hbmPort ?? Ports[0];
        }

        RefreshHardwareSummary();
        Status = hbm.Count > 0
            ? $"Porturi: {com.Count} COM, {hbm.Count} HBM USB ({string.Join(", ", hbm.Select(d => d.Serial))}). " +
              $"Backend curent: {SelectedBackend}. Pentru demo folosiți Simulator; Spider32 USB e best-effort."
            : $"Porturi: {com.Count} COM. Niciun dispozitiv HBM USB (VID_10D1) detectat.";
        _journal.Info(Status);
    }

    private void RefreshHardwareSummary()
    {
        var hbm = HbmUsbDeviceScanner.GetDevices();
        var com = ComPortScanner.GetPorts().Count;
        var enabled = Devices.Count(d => d.Enabled);
        HardwareSummary = hbm.Count > 0
            ? $"Dispozitive HBM USB: {hbm.Count} · Sloturi: {enabled} · Canale UI: {Channels.Count} · COM: {com}"
            : $"Dispozitive: sloturi {enabled} · Canale UI: {Channels.Count} · COM: {com}";
    }

    private static readonly System.Windows.Media.Brush StatusBrushConnected = FreezeStatusBrush(0x0A, 0x7A, 0x3E);
    private static readonly System.Windows.Media.Brush StatusBrushDisconnected = FreezeStatusBrush(0x90, 0xA0, 0xB0);
    private static readonly System.Windows.Media.Brush StatusBrushError = FreezeStatusBrush(0xC8, 0x10, 0x2E);
    private static readonly System.Windows.Media.Brush StatusBrushConnecting = FreezeStatusBrush(0xE0, 0xA0, 0x00);

    private static System.Windows.Media.Brush FreezeStatusBrush(byte r, byte g, byte b)
    {
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void SetConnectStage(double progress, string stage)
    {
        ConnectProgress = Math.Clamp(progress, 0, 100);
        ConnectStageText = stage;
        Status = stage;
        RefreshConnectedDeviceLabel();
        RefreshConnectionHealth();
    }

    /// <summary>Map backend StatusChanged messages to determinate progress stages.</summary>
    private void ApplyConnectProgressFromStatus(string msg)
    {
        Status = msg;
        _journal.Info(msg);
        ParseDeviceEstFromStatus(msg);
        if (string.IsNullOrWhiteSpace(msg)) return;

        var m = msg;
        if (ContainsAny(m, "Deschid", "Opening", "detect", "Detectare", "S8_InitAll"))
            SetConnectStage(Math.Max(ConnectProgress, 25), TruncStage(msg));
        else if (ContainsAny(m, "USB deschis", "pipe", "deschis", "OpenDevice", "PORT_"))
            SetConnectStage(Math.Max(ConnectProgress, 45), TruncStage(msg));
        else if (ContainsAny(m, "EST?"))
            SetConnectStage(Math.Max(ConnectProgress, 60), TruncStage(msg));
        else if (ContainsAny(m, "IDN", "versiune", "online"))
        {
            if (m.Contains("IDN", StringComparison.OrdinalIgnoreCase))
                _lastConnectFirmware = msg;
            SetConnectStage(Math.Max(ConnectProgress, 75), TruncStage(msg));
        }
        else if (ContainsAny(m, "Conectat via", "DEST conectat", "Connected on", "Config canale", "Channel setup", "canale raportate"))
            SetConnectStage(Math.Max(ConnectProgress, 88), TruncStage(msg));
        else if (ContainsAny(m, "PORT_USB ocupat", "fallback DEST", "trec pe DEST", "Intfac nefolosit"))
            SetConnectStage(Math.Max(ConnectProgress, 40), TruncStage(msg));
        else
            ConnectStageText = TruncStage(msg);
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string TruncStage(string msg) =>
        msg.Length <= 90 ? msg : msg[..87] + "…";

    private void EndConnectProgress(bool success, bool failed = false)
    {
        IsConnecting = false;
        ConnectProgress = success ? 100 : 0;
        ConnectStageText = "";
        RaiseCommands();
        if (success)
        {
            RefreshConnectedDeviceLabel();
            RefreshConnectionHealth();
            return;
        }

        ConnectedDeviceLabel = "Deconectat";
        ConnectionStatusBrush = failed ? StatusBrushError : StatusBrushDisconnected;
        RefreshConnectionHealth();
    }

    private void RefreshConnectedDeviceLabel()
    {
        if (IsConnecting)
        {
            ConnectedDeviceLabel = string.IsNullOrWhiteSpace(ConnectStageText)
                ? "Se conectează…"
                : $"Se conectează… {ConnectStageText}";
            ConnectionStatusBrush = StatusBrushConnecting;
            RefreshConnectionHealth();
            return;
        }

        if (_device is not null && IsConnected)
        {
            var slot = Devices.FirstOrDefault(d => d.Enabled)?.Name ?? "Spider8_1";
            ConnectedDeviceLabel = $"Conectat: {slot} {_device.DisplayName}";
            ConnectionStatusBrush = StatusBrushConnected;
            RefreshConnectionHealth();
            return;
        }

        ConnectionStatusBrush = StatusBrushDisconnected;

        if (SelectedBackend == "Simulator")
        {
            ConnectedDeviceLabel = "Deconectat (Simulator)";
            RefreshConnectionHealth();
            return;
        }

        var target = SelectedPort ?? "—";
        if (HbmUsbDeviceScanner.IsHbmUsbTarget(target))
        {
            var serial = target.StartsWith("USB ", StringComparison.OrdinalIgnoreCase) ? target[4..].Trim() : target.Trim();
            ConnectedDeviceLabel = SelectedBackend is "Spider32.dll" or "HBM USB"
                ? $"Deconectat — Spider8 [USB {serial}] disponibil"
                : $"Deconectat — USB {serial} detectat (alegeți HBM USB / Spider32.dll)";
            RefreshConnectionHealth();
            return;
        }

        ConnectedDeviceLabel = string.IsNullOrWhiteSpace(SelectedPort)
            ? "Deconectat"
            : $"Deconectat — țintă {SelectedPort} ({SelectedBackend})";
        RefreshConnectionHealth();
    }

    private async Task ConnectAsync()
    {
        if (IsConnecting) return;
        IsConnecting = true;
        ConnectProgress = 5;
        ConnectStageText = "Detectare dispozitiv…";
        Status = ConnectStageText;
        RaiseCommands();
        RefreshConnectedDeviceLabel();
        RefreshConnectionHealth();

        try
        {
            await DisconnectAsync();
            _lastConnectFirmware = null;
            _internalShuntFromSpider830 = false;
            _communicationLost = false;
            SetChannelsLinkLost(false);
            var port = SelectedPort ?? Devices.FirstOrDefault(d => d.Enabled)?.ComPort;
            var isHbmUsb = HbmUsbDeviceScanner.IsHbmUsbTarget(port);

            // Simulator Connect must ignore USB/COM selection — rock-solid demo path.
            if (SelectedBackend == "Simulator")
            {
                port = null;
                isHbmUsb = false;
            }

            if (SelectedBackend == "Serial" && isHbmUsb)
            {
                Status =
                    $"Ținta {port} este HBM USB IO (usbhbm), nu port COM RS232. " +
                    "Pentru hardware: backend HBM USB / Spider32.dll, sau adaptor Serial pe COM real. " +
                    "Pentru demo: treceți pe Simulator → Connect → Start.";
                _journal.Warn(Status);
                ConnectionStatusBrush = StatusBrushError;
                EndConnectProgress(false, failed: true);
                return;
            }

            // USBHBM + catman Easy open = exclusive pipe conflict (document, don't thrash).
            if ((SelectedBackend is "HBM USB" or "Spider32.dll") && isHbmUsb
                && IsCatmanEasyRunning())
            {
                Status =
                    "catman Easy rulează și ține USBHBM deschis. Închideți catmanEASY.exe, " +
                    "apoi Connect din nou — sau folosiți Simulator pentru demo lab.";
                _journal.Warn(Status);
                ConnectionStatusBrush = StatusBrushError;
                HelpPanelText =
                    "USBHBM: un singur client. catman Easy SAU UPET (Intfac/DEST), nu ambele. " +
                    "Simulator = calea oficială demo fără hardware.";
                EndConnectProgress(false, failed: true);
                return;
            }

            // Spider32.dll + USBHBM → DeviceFactory prefers Intfac32 (catman), not DEST ACT/ASA.
            if (SelectedBackend.Contains("Spider32", StringComparison.OrdinalIgnoreCase)
                && !isHbmUsb)
            {
                var dllPath = Path.Combine(AppContext.BaseDirectory, "vendor", "Spider32.dll");
                if (!File.Exists(dllPath))
                {
                    Status =
                        "Spider32.dll lipsește din vendor\\. Pentru COM/RS232: copiați DLL HBM. " +
                        "Pentru USBHBM: selectați ținta USBHBM… (nu e nevoie de Spider32 pe USB). " +
                        "Demo: Simulator → Connect → Start.";
                    _journal.Warn(Status);
                    ConnectionStatusBrush = StatusBrushError;
                    EndConnectProgress(false, failed: true);
                    return;
                }
            }

            SetConnectStage(15, "Pregătire backend…");

            var backend = SelectedBackend switch
            {
                "Serial" => DeviceBackend.Serial,
                "Spider32.dll" when isHbmUsb => DeviceBackend.HbmUsb,
                "Spider32.dll" => DeviceBackend.Spider32Dll,
                "HBM USB" => DeviceBackend.HbmUsb,
                _ => DeviceBackend.Simulator
            };
            var deviceCount = DeviceFactory.CountEnabledDevices(Devices.Select(d => new DeviceSlot
            {
                Index = d.Index, Name = d.Name, Enabled = d.Enabled, ComPort = d.ComPort
            }));

            // If HBM USB / Spider32 + USBHBM present but combo empty, pick first HBM serial.
            if ((backend is DeviceBackend.Spider32Dll or DeviceBackend.HbmUsb)
                && string.IsNullOrWhiteSpace(port)
                && HbmUsbDeviceScanner.IsUsbHbmPresent())
            {
                port = HbmUsbDeviceScanner.GetDevices()[0].Serial;
                SelectedPort = port;
            }

            // Normalize "USB USBHBM…" → serial for port hint.
            var portHint = port;
            if (portHint is not null && portHint.StartsWith("USB ", StringComparison.OrdinalIgnoreCase))
                portHint = portHint[4..].Trim();

            SetConnectStage(20, backend == DeviceBackend.HbmUsb
                ? "Detectare USBHBM / Intfac32 (catman)…"
                : backend == DeviceBackend.Simulator
                    ? "Pornire simulator…"
                    : "Deschidere conexiune…");

            _device = DeviceFactory.Create(new DeviceFactoryOptions
            {
                Backend = backend,
                ComPort = portHint,
                BaudRate = 9600,
                ChannelCount = Channels.Count,
                DeviceCount = deviceCount,
                Spider32DllPath = Path.Combine(AppContext.BaseDirectory, "vendor", "Spider32.dll"),
                Slots = Devices.Select(d => new DeviceSlot
                {
                    Index = d.Index,
                    Name = d.Name,
                    Enabled = d.Enabled,
                    ComPort = string.IsNullOrWhiteSpace(d.ComPort) ? portHint : d.ComPort,
                    Notes = d.Notes
                }).ToList()
            });
            WireDeviceEvents(_device);
            _device.SampleRateHz = SampleRateHz;
            // Native USB/DLL can block; run off-UI with 15s watchdog + one soft retry on USB.
            var connectTask = Task.Run(() => _device.ConnectAsync());
            var finished = await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(15)));
            if (finished != connectTask)
            {
                Status = "Connect timeout 15s. Închideți catman Easy, deconectați-reconectați USB, reîncercați.";
                _journal.Error(Status);
                try { await _device.DisposeAsync(); } catch { /* ignore */ }
                _device = null;
                IsConnected = false;
                ConnectionStatusBrush = StatusBrushError;
                EndConnectProgress(false, failed: true);
                return;
            }

            try
            {
                await connectTask;
            }
            catch (Exception firstEx) when (backend == DeviceBackend.HbmUsb || backend == DeviceBackend.Serial)
            {
                // Soft retry once after brief settle (USB pipe race / Serial port busy).
                SetConnectStage(35, backend == DeviceBackend.Serial
                    ? "Reîncerc Serial după 500 ms…"
                    : "Reîncerc USB după 600 ms…");
                Status = "Connect eșuat o dată — reîncerc… " + TruncStage(firstEx.Message);
                _journal.Warn(Status);
                try { await _device.DisposeAsync(); } catch { /* ignore */ }
                await Task.Delay(backend == DeviceBackend.Serial ? 500 : 600);
                _device = DeviceFactory.Create(new DeviceFactoryOptions
                {
                    Backend = backend,
                    ComPort = portHint,
                    BaudRate = 9600,
                    ChannelCount = Channels.Count,
                    DeviceCount = deviceCount,
                    Spider32DllPath = Path.Combine(AppContext.BaseDirectory, "vendor", "Spider32.dll"),
                    Slots = Devices.Select(d => new DeviceSlot
                    {
                        Index = d.Index,
                        Name = d.Name,
                        Enabled = d.Enabled,
                        ComPort = string.IsNullOrWhiteSpace(d.ComPort) ? portHint : d.ComPort,
                        Notes = d.Notes
                    }).ToList()
                });
                WireDeviceEvents(_device);
                _device.SampleRateHz = SampleRateHz;
                var retry = Task.Run(() => _device.ConnectAsync());
                var done = await Task.WhenAny(retry, Task.Delay(TimeSpan.FromSeconds(15)));
                if (done != retry)
                    throw new TimeoutException("Connect retry timeout 15s.");
                await retry;
            }

            SetConnectStage(90, "Configurare canale…");
            SyncDigitalChannelFromDevice();
            if (TimbruAutorange)
                TryApplyStrainAutorange(push: false, preferLive: true);
            await _device.ApplyChannelConfigAsync(Channels.Select(ToConfig));
            PushSimulatorScenarioHint();
            SetConnectStage(96, "Canale gata…");
            _engine.Attach(_device);
            SyncMathToEngine();
            IsConnected = true;
            EndConnectProgress(true);
            RefreshHardwareSummary();
            ApplyMetrologyFilterConfig();
            RunConnectMetrologyChecks();
            var rshStatus = ApplyInternalShuntFromDevice();
            var analogCount = Channels.Count(c =>
                !c.Name.Contains("DI", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(c.Unit, "bitmask", StringComparison.OrdinalIgnoreCase));
            var hasDi = Channels.Any(c =>
                c.Name.Contains("DI", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.Unit, "bitmask", StringComparison.OrdinalIgnoreCase));
            Status = (hasDi
                ? $"{ConnectedDeviceLabel} — {analogCount} canale analogice (CH0–CH{Math.Max(0, analogCount - 1)}); CH8 DI = digital bitmask."
                : $"{ConnectedDeviceLabel} — {analogCount} canale analogice (0–7 / CH0–CH7).")
                + " Conectat — apăsați Start pentru semnal.";
            if (!string.IsNullOrWhiteSpace(rshStatus))
                Status += " · " + rshStatus;
            if (!string.IsNullOrWhiteSpace(MetrologyDiagText) && MetrologyDiagText.Contains("suspect", StringComparison.OrdinalIgnoreCase))
                Status += " · " + MetrologyDiagText.Split('\n')[0];
            _journal.Info(Status);
            RebuildPlotSeries();
            StartWatchdog();
        }
        catch (OperationCanceledException)
        {
            Status = "Connect eșuat — timeout (fără răspuns Spider8). Verificați USB/catman.";
            _journal.Error(Status);
            IsConnected = false;
            ConnectionStatusBrush = StatusBrushError;
            EndConnectProgress(false, failed: true);
        }
        catch (Exception ex)
        {
            var tip = "";
            if (SelectedBackend is "HBM USB" or "Spider32.dll")
            {
                tip = IsCatmanEasyRunning()
                    ? " Închideți catman Easy, apoi Connect. Alternativ: Simulator."
                    : " Pași: închideți catman → power-cycle Spider8 (LED ERROR) → backend HBM USB → Connect. Demo: Simulator.";
            }
            Status = $"Connect eșuat — fără dispozitiv/driver. {ex.Message}.{tip}";
            _journal.Error(Status);
            IsConnected = false;
            ConnectionStatusBrush = StatusBrushError;
            EndConnectProgress(false, failed: true);
        }
        finally
        {
            IsConnecting = false;
            RaiseCommands();
            RefreshConnectionHealth();
        }
    }

    private void WireDeviceEvents(ISpider8Device device)
    {
        device.StatusChanged += (_, msg) => _dispatcher.Invoke(() => ApplyConnectProgressFromStatus(msg));
        device.ConnectionLost += OnDeviceConnectionLost;
    }

    private void OnDeviceConnectionLost(object? sender, EventArgs e)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => OnDeviceConnectionLost(sender, e));
            return;
        }
        _ = HandleConnectionLostAsync();
    }

    private async Task HandleConnectionLostAsync()
    {
        if (Interlocked.Exchange(ref _handlingConnectionLost, 1) != 0) return;
        try
        {
            StopWatchdog();
            _communicationLost = true;

            if (IsRecording)
            {
                try { await StopRecordingAsync(skipConfirm: true); } catch { /* ignore */ }
            }

            IsStreaming = false;

            try { _engine.Detach(); } catch { /* ignore */ }

            var dev = _device;
            _device = null;
            if (dev is not null)
            {
                try { await dev.DisposeAsync(); } catch { /* ignore */ }
            }

            IsConnected = false;
            if (DisplayFrozen) DisplayFrozen = false;
            foreach (var ch in Channels)
                ch.LiveReading = "—";
            SetChannelsLinkLost(true);

            Status = "Comunicare pierdută";
            HealthText = "Comunicare pierdută";
            ConnectionStatusBrush = StatusBrushError;
            ConnectProgress = 0;
            ConnectStageText = "";
            RefreshConnectedDeviceLabel();
            RefreshConnectionHealth();
            _journal.Error("Comunicare pierdută — USB/Spider8 indisponibil. Apăsați Conn după power-on.");
        }
        catch (Exception ex)
        {
            try { _journal.Error("Comunicare pierdută (cleanup): " + ex.Message); } catch { /* ignore */ }
        }
        finally
        {
            Interlocked.Exchange(ref _handlingConnectionLost, 0);
        }
    }

    /// <summary>catman Easy holds USBHBM exclusively — detect before OpenPort/DEST.</summary>
    private static bool IsCatmanEasyRunning() => Intfac32Native.IsCatmanEasyRunning();

    /// <summary>If backend reports DigIO (IDS_S8DigIO / Spider8 CH8), mirror it as CH8 DI.</summary>
    private void SyncDigitalChannelFromDevice()
    {
        if (_device is not IDigitalIoChannel dig || dig.DigitalChannelIndex is not int di)
            return;

        while (Channels.Count <= di)
        {
            var i = Channels.Count;
            var newRow = new ChannelRow
            {
                Index = i,
                DeviceIndex = i / 8,
                Name = FormatAnalogChannelName(i, Math.Max(Channels.Count + 1, 9)),
                Unit = "mV/V",
                Enabled = false,
                ShowOnPlot = false,
                Scale = 1,
                Bridge = nameof(BridgeType.None),
                ChannelSampleRateHz = SampleRateHz
            };
            newRow.PropertyChanged += OnChannelRowPropertyChanged;
            Channels.Add(newRow);
            LiveValues.Add(new LiveValueRow { Name = newRow.Name, Unit = "mV/V", Display = "—" });
        }

        var row = Channels[di];
        row.Name = di == 8 ? "CH8 DI" : $"CH{di} DI";
        row.Unit = "bitmask";
        row.Bridge = nameof(BridgeType.None);
        // Slot exists for DigIO — leave Off so SoftSetup/LiveValues stay on analog On mask.
        // User can tick On if they need the digital bitmask live.
        row.Enabled = false;
        row.ShowOnPlot = false;
        if (LiveValues.Count > di)
            LiveValues[di].Name = row.Name;
        Status = $"Canal digital {row.Name} detectat (index HW {di}; bifă On dacă e nevoie).";
        _journal.Info(Status);
    }

    private async Task DisconnectAsync()
    {
        CancelMacro();
        StopWatchdog();
        _communicationLost = false;
        SetChannelsLinkLost(false);
        await StopRecordingAsync(skipConfirm: true);
        await StopAsync();
        _engine.Detach();
        if (_device is not null)
        {
            await _device.DisposeAsync();
            _device = null;
        }
        IsConnected = false;
        if (DisplayFrozen) DisplayFrozen = false;
        foreach (var ch in Channels)
            ch.LiveReading = "—";
        // Don't clobber connecting progress when ConnectAsync cleans up first.
        if (!IsConnecting)
        {
            Status = "Deconectat.";
            ConnectionStatusBrush = StatusBrushDisconnected;
            ConnectProgress = 0;
            ConnectStageText = "";
        }
        RefreshConnectedDeviceLabel();
    }

    private async Task StartAsync()
    {
        if (_device is null) return;
        _replayMode = false;
        if (SanitizeTimbruScales())
            Status = ShuntCheck.ScaleInvalidLabel + " — Scale restaurat la 4000/GF. Domeniul Autorange rămâne în Capacity.";
        if (TimbruAutorange)
        {
            _autorangeSessionPeak = 0;
            _autorangePickedFromLive = false;
            TryApplyStrainAutorange(push: false, preferLive: true);
        }
        await _device.ApplyChannelConfigAsync(Channels.Select(ToConfig));
        DrawContourSimAssignmentIfNeeded();
        PushSimulatorScenarioHint();
        SyncMathToEngine();
        ApplyMetrologyFilterConfig();
        RefreshLiveValueHeaders();
        RebuildPlotSeries();
        _device.SampleRateHz = SampleRateHz;
        try
        {
            await _device.StartStreamingAsync();
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("power-cycle", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("Power-cycle", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("LED ERROR", StringComparison.OrdinalIgnoreCase))
        {
            IsStreaming = false;
            Status = "Power-cycle Spider8 — LED ERROR";
            _journal.Error(Status);
            return;
        }
        IsStreaming = true;
        FollowLiveZoom = true;
        ResetCatmanLiveAxis();
        ResetAllChannelPlotScales();
        var liveCh = Channels
            .Where(c => c.Enabled && !c.Name.Contains("DI", StringComparison.OrdinalIgnoreCase))
            .Select(c => $"{c.Name}({c.Bridge})");
        var nOm = Channels.Count(c => c.Enabled && !c.Name.Contains("DI", StringComparison.OrdinalIgnoreCase));
        Status = $"Live OK — OMB activ pe {nOm} canale @ {SampleRateHz} Hz — {string.Join(", ", liveCh.DefaultIfEmpty("CH?"))}.";
        if (_contourSimAssignment is { } asg)
            Status += " · " + asg.ToSummaryRo();
        _journal.Info(Status);
        _streamingStartedUtc = DateTime.UtcNow;
        _flatSampleWarned = false;
        _framesAtStart = _engine.FramesReceived;
        LogLiveAcquisitionDiagnostics();
        StartWatchdog();
        ScheduleFlatSampleWatch();
        if (TimbruAutorange && !_autorangePickedFromLive)
            _autorangePendingLive = true;
    }

    private async Task StopAsync()
    {
        // Finalize CSV first — otherwise the writer keeps an exclusive write handle and
        // Journal shows "(citire eșuată)" even though the file already has samples.
        if (IsRecording)
            await StopRecordingAsync(skipConfirm: true);
        if (_device is null) return;
        StopWatchdog();
        await _device.StopStreamingAsync();
        IsStreaming = false;
        Status = "Măsurare oprită.";
    }

    private async Task TareAsync(int? channelIndexOrNull)
    {
        if (_device is null) return;
        if (!IsStreaming)
            Status = "Zero: Start (F5) recomandat — folosesc ultima citire disponibilă.";

        int? index = channelIndexOrNull is int ui ? ResolveHwIndex(ui) : null;

        void SoftZero(ChannelRow ch)
        {
            PushChannelEditUndo(ch);
            // Soft Zero like catman: adjust TareValue in raw units; Scale unchanged.
            // Do NOT send hardware TAR — that zeros device raw and breaks DcVoltage/P15 live stream.
            var scale = Math.Max(1e-12, ch.Scale);
            var latest = Volatile.Read(ref _latestUiSample);
            if (latest is not null
                && ch.Index >= 0
                && ch.Index < latest.Frame.Values.Length)
            {
                var raw = latest.Frame.Values[ch.Index];
                if (!double.IsNaN(raw) && !double.IsInfinity(raw))
                {
                    // Target physical = 0 → TareValue = raw + Offset/Scale (raw=0 after P15/atmos is valid)
                    ch.TareValue = raw + ch.Offset / scale;
                    return;
                }
            }

            if (TryParseLiveDisplay(ch, out var physical))
                ch.TareValue += physical / scale;
        }

        if (index is int idx)
            SoftZero(Channels[idx]);
        else
        {
            foreach (var ch in Channels.Where(c => c.Enabled))
                SoftZero(ch);
        }

        await _device.ApplyChannelConfigAsync(Channels.Select(ToConfig));
        // Recentre Y axis; keep DataLoggers alive so live plot keeps scrolling after Zero.
        ResetCatmanLiveAxis();
        RefreshUiAfterTare();
        var msg = index is null
            ? $"Zero balance (soft) — Scale păstrat ({Channels.FirstOrDefault(c => c.Enabled)?.Scale:G6})."
            : $"Zero balance {Channels[index.Value].Name} (HW{index.Value}) — Scale={Channels[index.Value].Scale:G6} păstrat.";
        Status = msg;
        _journal.Setup(msg);
        MarkSessionZeroed();
        NotifyMetrologyZero();
        RefreshWorkflowStepsOnly();
    }

    /// <summary>Recompute Physical with updated TareValue and push one UI tick (no wait for next frame).</summary>
    private void RefreshUiAfterTare()
    {
        var latest = Volatile.Read(ref _latestUiSample);
        if (latest is null || _device is null) return;

        var physical = new double[_device.Channels.Count];
        for (var i = 0; i < _device.Channels.Count; i++)
        {
            var ch = _device.Channels[i];
            var raw = i < latest.Frame.Values.Length ? latest.Frame.Values[i] : double.NaN;
            physical[i] = ch.Enabled ? ch.Apply(raw) : double.NaN;
        }

        // Settle software LPF to post-tare physical so Zero doesn't lag through the filter.
        _engine.Metrology.SettleFilters(physical);

        var combined = physical.Concat(latest.Math).Concat(latest.Compute).ToArray();
        var refreshed = new ProcessedSample(latest.Frame, physical, latest.Math, latest.Compute, combined);
        Volatile.Write(ref _latestUiSample, refreshed);
        ApplySampleToUi(refreshed);
        _plotA?.Refresh();
        _plotB?.Refresh();
    }

    private bool TryParseLiveDisplay(ChannelRow ch, out double physical)
    {
        physical = double.NaN;
        var live = LiveValues.FirstOrDefault(v => v.Name == ch.Name);
        if (live is not null
            && double.TryParse(live.Display?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out physical)
            && !double.IsNaN(physical))
            return true;
        return double.TryParse(ch.LiveReading?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out physical)
               && !double.IsNaN(physical);
    }

    private async Task RunShuntCheckAsync()
    {
        if (_device is null) return;
        var idx = ResolveHwIndexFromUiSelector(SelectedChannelForSensor);
        if (idx is null)
        {
            Status = "Selectează CH# valid (CH0–CH7 în caseta Apply) pentru shunt.";
            return;
        }
        var ch = Channels[idx.Value];
        if (ch.Name.Contains("DI", StringComparison.OrdinalIgnoreCase))
        {
            Status = $"{ch.Name}: fără shunt intern (digital I/O, nu analog CF).";
            LastShuntStatus = Status;
            return;
        }
        var reading = await _device.ShuntCheckAsync(idx.Value);
        reading = FallbackShuntReading(ch, reading);
        var result = ApplyShuntEvaluation(ch, reading);
        Status = result.StatusLine;
        _journal.Setup(Status);
        HelpPanelText = Status;
        await _device.ApplyChannelConfigAsync(Channels.Select(ToConfig));
        CommandManager.InvalidateRequerySuggested();
    }

    private ShuntCheckResult ApplyShuntEvaluation(ChannelRow ch, double reading)
    {
        var result = ShuntCheck.Evaluate(BuildShuntRequest(ch, reading));
        ch.ShuntEnabled = true;
        if (!double.IsNaN(result.MeasuredUe))
            ch.LastShuntReading = result.MeasuredUe;
        else if (!double.IsNaN(reading))
            ch.LastShuntReading = reading;
        ch.LastShuntExpectedUe = result.ExpectedUe;
        ch.LastShuntCanApplyScale = result.CanApplyScale;
        LastShuntStatus = result.StatusLine;
        return result;
    }

    private ShuntCheckRequest BuildShuntRequest(ChannelRow ch, double reading) => new()
    {
        ChannelName = ch.Name,
        Reading = reading,
        ShuntKohm = ch.ShuntKohm,
        GaugeFactor = ch.GaugeFactor,
        GaugeOhm = ch.GaugeOhm,
        Bridge = ch.Bridge,
        HalfConfig = ch.HalfConfig,
        PoissonRatio = ch.PoissonRatio > 0 ? ch.PoissonRatio : 0.3,
        Unit = ch.Unit,
        Simulator = IsSimulatedShuntBackend(),
        TolerancePercent = ShuntAllowedDifferencePercent,
        DeviceInError = ShuntCheck.LooksLikeEstLedError(DeviceErrorText),
        ChannelScale = ch.Scale,
        BackendHint = _device?.DisplayName ?? SelectedBackend
    };

    /// <summary>
    /// DEST/Spider32 may return NaN after SH1 if the OMB poller stole the snapshot.
    /// Use live raw electrical (not Citire×polluted Scale). Do not invent PASS.
    /// </summary>
    private double FallbackShuntReading(ChannelRow ch, double reading)
    {
        if (!double.IsNaN(reading) && !double.IsInfinity(reading))
            return reading;

        var latest = Volatile.Read(ref _latestUiSample);
        if (latest is not null
            && ch.Index >= 0
            && ch.Index < latest.Frame.Values.Length)
        {
            var raw = latest.Frame.Values[ch.Index];
            if (!double.IsNaN(raw) && !double.IsInfinity(raw) && Math.Abs(raw) > 1e-6)
                return raw;
        }

        // Citire is engineering. Invert with GF Scale only when grid Scale is the GF formula
        // (91885 × residual would invent tens of mV/V).
        var formula = ResolveTimbruFormulaScale(ch);
        if (TryParseLiveDisplay(ch, out var physical)
            && Math.Abs(physical) >= 50
            && formula >= 10
            && !StrainScale.LooksAbsurdTimbruScale(ch.Scale, formula))
            return physical / formula;

        return reading;
    }

    private bool IsSimulatedShuntBackend() =>
        string.Equals(SelectedBackend, "Simulator", StringComparison.OrdinalIgnoreCase)
        || _device is SimulatedSpider8;

    private string? ResolveConnectFirmware()
    {
        if (_device is IDeviceHealth health && !string.IsNullOrWhiteSpace(health.FirmwareHint))
            return health.FirmwareHint;
        return _lastConnectFirmware;
    }

    /// <summary>
    /// Fill empty Rsh on Timbru/strain channels from Spider8-30 internal shunt table (HBM ASS).
    /// Simulator: no fill. Value is never queried in ohms — IDN detects the model.
    /// </summary>
    private string ApplyInternalShuntFromDevice()
    {
        _internalShuntFromSpider830 = false;
        if (_device is null || IsSimulatedShuntBackend())
            return "";

        var firmware = ResolveConnectFirmware();
        var probe = Spider8InternalShunt.Resolve(firmware, 350);
        if (probe.Kind == Spider8InternalShuntKind.Simulator)
            return probe.StatusLabel;
        if (!probe.CanAutoFill)
            return string.IsNullOrWhiteSpace(firmware) ? "" : probe.StatusLabel;

        _internalShuntFromSpider830 = true;
        var filled = 0;
        double used = probe.Kohm;
        foreach (var ch in Channels)
        {
            if (!IsStrainOrTimbruChannel(ch)) continue;
            var res = Spider8InternalShunt.Resolve(firmware, ch.GaugeOhm);
            if (!res.CanAutoFill) continue;
            if (!ch.TryApplyInternalShuntKohm(res.Kohm)) continue;
            filled++;
            used = res.Kohm;
        }

        if (filled == 0)
            return probe.StatusLabel;

        var inv = CultureInfo.InvariantCulture;
        return "Rsh = " + used.ToString("0.##", inv) + " kΩ (intern Spider8-30)"
               + (filled > 1 ? " · " + filled + " canale" : "");
    }

    internal static bool IsStrainOrTimbruChannel(ChannelRow ch)
    {
        if (string.Equals(ch.Unit, "bitmask", StringComparison.OrdinalIgnoreCase))
            return false;
        if (StrainScale.IsStrainUnit(ch.Unit)) return true;
        if (ch.GaugeFactor > 1e-9) return true;
        return ch.SensorCategory is not null
               && ch.SensorCategory.Contains("tensomet", StringComparison.OrdinalIgnoreCase);
    }

    private void TryFillInternalShuntOnTimbruChannel(ChannelRow ch)
    {
        if (!_internalShuntFromSpider830 && !Spider8InternalShunt.IsSpider830(ResolveConnectFirmware()))
            return;
        var res = Spider8InternalShunt.Resolve(ResolveConnectFirmware() ?? "Spider8-30", ch.GaugeOhm);
        if (res.CanAutoFill)
            ch.TryApplyInternalShuntKohm(res.Kohm);
    }

    private void ApplyScaleFromShunt()
    {
        var ch = ResolveSelectedHwChannel();
        if (ch is null)
        {
            Status = "Selectați un canal pentru Aplică Scale din shunt.";
            return;
        }
        if (!ch.LastShuntCanApplyScale
            || double.IsNaN(ch.LastShuntExpectedUe)
            || double.IsNaN(ch.LastShuntReading)
            || Math.Abs(ch.LastShuntReading) < 1e-12)
        {
            Status = "Aplică Scale din shunt: rulați Shunt CH pe un canal µm/m cu rezultat PASS (sau aproape ±20%).";
            return;
        }
        if (!StrainScale.IsStrainUnit(ch.Unit))
        {
            Status = "Aplică Scale din shunt: doar canale µm/m (Scale forță/OMB nu se atinge).";
            return;
        }

        var formula = ResolveTimbruFormulaScale(ch);
        if (StrainScale.LooksAbsurdTimbruScale(ch.Scale, formula))
        {
            Status = ShuntCheck.ScaleInvalidLabel + " — "
                     + StrainScale.SuggestedTimbruScaleLabel(formula)
                     + ". Nu Aplică Scale din shunt până Scale e 4000/GF.";
            return;
        }

        var next = ShuntCheck.ScaleAfterShunt(
            ch.Scale, ch.LastShuntExpectedUe, ch.LastShuntReading, formula);
        if (StrainScale.LooksAbsurdTimbruScale(next, formula))
        {
            Status = "Aplică Scale din shunt anulat — rezultatul ar fi Scale invalid. Aplică Timbru (4000/GF).";
            return;
        }
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                    ?? Application.Current?.MainWindow;
        var msg =
            $"{ch.Name}: Scale {ch.Scale:0.####} → {next:0.####} µm/m per mV/V\n\n" +
            $"măsurat {ch.LastShuntReading:0} µm/m, așteptat {ch.LastShuntExpectedUe:0} µm/m.\n" +
            "Scale × (așteptat/măsurat). Nu se schimbă GF Timbru pe canal — doar scara de afișare.\n\nContinuați?";
        var dlg = new Controls.SoftConfirmWindow("Aplică Scale din shunt", msg, confirmLabel: "Aplică Scale")
        {
            Owner = owner
        };
        if (dlg.ShowDialog() != true || !dlg.Confirmed)
        {
            Status = "Aplică Scale din shunt anulat.";
            return;
        }

        PushChannelEditUndo(ch);
        ch.Scale = next;
        ch.LastShuntCanApplyScale = false;
        if (_device is not null && IsConnected)
            _ = PushChannelConfigAsync(reapplyAcquisition: false);
        Status = $"Scale din shunt pe {ch.Name}: Scale={ch.Scale:0.####} (Ctrl+Z = Undo).";
        _journal.Setup(Status);
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task StartRecordingAsync(bool skipExperimentWarning = false)
    {
        if (_device is null) return;
        if (!IsStreaming)
        {
            Status = "Porniți măsurarea (Start / F5) înainte de Record.";
            return;
        }

        if (!skipExperimentWarning &&
            !IsExperimentActive &&
            !RecordingUiPrefs.SkipNoExperimentWarning)
        {
            var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? Application.Current?.MainWindow;
            var warn = new Controls.SoftConfirmWindow(
                "Record fără experiment",
                "Fără experiment — CSV fără meta/poze de montaj.\n\n" +
                "Puteți continua înregistrarea acum, sau anulați și porniți întâi un experiment nou (Nou).\n\n" +
                "Continuați?",
                confirmLabel: "Continuați Rec")
            {
                Owner = owner
            };
            if (warn.ShowDialog() != true || !warn.Confirmed)
            {
                Status = "Record anulat — porniți un experiment nou (Nou) dacă doriți meta/poze, apoi Rec.";
                return;
            }

            if (warn.DontAskAgain)
                RecordingUiPrefs.SkipNoExperimentWarning = true;
        }

        try
        {
            var dir = GetWritableRecordingsDirectory();
            Directory.CreateDirectory(dir);
            string path;
            if (AutoFileName)
            {
                var stamp = IsExperimentActive && ExperimentStartedAt is { } expStamp
                    ? expStamp
                    : DateTime.Now;
                var name = ExperimentFileNaming.BuildFileName(SampleId, stamp, role: null, ".csv");
                path = ExperimentFileNaming.UniquePath(Path.Combine(dir, name));
            }
            else
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "CSV (*.csv)|*.csv",
                    InitialDirectory = dir,
                    FileName = "recording.csv"
                };
                if (dlg.ShowDialog() != true) return;
                path = dlg.FileName;
            }

            // Headers MUST match AcquisitionEngine packed layout (Rec flag + PeakInterval).
            SyncMathToEngine();
            SyncComputeToEngine();
            var recordIndices = new List<int>();
            var headers = new List<string>();
            var peak = string.Equals(StorageMode, "PeakInterval", StringComparison.OrdinalIgnoreCase);
            for (var i = 0; i < Channels.Count; i++)
            {
                var c = Channels[i];
                if (!c.Enabled || !c.RecordEnabled) continue;
                recordIndices.Add(i);
                var label = ExportLabels.ChannelHeader(c.Name, c.Unit);
                if (peak)
                {
                    headers.Add(label + "_min");
                    headers.Add(label + "_max");
                }
                else headers.Add(label);
            }
            var mathOffset = Channels.Count;
            for (var i = 0; i < MathChannels.Count; i++)
            {
                var m = MathChannels[i];
                if (!m.Enabled) continue;
                recordIndices.Add(mathOffset + i);
                var label = ExportLabels.ChannelHeader(m.Name, m.Unit);
                if (peak)
                {
                    headers.Add(label + "_min");
                    headers.Add(label + "_max");
                }
                else headers.Add(label);
            }
            var computeOffset = Channels.Count + MathChannels.Count;
            for (var i = 0; i < ComputeRows.Count; i++)
            {
                var c = ComputeRows[i];
                if (!c.Enabled) continue;
                recordIndices.Add(computeOffset + i);
                if (peak)
                {
                    headers.Add(c.Name + "_min");
                    headers.Add(c.Name + "_max");
                }
                else headers.Add(c.Name);
            }
            if (headers.Count == 0)
            {
                Status = "Niciun canal cu Rec activ — bifați Rec pe canalele de salvat.";
                return;
            }
            _engine.Trigger = new TriggerSettings
            {
                Enabled = TriggerEnabled,
                ChannelIndex = Math.Max(0, TriggerChannel - 1),
                Threshold = TriggerThreshold,
                RisingEdge = TriggerRising
            };
            _engine.Recording = new RecordingSettings
            {
                PreTriggerSamples = PreTriggerSamples,
                AppendMode = AppendMode,
                AutoFileName = AutoFileName,
                StorageMode = StorageMode,
                PeakIntervalSeconds = PeakIntervalSeconds
            };
            _engine.MaxSamples = MaxSamples;
            _engine.MaxDuration = MaxSeconds is int s ? TimeSpan.FromSeconds(s) : null;
            _engine.StatisticsJournalEnabled = StatsJournalEnabled;
            _engine.StatisticsIntervalSeconds = StatsIntervalSeconds;
            _engine.StopRecordingOnAlarm = StopRecordOnAlarm;
            _engine.ThresholdStopEnabled = RecordStopMode == "Threshold";
            _engine.ThresholdChannelIndex = RecordThresholdChannel;
            _engine.ThresholdValue = RecordThresholdValue;
            SyncAdvancedTrigger();
            var projectMeta = CurrentProjectMeta();
            // ContourConfig in CSV must use packed Rec column indices (not hardware CH),
            // otherwise Excel Contur fails with «CH1 invalid» when only CH0 was recorded.
            if (projectMeta.CylinderContour is { } contourHw)
            {
                var hwRec = recordIndices.Where(i => i >= 0 && i < Channels.Count).ToList();
                var packed = new CylinderContourConfig
                {
                    SensorCount = contourHw.SensorCount,
                    SensorChannelIndices = contourHw.SensorChannelIndices.ToList(),
                    SensorAnglesDeg = contourHw.SensorAnglesDeg.ToList(),
                    ForceChannelIndex = contourHw.ForceChannelIndex,
                    StrokeChannelIndex = contourHw.StrokeChannelIndex,
                    InitialRadiusMm = contourHw.InitialRadiusMm
                };
                if (!packed.TryRemapHardwareToPacked(hwRec, out _))
                    packed.MapToAvailableChannels(hwRec.Count);
                projectMeta.CylinderContour = packed;
            }
            var meta = new[]
            {
                $"UPET AcqLab {AppVersion}",
                $"Operator={OperatorName}",
                $"Sample={SampleId}",
                $"Backend={SelectedBackend}",
                $"RateHzSet={SampleRateHz}",
                $"Storage={StorageMode}",
                $"PeakIntervalSec={PeakIntervalSeconds:0.###}",
                $"Comment={Comment}",
                "Note=t_s = seconds from first sample; RateHzEffective written at stop"
            }.Concat(CylinderContourExport.BuildRecordingMetaExtraLines(projectMeta))
             .Concat(Spider8DAQ.Core.Specimens.SpecimenIdentification.BuildCsvCommentLines(projectMeta))
             .ToArray();
            if (TimbruAutorange)
                LockStrainAutorangeForRecord();
            await _engine.ArmRecordingAsync(path, headers, recordIndices, meta);
            LastRecordingPath = path;
            IsRecording = true;
            OnRecordingStartedForIntegrations(path);
            RefreshWorkflowStepsOnly();
            var mode = RecordStopMode;
            var storageHint = peak ? $" · Peak {PeakIntervalSeconds:0.##}s" : "";
            Status = mode switch
            {
                "Duration" => $"Înregistrare {MaxSeconds}s{storageHint} → {path}",
                "Samples" => $"Înregistrare {MaxSamples} eșantioane{storageHint} → {path}",
                "Trigger" => $"Înregistrare armată (trigger, pre={PreTriggerSamples}){storageHint} → {path}",
                "Threshold" => $"Înregistrare până |CH{RecordThresholdChannel}| ≥ {RecordThresholdValue:0.####}{storageHint} → {path}",
                _ => $"Înregistrare (stop manual){storageHint} → {path}"
            };
            StatsJournalStatus = StatsJournalEnabled
                ? $"Jurnal statistici ON ({StatsIntervalSeconds:0.#}s)"
                : "Jurnal statistici OFF";
            _journal.Info(Status);
        }
        catch (Exception ex)
        {
            IsRecording = false;
            UnlockStrainAutorangeAfterRecord();
            Status = "Record eșuat: " + AppPaths.FriendlyIoMessage(ex, GetWritableRecordingsDirectory());
            _journal.Error(Status);
        }
    }

    private async Task StopRecordingAsync(bool skipConfirm = false)
    {
        if (IsRecording && !skipConfirm)
        {
            var n = _engine.RecordedSamples;
            var path = string.IsNullOrWhiteSpace(LastRecordingPath) ? "(cale necunoscută)" : LastRecordingPath;
            var enabled = Channels.Count(c => c.Enabled);
            var result = MessageBox.Show(
                $"Opriți înregistrarea?\n\n" +
                $"Eșantioane: {n:N0}\n" +
                $"Rată: {SampleRateHz} Hz · {enabled} canale\n" +
                $"Fișier:\n{path}",
                "Confirmare stop Record",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                Status = "Stop Record anulat — înregistrare continuă.";
                return;
            }
        }

        await _engine.StopRecordingAsync();
        IsRecording = false;
        UnlockStrainAutorangeAfterRecord();
        TrimLivePlotLoggers();
        // Status + path are set by RecordingStopped handler.
    }

    private ProcessedSample? _latestUiSample;
    private int _uiSampleScheduled;

    // Catman Easy–like live axes: Physical Y (µm/m…), X in seconds, expand fast / shrink slow.
    private double _liveYMinA = double.NaN;
    private double _liveYMaxA = double.NaN;
    private double _liveYMinB = double.NaN;
    private double _liveYMaxB = double.NaN;
    private readonly Queue<double> _liveYWindowA = new();
    private readonly Queue<double> _liveYWindowB = new();
    private const int LiveYWindowSize = 120; // rolling extrema pairs
    private const double LiveYPadFraction = 0.14;
    private const double LiveYShrinkAlpha = 0.08;
    private const double LiveXWindowSec = 5.0;
    private const double LiveStrainMinSpan = 100.0; // ±50 µm/m after Zero (catman-like)
    private const double StackGapFraction = 0.18;
    private double _liveTimeSec;
    private bool _liveStrainLikeA;
    private bool _liveStrainLikeB;
    /// <summary>Last X passed to ScottPlot DataLoggers (must stay strictly ascending).</summary>
    private double _lastDataLoggerX = double.NaN;

    private void ResetCatmanLiveAxis()
    {
        _liveYMinA = double.NaN;
        _liveYMaxA = double.NaN;
        _liveYMinB = double.NaN;
        _liveYMaxB = double.NaN;
        _liveYWindowA.Clear();
        _liveYWindowB.Clear();
        _liveStrainLikeA = false;
        _liveStrainLikeB = false;
        _liveTimeSec = 0;
        _lastDataLoggerX = double.NaN;
        _liveYValueByChannel.Clear();
        _channelPlotOffsets.Clear();
        _plotStackedMode = false;
    }

    /// <summary>
    /// SoftSetup / Simulator StartStreaming resets Sequence→0 while DataLoggers still hold
    /// large X values; SampleRateHz changes also warp Sequence×period. Rebuild before Add.
    /// </summary>
    private void NoteOrResetLoggerTimeline(double t)
    {
        if (double.IsNaN(t) || double.IsInfinity(t)) return;
        if (!double.IsNaN(_lastDataLoggerX) && t < _lastDataLoggerX)
        {
            RebuildPlotSeries();
            RebuildAllChannelPlotSeries();
        }
        if (double.IsNaN(_lastDataLoggerX) || t > _lastDataLoggerX)
            _lastDataLoggerX = t;
    }

    /// <summary>ScottPlot DataLoggerSource.Add throws if X is not strictly ascending.</summary>
    private static void AddDataLoggerPoint(ScottPlot.Plottables.DataLogger logger, double t, double y)
    {
        try
        {
            logger.Add(t, y);
            TrimDataLogger(logger);
        }
        catch (ArgumentException)
        {
            // Duplicate UI tick or rare race after SoftSetup — skip point, keep live stream alive.
        }
    }

    /// <summary>
    /// Live plots only need the rolling window. Keeping every UI point from a long Record
    /// makes ScottPlot Refresh() hitch after export/GC.
    /// </summary>
    private const int LiveLoggerMaxPoints = 8_000;

    private static void TrimDataLogger(ScottPlot.Plottables.DataLogger logger)
    {
        var coords = logger.Data.Coordinates;
        var extra = coords.Count - LiveLoggerMaxPoints;
        if (extra < 512)
            return;
        coords.RemoveRange(0, extra);
    }

    private void TrimLivePlotLoggers()
    {
        foreach (var logger in _loggersA.Values)
            TrimDataLogger(logger);
        foreach (var logger in _loggersB.Values)
            TrimDataLogger(logger);
        foreach (var runtime in _channelPlotRuntimes.Values)
        {
            if (runtime.Logger is not null)
                TrimDataLogger(runtime.Logger);
        }
    }

    private static string NormalizePlotUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return "?";
        return unit.Trim();
    }

    private static bool HasMixedPlotUnits(IReadOnlyList<ChannelRow> plotChannels)
    {
        return plotChannels.Select(c => NormalizePlotUnit(c.Unit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1;
    }

    /// <summary>Dual Y(t): largest unit group on A, rest on B; fallback to CH1–4 / CH5+ when one unit.</summary>
    private static (List<ChannelRow> groupA, List<ChannelRow> groupB) SplitDualYtGroups(IReadOnlyList<ChannelRow> enabled)
    {
        if (enabled.Count == 0)
            return ([], []);

        var unitGroups = enabled
            .GroupBy(c => NormalizePlotUnit(c.Unit), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.ToList())
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g[0].Index)
            .ToList();

        if (unitGroups.Count > 1)
        {
            var groupA = unitGroups[0];
            var groupB = unitGroups.Skip(1).SelectMany(g => g).OrderBy(c => c.Index).ToList();
            return (groupA, groupB);
        }

        if (enabled.Count > 4)
            return (enabled.Take(4).ToList(), enabled.Skip(4).ToList());

        return (enabled.ToList(), []);
    }

    private void RefreshPlotStackedMode(IReadOnlyList<ChannelRow> plotChannels)
    {
        _plotStackedMode = PlotMode == DisplayLayouts.Yt && HasMixedPlotUnits(plotChannels);
        if (!_plotStackedMode)
        {
            _channelPlotOffsets.Clear();
            return;
        }

        RecalculateStackedOffsets(plotChannels);
    }

    private void RecalculateStackedOffsets(IReadOnlyList<ChannelRow> plotChannels)
    {
        var offset = 0.0;
        foreach (var ch in plotChannels)
        {
            _channelPlotOffsets[ch.Index] = offset;
            offset += GetChannelPlotSpan(ch) * (1.0 + StackGapFraction);
        }
    }

    private double GetChannelPlotSpan(ChannelRow ch)
    {
        if (_liveYValueByChannel.TryGetValue(ch.Index, out var vals) && vals.Count > 0)
        {
            var lo = vals.Min();
            var hi = vals.Max();
            var span = hi - lo;
            var minSpan = IsStrainUnit(ch.Unit)
                ? LiveStrainMinSpan
                : Math.Max(1e-4, Math.Abs(hi) * 0.02 + 1e-4);
            return Math.Max(span, minSpan);
        }

        return IsStrainUnit(ch.Unit) ? LiveStrainMinSpan : 1.0;
    }

    private void TrackChannelValue(int chIndex, double v)
    {
        if (!_liveYValueByChannel.TryGetValue(chIndex, out var vals))
        {
            vals = new Queue<double>();
            _liveYValueByChannel[chIndex] = vals;
        }

        vals.Enqueue(v);
        while (vals.Count > LiveYWindowSize)
            vals.Dequeue();
    }

    private double PlotSampleY(int chIndex, double physical)
        => _plotStackedMode && _channelPlotOffsets.TryGetValue(chIndex, out var off)
            ? physical + off
            : physical;

    private static bool IsStrainUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        return unit.Contains("µm/m", StringComparison.OrdinalIgnoreCase)
            || unit.Contains("um/m", StringComparison.OrdinalIgnoreCase)
            || unit.Contains("με", StringComparison.OrdinalIgnoreCase)
            || unit.Contains("ustrain", StringComparison.OrdinalIgnoreCase);
    }

    private double SamplePeriodSec => 1.0 / Math.Max(1, SampleRateHz);

    private double TimeFromSample(ProcessedSample sample)
        => sample.Frame.Sequence * SamplePeriodSec;

    private void OnSampleProcessed(object? sender, ProcessedSample sample)
    {
        if (_replayMode) return;
        if (IsStreaming) NotifyWatchdogSample();

        // Coalesce: always show newest sample; never block acquisition with Invoke.
        Volatile.Write(ref _latestUiSample, sample);
        if (Interlocked.Exchange(ref _uiSampleScheduled, 1) == 1)
            return;

        _dispatcher.BeginInvoke(DispatcherPriority.Render, static (object? s) =>
        {
            var vm = (MainViewModel)s!;
            Interlocked.Exchange(ref vm._uiSampleScheduled, 0);
            var latest = Volatile.Read(ref vm._latestUiSample);
            if (latest is null) return;
            vm.ApplySampleToUi(latest);
        }, this);
    }

    private void ApplySampleToUi(ProcessedSample sample)
    {
        // Hold/Freeze: keep acquisition + recording; skip live plot/numeric refresh.
        if (DisplayFrozen)
        {
            if (IsRecording)
            {
                RecordingSampleCount = (int)_engine.RecordedSamples;
                var enabled = Channels.Count(c => c.Enabled);
                var recN = Channels.Count(c => c.Enabled && c.RecordEnabled);
                RecordingInfoText =
                    $"REC · {RecordingSampleCount:N0} eșantioane · {SampleRateHz} Hz · {recN}/{enabled} Rec · {StorageMode} · {LastRecordingPath}";
                Status = $"Hold + REC… {_engine.RecordedSamples} eșantioane";
            }
            else if (IsStreaming && sample.Frame.Sequence % 16 == 0)
                Status = $"Hold afișaj · achiziție {SampleRateHz} Hz (Pause = live)";
            return;
        }

        switch (PlotMode)
        {
            case DisplayLayouts.Yx:
                UpdateXy(sample);
                break;
            case DisplayLayouts.Poisson:
                UpdatePoisson(sample);
                break;
            case DisplayLayouts.Fft:
                UpdateFft(sample);
                break;
            case DisplayLayouts.Bar:
                UpdateBars(sample);
                break;
            case DisplayLayouts.Cwt:
                UpdateLiveCwt(sample);
                break;
            case DisplayLayouts.Numeric:
                break;
            default:
                UpdateYtLoggers(sample);
                break;
        }

        if (TimbruAutorange && !_autorangeLocked)
            MaybeRefineAutorangeFromLive();
        var overflow = CheckStrainAutorangeOverflow(sample);
        UpdateLiveNumeric(sample);
        TrackVerificationSample(sample);
        UpdateLiveGauge(sample);
        UpdateChannelPlots(sample);
        FeedCatmanLiveWindow(sample);
        if (IsRecording)
        {
            RecordingSampleCount = (int)_engine.RecordedSamples;
            var enabled = Channels.Count(c => c.Enabled);
            var recN = Channels.Count(c => c.Enabled && c.RecordEnabled);
            RecordingInfoText =
                $"REC · {RecordingSampleCount:N0} eșantioane · {SampleRateHz} Hz · {recN}/{enabled} Rec · {StorageMode} · {LastRecordingPath}";
        }

        var seq = sample.Frame.Sequence;
        if (FollowLiveZoom
            && PlotMode is DisplayLayouts.Yt or DisplayLayouts.DualYt or DisplayLayouts.Bar)
            ApplyCatmanLiveAxis(force: seq % 8 == 0);

        // Every coalesced UI tick — fluid like catman live (coalesce already limits to ~display Hz).
        if (PlotMode != DisplayLayouts.Cwt)
        {
            _plotA?.Refresh();
            _plotB?.Refresh();
        }
        if (IsRecording)
            Status = overflow ?? $"Înregistrare… {_engine.RecordedSamples} eșantioane";
        else if (IsStreaming && seq % 8 == 0)
        {
            var ch0 = Channels.FirstOrDefault(c => c.Enabled);
            if (ch0 is not null && ch0.Index < sample.Physical.Length && !double.IsNaN(sample.Physical[ch0.Index]))
                Status = $"Live · {SampleRateHz} Hz · {ch0.Name}={Format(sample.Physical[ch0.Index], ch0.Unit)} {ch0.Unit} · t={TimeFromSample(sample):0.00}s";
        }
    }

    private void EnqueueLiveExtremum(Queue<double> window, double lo, double hi)
    {
        window.Enqueue(lo);
        window.Enqueue(hi);
        while (window.Count > LiveYWindowSize * 2)
            window.Dequeue();
    }

    private void FeedCatmanLiveWindow(ProcessedSample sample)
    {
        _liveTimeSec = TimeFromSample(sample);
        var plotChannels = Channels.Where(c => c.Enabled && c.ShowOnPlot).ToList();
        RefreshPlotStackedMode(plotChannels);

        if (PlotMode == DisplayLayouts.DualYt)
        {
            var (groupA, groupB) = SplitDualYtGroups(plotChannels);
            FeedPanelWindow(sample, groupA, _liveYWindowA, ref _liveStrainLikeA);
            FeedPanelWindow(sample, groupB, _liveYWindowB, ref _liveStrainLikeB);
            var mathIdx = 0;
            double loB = double.PositiveInfinity, hiB = double.NegativeInfinity;
            var anyMath = false;
            foreach (var m in MathChannels)
            {
                if (!m.Enabled) { mathIdx++; continue; }
                var v = mathIdx < sample.Math.Length ? sample.Math[mathIdx] : double.NaN;
                mathIdx++;
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                anyMath = true;
                loB = Math.Min(loB, v);
                hiB = Math.Max(hiB, v);
                if (IsStrainUnit(m.Unit)) _liveStrainLikeB = true;
            }
            if (anyMath)
                EnqueueLiveExtremum(_liveYWindowB, loB, hiB);
            return;
        }

        if (_plotStackedMode)
        {
            foreach (var ch in plotChannels)
            {
                if (ch.Index < 0 || ch.Index >= sample.Physical.Length) continue;
                var v = sample.Physical[ch.Index];
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                TrackChannelValue(ch.Index, v);
            }

            RecalculateStackedOffsets(plotChannels);
            return;
        }

        FeedPanelWindow(sample, plotChannels, _liveYWindowA, ref _liveStrainLikeA);
    }

    private void FeedPanelWindow(ProcessedSample sample, IEnumerable<ChannelRow> channels, Queue<double> window, ref bool strainLike)
    {
        double lo = double.PositiveInfinity, hi = double.NegativeInfinity;
        var any = false;
        foreach (var ch in channels)
        {
            if (ch.Index < 0 || ch.Index >= sample.Physical.Length) continue;
            var v = sample.Physical[ch.Index];
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            any = true;
            lo = Math.Min(lo, v);
            hi = Math.Max(hi, v);
            if (IsStrainUnit(ch.Unit)) strainLike = true;
        }
        if (!any) return;
        EnqueueLiveExtremum(window, lo, hi);
    }

    /// <summary>
    /// Y-axis like catman Easy live: follow recent Physical samples, sensible padding,
    /// expand immediately on new peaks, shrink slowly. X follows real time in seconds.
    /// </summary>
    private void ApplyCatmanLiveAxis(bool force)
    {
        try
        {
            if (_plotStackedMode && PlotMode == DisplayLayouts.Yt)
            {
                var plotChannels = Channels.Where(c => c.Enabled && c.ShowOnPlot).ToList();
                if (plotChannels.Count > 0)
                {
                    double yMin = double.PositiveInfinity, yMax = double.NegativeInfinity;
                    foreach (var ch in plotChannels)
                    {
                        var off = _channelPlotOffsets.GetValueOrDefault(ch.Index);
                        if (_liveYValueByChannel.TryGetValue(ch.Index, out var vals) && vals.Count > 0)
                        {
                            yMin = Math.Min(yMin, off + vals.Min());
                            yMax = Math.Max(yMax, off + vals.Max());
                        }
                        else
                        {
                            var span = GetChannelPlotSpan(ch);
                            yMin = Math.Min(yMin, off);
                            yMax = Math.Max(yMax, off + span);
                        }
                    }
                    if (double.IsInfinity(yMin) || double.IsInfinity(yMax))
                    {
                        var last = plotChannels[^1];
                        yMax = _channelPlotOffsets.GetValueOrDefault(last.Index) + GetChannelPlotSpan(last);
                        yMin = 0;
                    }
                    var pad = Math.Max((yMax - yMin) * LiveYPadFraction, 0.5);
                    _plotA?.Plot.Axes.SetLimitsY(yMin - pad, yMax + pad);
                }

                if (_liveTimeSec > SamplePeriodSec * 2)
                {
                    var x0 = Math.Max(0, _liveTimeSec - LiveXWindowSec);
                    var x1 = _liveTimeSec + Math.Max(0.05, LiveXWindowSec / 40);
                    _plotA?.Plot.Axes.SetLimitsX(x0, x1);
                }

                return;
            }

            UpdateSmoothedYLimits(_liveYWindowA, ref _liveYMinA, ref _liveYMaxA, _liveStrainLikeA, force);
            if (!double.IsNaN(_liveYMinA) && !double.IsNaN(_liveYMaxA))
                _plotA?.Plot.Axes.SetLimitsY(_liveYMinA, _liveYMaxA);

            if (ShowPlotB && PlotMode == DisplayLayouts.DualYt)
            {
                UpdateSmoothedYLimits(_liveYWindowB, ref _liveYMinB, ref _liveYMaxB, _liveStrainLikeB, force);
                if (!double.IsNaN(_liveYMinB) && !double.IsNaN(_liveYMaxB))
                    _plotB?.Plot.Axes.SetLimitsY(_liveYMinB, _liveYMaxB);
                else if (!double.IsNaN(_liveYMinA) && !double.IsNaN(_liveYMaxA))
                    _plotB?.Plot.Axes.SetLimitsY(_liveYMinA, _liveYMaxA);
            }
            else if (ShowPlotB && PlotMode is DisplayLayouts.Yt or DisplayLayouts.Bar)
            {
                if (!double.IsNaN(_liveYMinA) && !double.IsNaN(_liveYMaxA))
                    _plotB?.Plot.Axes.SetLimitsY(_liveYMinA, _liveYMaxA);
            }

            // X: rolling window in real seconds (DataLogger X = Sequence / SampleRateHz).
            if (_liveTimeSec > SamplePeriodSec * 2)
            {
                var x0 = Math.Max(0, _liveTimeSec - LiveXWindowSec);
                var x1 = _liveTimeSec + Math.Max(0.05, LiveXWindowSec / 40);
                _plotA?.Plot.Axes.SetLimitsX(x0, x1);
                if (ShowPlotB && PlotMode == DisplayLayouts.DualYt)
                    _plotB?.Plot.Axes.SetLimitsX(x0, x1);
            }
        }
        catch
        {
            _plotA?.Plot.Axes.AutoScale();
        }
    }

    private void UpdateSmoothedYLimits(Queue<double> window, ref double liveMin, ref double liveMax, bool strainLike, bool force)
    {
        if (window.Count == 0) return;

        var winLo = window.Min();
        var winHi = window.Max();
        var span = winHi - winLo;
        var minSpan = strainLike
            ? LiveStrainMinSpan
            : Math.Max(1e-4, Math.Abs(winHi) * 0.02 + 1e-4);
        if (span < minSpan)
        {
            var mid = (winHi + winLo) * 0.5;
            winLo = mid - minSpan * 0.5;
            winHi = mid + minSpan * 0.5;
            span = minSpan;
        }

        var pad = Math.Max(span * LiveYPadFraction, minSpan * 0.08);
        var targetLo = winLo - pad;
        var targetHi = winHi + pad;

        if (double.IsNaN(liveMin) || double.IsNaN(liveMax))
        {
            liveMin = targetLo;
            liveMax = targetHi;
        }
        else
        {
            if (targetLo < liveMin) liveMin = targetLo;
            else if (force) liveMin += (targetLo - liveMin) * LiveYShrinkAlpha;

            if (targetHi > liveMax) liveMax = targetHi;
            else if (force) liveMax += (targetHi - liveMax) * LiveYShrinkAlpha;
        }

        if (liveMax <= liveMin)
            liveMax = liveMin + minSpan;
    }

    private void UpdateYtLoggers(ProcessedSample sample)
    {
        var t = TimeFromSample(sample);
        NoteOrResetLoggerTimeline(t);
        for (var i = 0; i < Channels.Count; i++)
        {
            var ch = Channels[i];
            if (!ch.Enabled || !ch.ShowOnPlot) continue;
            var idx = ch.Index;
            if (idx < 0 || idx >= sample.Physical.Length) continue;
            // Always plot Physical (after Scale/Tare), never raw mV/V digits.
            var v = sample.Physical[idx];
            if (double.IsNaN(v)) continue;
            var plotY = PlotSampleY(idx, v);
            if (_loggersA.TryGetValue(idx, out var a)) AddDataLoggerPoint(a, t, plotY);
            if (_loggersB.TryGetValue(idx, out var b)) AddDataLoggerPoint(b, t, plotY);
        }

        var mathIdx = 0;
        foreach (var m in MathChannels)
        {
            if (!m.Enabled) { mathIdx++; continue; }
            var v = mathIdx < sample.Math.Length ? sample.Math[mathIdx] : double.NaN;
            if (!double.IsNaN(v) && _loggersB.TryGetValue(1000 + mathIdx, out var logger))
                AddDataLoggerPoint(logger, t, v);
            mathIdx++;
        }
    }

    private void UpdateXy(ProcessedSample sample)
    {
        var xi = Math.Max(0, XyXChannel - 1);
        var yi = Math.Max(0, XyYChannel - 1);
        if (xi >= sample.Physical.Length || yi >= sample.Physical.Length || _plotA is null) return;
        var x = sample.Physical[xi];
        var y = sample.Physical[yi];
        if (double.IsNaN(x) || double.IsNaN(y)) return;
        _xyXs.Add(x);
        _xyYs.Add(y);
        while (_xyXs.Count > 4000)
        {
            _xyXs.RemoveAt(0);
            _xyYs.RemoveAt(0);
        }
        if (sample.Frame.Sequence % 3 != 0) return;
        _plotA.Plot.Clear();
        var sc = _plotA.Plot.Add.Scatter(_xyXs.ToArray(), _xyYs.ToArray());
        sc.LegendText = $"CH{XyYChannel} vs CH{XyXChannel}";
        sc.Color = ScottPlot.Colors.SteelBlue;
        sc.MarkerSize = 3;
        sc.LineWidth = 0;
        _xyScatter = sc;
        _plotA.Plot.Title($"Y(X) — Y=CH{XyYChannel}, X=CH{XyXChannel}");
        _plotA.Plot.Axes.Bottom.Label.Text = $"CH{XyXChannel}";
        _plotA.Plot.Axes.Left.Label.Text = $"CH{XyYChannel}";
        _plotA.Plot.Axes.AutoScale();
        CursorText = $"Y(X): X={x:0.####}  Y={y:0.####}  n={_xyXs.Count}";
    }

    private void UpdatePoisson(ProcessedSample sample)
    {
        var li = Math.Max(0, PoissonEpsLChannel - 1);
        var ti = Math.Max(0, PoissonEpsTChannel - 1);
        if (_plotA is null) return;
        if (li >= sample.Physical.Length || ti >= sample.Physical.Length || li == ti)
        {
            LivePoissonText = "ν: — (selectați canale ε_l / ε_t distincte)";
            return;
        }

        var epsL = sample.Physical[li];
        var epsT = sample.Physical[ti];
        if (double.IsNaN(epsL) || double.IsNaN(epsT) || double.IsInfinity(epsL) || double.IsInfinity(epsT))
            return;

        var t = TimeFromSample(sample);
        NoteOrResetLoggerTimeline(t);
        if (_loggersB.TryGetValue(li, out var logL)) AddDataLoggerPoint(logL, t, epsL);
        if (_loggersB.TryGetValue(ti, out var logT)) AddDataLoggerPoint(logT, t, epsT);

        _xyXs.Add(epsL);
        _xyYs.Add(epsT);
        while (_xyXs.Count > 4000)
        {
            _xyXs.RemoveAt(0);
            _xyYs.RemoveAt(0);
        }

        var nuResult = PoissonRatioAnalysis.ComputeFromBuffers(_xyXs, _xyYs);
        if (sample.Frame.Sequence % 3 != 0)
        {
            UpdateLivePoissonDisplay(nuResult, epsL, epsT);
            return;
        }

        _plotA.Plot.Clear();
        var sc = _plotA.Plot.Add.Scatter(_xyXs.ToArray(), _xyYs.ToArray());
        var lName = Channels.ElementAtOrDefault(li)?.Name ?? $"CH{PoissonEpsLChannel}";
        var tName = Channels.ElementAtOrDefault(ti)?.Name ?? $"CH{PoissonEpsTChannel}";
        sc.LegendText = $"ε_t vs ε_l";
        sc.Color = ScottPlot.Colors.SteelBlue;
        sc.MarkerSize = 3;
        sc.LineWidth = 0;
        _xyScatter = sc;

        if (nuResult.IsValid)
        {
            var a = nuResult.WindowStart;
            var b = nuResult.WindowEndExclusive;
            if (b > a && a >= 0 && b <= _xyXs.Count)
            {
                var fitXs = new double[b - a];
                var fitYs = new double[b - a];
                for (var i = 0; i < fitXs.Length; i++)
                {
                    fitXs[i] = _xyXs[a + i];
                    fitYs[i] = nuResult.Slope * fitXs[i] + nuResult.Intercept;
                }
                var line = _plotA.Plot.Add.Scatter(fitXs, fitYs);
                line.LegendText = $"ν≈{nuResult.Nu:0.####}";
                line.MarkerSize = 0;
                line.LineWidth = 2.2f;
                line.Color = ScottPlot.Colors.OrangeRed;
            }
        }

        _plotA.Plot.Title($"Poisson — ε_t vs ε_l · {LivePoissonText}");
        _plotA.Plot.Axes.Bottom.Label.Text = $"{lName} (ε_l)";
        _plotA.Plot.Axes.Left.Label.Text = $"{tName} (ε_t)";
        _plotA.Plot.ShowLegend();
        _plotA.Plot.Axes.AutoScale();
        UpdateLivePoissonDisplay(nuResult, epsL, epsT);
    }

    private void UpdateFft(ProcessedSample sample)
    {
        var ch = Math.Max(0, FftChannel - 1);
        if (ch >= sample.Physical.Length) return;
        var v = sample.Physical[ch];
        if (double.IsNaN(v)) return;
        _fftBuffer.Enqueue(v);
        while (_fftBuffer.Count > FftBufferSize) _fftBuffer.Dequeue();
        if (_fftBuffer.Count < 64 || sample.Frame.Sequence % 8 != 0) return;

        var mag = LiveFft.Magnitude(_fftBuffer.ToArray());
        if (mag.Length == 0 || _plotB is null) return;
        var df = SampleRateHz / (double)(mag.Length * 2);
        var xs = Enumerable.Range(0, mag.Length).Select(i => i * df).ToArray();
        _plotB.Plot.Clear();
        var sig = _plotB.Plot.Add.Scatter(xs, mag);
        sig.LegendText = $"FFT CH{FftChannel}";
        sig.Color = ScottPlot.Colors.SteelBlue;
        sig.LineWidth = 1.5f;
        sig.MarkerSize = 0;
        _plotB.Plot.Title($"FFT live — CH{FftChannel} (Hz)");
        _plotB.Plot.Axes.Bottom.Label.Text = "f [Hz]";
        _plotB.Plot.Axes.Left.Label.Text = "|A|";
        _plotB.Plot.Axes.AutoScale();
    }

    private void UpdateBars(ProcessedSample sample)
    {
        if (_plotA is null) return;
        foreach (var ch in Channels.Where(c => c.Enabled))
        {
            if (ch.Index >= sample.Physical.Length) continue;
            var v = sample.Physical[ch.Index];
            if (!double.IsNaN(v)) _barValues[ch.Index] = v;
        }

        if (sample.Frame.Sequence % 5 != 0) return;
        var enabled = Channels.Where(c => c.Enabled).ToList();
        if (enabled.Count == 0) return;
        var positions = enabled.Select((_, i) => (double)i).ToArray();
        var values = enabled.Select(c => _barValues.TryGetValue(c.Index, out var v) ? v : 0).ToArray();
        _plotA.Plot.Clear();
        var bars = _plotA.Plot.Add.Bars(positions, values);
        _plotA.Plot.Axes.Bottom.TickGenerator = new ScottPlot.TickGenerators.NumericManual(
            positions, enabled.Select(c => c.Name).ToArray());
        _plotA.Plot.Title("Bar — valori live pe canale");
        _plotA.Plot.Axes.AutoScale();
    }

    private void UpdateLiveNumeric(ProcessedSample sample)
    {
        foreach (var ch in Channels)
        {
            var inScan = ch.Index >= 0 && ch.Index < sample.Physical.Length;
            var v = inScan ? sample.Physical[ch.Index] : double.NaN;
            ch.LiveReading = LiveCitire.FormatOrPlaceholder(
                v, ch.Unit, ch.Enabled, ch.IsOverflow, ch.IsLinkLost, inScan, ch.HasMeasurementSetup);
        }
        RefreshFaultBanner();

        var expectedSlots = Channels.Count(c => c.Enabled) + MathChannels.Count(m => m.Enabled);
        if (LiveValues.Count != expectedSlots)
            RefreshLiveValueHeaders();

        var idx = 0;
        foreach (var ch in Channels.Where(c => c.Enabled))
        {
            if (idx < LiveValues.Count)
            {
                var inScan = ch.Index >= 0 && ch.Index < sample.Physical.Length;
                var v = inScan ? sample.Physical[ch.Index] : double.NaN;
                LiveValues[idx].Name = ch.Name;
                LiveValues[idx].Unit = ch.HasMeasurementSetup && !LiveCitire.IsPlaceholder(ch.LiveReading)
                    ? ch.Unit
                    : "";
                LiveValues[idx].Display = ch.LiveReading;
                LiveValues[idx].IsAlarm = ch.IsInAlarm(v) || ch.IsOverflow;
            }
            idx++;
        }
        for (var m = 0; m < MathChannels.Count; m++)
        {
            if (!MathChannels[m].Enabled) continue;
            if (idx < LiveValues.Count)
            {
                var v = m < sample.Math.Length ? sample.Math[m] : double.NaN;
                LiveValues[idx].Name = MathChannels[m].Name;
                LiveValues[idx].Unit = MathChannels[m].Unit;
                LiveValues[idx].Display = Format(v, MathChannels[m].Unit);
                LiveValues[idx].IsAlarm = false;
            }
            idx++;
        }
    }

    private void RebuildPlotSeries()
    {
        if (_plotA is null || _plotB is null) return;
        _plotA.Plot.Clear();
        _plotB.Plot.Clear();
        _loggersA.Clear();
        _loggersB.Clear();
        _barValues.Clear();
        _xyScatter = null;
        _xyXs.Clear();
        _xyYs.Clear();
        _fftBuffer.Clear();
        ResetCatmanLiveAxis();

        var enabled = Channels.Where(c => c.Enabled && c.ShowOnPlot).ToList();
        RefreshPlotStackedMode(enabled);
        var yUnit = _plotStackedMode
            ? "Y [stivuit · unități mixte]"
            : FormatYAxisLabel(enabled.Select(c => c.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)));
        var xLabel = SampleRateHz > 0 ? $"t [s] @ {SampleRateHz} Hz" : "t [s]";
        var period = SamplePeriodSec;

        ScottPlot.Plottables.DataLogger MakeLogger(ScottPlot.Plot plot, string legend, ScottPlot.Color color)
        {
            var logger = plot.Add.DataLogger();
            logger.LegendText = legend;
            logger.Color = color;
            logger.ManageAxisLimits = false;
            logger.LineStyle.Width = 1.6f;
            logger.Period = period; // fallback if Add(y) used; live path uses Add(t, y)
            return logger;
        }

        switch (PlotMode)
        {
            case DisplayLayouts.Yt:
            {
                foreach (var ch in enabled)
                {
                    _loggersA[ch.Index] = MakeLogger(_plotA.Plot, $"{ch.Name} [{ch.Unit}]",
                        Controls.ChannelPalette.GetScottPlotColor(ch.Index));
                }
                ApplyEngineeringPlotStyle(_plotA.Plot, $"Y(t) — {enabled.Count} canale @ {SampleRateHz} Hz", xLabel, yUnit);
                ApplyEngineeringPlotStyle(_plotB.Plot, "—", xLabel, yUnit);
                ApplyLivePlotAnnotations(_plotA.Plot);
                if (_plotB is not null) ApplyLivePlotAnnotations(_plotB.Plot);
                break;
            }
            case DisplayLayouts.DualYt:
            {
                var (groupA, groupB) = SplitDualYtGroups(enabled);
                foreach (var ch in groupA)
                {
                    _loggersA[ch.Index] = MakeLogger(_plotA.Plot, $"{ch.Name} [{ch.Unit}]",
                        Controls.ChannelPalette.GetScottPlotColor(ch.Index));
                }
                foreach (var ch in groupB)
                {
                    _loggersB[ch.Index] = MakeLogger(_plotB.Plot, $"{ch.Name} [{ch.Unit}]",
                        Controls.ChannelPalette.GetScottPlotColor(ch.Index));
                }
                for (var i = 0; i < MathChannels.Count; i++)
                {
                    if (!MathChannels[i].Enabled) continue;
                    // Math channels: palette offset so they stay distinct from CH0–CH7.
                    _loggersB[1000 + i] = MakeLogger(_plotB.Plot,
                        $"{MathChannels[i].Name} [{MathChannels[i].Unit}]",
                        Controls.ChannelPalette.GetScottPlotColor(8 + i));
                }
                var unitA = FormatYAxisLabel(groupA.Select(c => c.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)) ?? yUnit);
                var unitBRaw = groupB.Select(c => c.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                    ?? MathChannels.FirstOrDefault(m => m.Enabled)?.Unit
                    ?? yUnit;
                var unitB = FormatYAxisLabel(unitBRaw);
                var titleA = groupA.Count > 0
                    ? $"Y(t) A — {groupA.Count} canale [{NormalizePlotUnit(groupA[0].Unit)}]"
                    : "Y(t) A";
                var titleB = groupB.Count > 0
                    ? $"Y(t) B — {groupB.Count} canale [{NormalizePlotUnit(groupB[0].Unit)}]"
                    : "Y(t) B — Math";
                ApplyEngineeringPlotStyle(_plotA.Plot, titleA, xLabel, unitA);
                ApplyEngineeringPlotStyle(_plotB.Plot, titleB, xLabel, unitB);
                break;
            }
            case DisplayLayouts.Yx:
            {
                _xyScatter = _plotA.Plot.Add.Scatter(Array.Empty<double>(), Array.Empty<double>());
                _xyScatter.LegendText = $"CH{XyYChannel} vs CH{XyXChannel}";
                _xyScatter.Color = Controls.ChannelPalette.GetScottPlotColor(Math.Max(0, XyYChannel));
                _xyScatter.MarkerSize = 4;
                _xyScatter.LineWidth = 0;
                var xName = Channels.ElementAtOrDefault(Math.Max(0, XyXChannel - 1));
                var yName = Channels.ElementAtOrDefault(Math.Max(0, XyYChannel - 1));
                ApplyEngineeringPlotStyle(_plotA.Plot,
                    $"Y(X) — Y={yName?.Name ?? $"CH{XyYChannel}"}, X={xName?.Name ?? $"CH{XyXChannel}"}",
                    $"{xName?.Name ?? $"CH{XyXChannel}"} [{xName?.Unit ?? ""}]",
                    $"{yName?.Name ?? $"CH{XyYChannel}"} [{yName?.Unit ?? ""}]");
                ApplyEngineeringPlotStyle(_plotB.Plot, "Y(X) — curba pe panoul A", xLabel, yUnit);
                break;
            }
            case DisplayLayouts.Poisson:
            {
                _xyScatter = _plotA.Plot.Add.Scatter(Array.Empty<double>(), Array.Empty<double>());
                _xyScatter.LegendText = "ε_t vs ε_l";
                _xyScatter.Color = Controls.ChannelPalette.GetScottPlotColor(Math.Max(0, PoissonEpsTChannel));
                _xyScatter.MarkerSize = 4;
                _xyScatter.LineWidth = 0;
                var lCh = Channels.ElementAtOrDefault(Math.Max(0, PoissonEpsLChannel - 1));
                var tCh = Channels.ElementAtOrDefault(Math.Max(0, PoissonEpsTChannel - 1));
                var lIdx = Math.Max(0, PoissonEpsLChannel - 1);
                var tIdx = Math.Max(0, PoissonEpsTChannel - 1);
                if (lCh is not null)
                    _loggersB[lIdx] = MakeLogger(_plotB.Plot, $"ε_l {lCh.Name}",
                        Controls.ChannelPalette.GetScottPlotColor(lIdx));
                if (tCh is not null && tIdx != lIdx)
                    _loggersB[tIdx] = MakeLogger(_plotB.Plot, $"ε_t {tCh.Name}",
                        Controls.ChannelPalette.GetScottPlotColor(tIdx));
                ApplyEngineeringPlotStyle(_plotA.Plot,
                    $"Poisson — ε_t vs ε_l · ν live",
                    $"{lCh?.Name ?? $"CH{PoissonEpsLChannel}"} (ε_l) [{lCh?.Unit ?? "µm/m"}]",
                    $"{tCh?.Name ?? $"CH{PoissonEpsTChannel}"} (ε_t) [{tCh?.Unit ?? "µm/m"}]");
                ApplyEngineeringPlotStyle(_plotB.Plot,
                    "Poisson — Dual Y(t) ε_l / ε_t",
                    xLabel,
                    FormatYAxisLabel(lCh?.Unit ?? tCh?.Unit ?? "µm/m"));
                LivePoissonText = "ν: — (Start pentru date)";
                LivePoissonNu = double.NaN;
                break;
            }
            case DisplayLayouts.Bar:
            {
                ApplyEngineeringPlotStyle(_plotA.Plot, "Bar — valori live (Start pentru update)", "Canal", yUnit);
                ApplyEngineeringPlotStyle(_plotB.Plot, "—", xLabel, yUnit);
                break;
            }
            case DisplayLayouts.Fft:
            {
                ApplyEngineeringPlotStyle(_plotA.Plot, $"Canal FFT: CH{FftChannel} (buffer {FftBufferSize})", xLabel, yUnit);
                ApplyEngineeringPlotStyle(_plotB.Plot, "FFT live — Start pentru spectru", "f [Hz]", "|A|");
                break;
            }
            case DisplayLayouts.Cwt:
            {
                ApplyEngineeringPlotStyle(_plotA.Plot, "CWT — scalograme pe panoul CWT (canale On)", xLabel, yUnit);
                ApplyEngineeringPlotStyle(_plotB.Plot, "—", xLabel, yUnit);
                SyncLiveCwtPanels();
                break;
            }
            case DisplayLayouts.Numeric:
            {
                ApplyEngineeringPlotStyle(_plotA.Plot, "Numeric — valori pe panoul stânga / overlay", xLabel, yUnit);
                ApplyEngineeringPlotStyle(_plotB.Plot, "—", xLabel, yUnit);
                break;
            }
        }

        var showLegend = PlotMode is DisplayLayouts.Yt or DisplayLayouts.DualYt
            or DisplayLayouts.Fft or DisplayLayouts.Yx or DisplayLayouts.Poisson;
        if (showLegend)
        {
            _plotA.Plot.ShowLegend();
            if (PlotMode == DisplayLayouts.DualYt || PlotMode == DisplayLayouts.Fft || PlotMode == DisplayLayouts.Poisson)
                _plotB.Plot.ShowLegend();
            else
                _plotB.Plot.HideLegend();
        }
        else
        {
            _plotA.Plot.HideLegend();
            _plotB.Plot.HideLegend();
        }
        RebuildAllChannelPlotSeries();
        if (PlotMode == DisplayLayouts.Cwt)
            SyncLiveCwtPanels();
        _plotA.Refresh();
        _plotB.Refresh();
    }

    private void RefreshLiveValueHeaders()
    {
        LiveValues.Clear();
        foreach (var ch in Channels.Where(c => c.Enabled))
            LiveValues.Add(new LiveValueRow { Name = ch.Name, Unit = ch.Unit, Display = "—" });
        foreach (var m in MathChannels.Where(x => x.Enabled))
            LiveValues.Add(new LiveValueRow { Name = m.Name, Unit = m.Unit, Display = "—" });
    }

    private void SyncMathToEngine()
    {
        _engine.MathChannels.Clear();
        foreach (var m in MathChannels)
        {
            _engine.MathChannels.Add(new MathChannelDefinition
            {
                Name = m.Name,
                Unit = m.Unit,
                Operation = Enum.TryParse<MathOp>(m.Operation, out var op) ? op : MathOp.Average,
                SourceA = m.SourceA,
                SourceB = m.SourceB,
                SourceC = m.SourceC,
                WindowSize = m.WindowSize,
                Enabled = m.Enabled,
                Formula = m.Formula
            });
        }
    }

    private static string FormatYAxisLabel(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit) || unit == "Y") return "Y";
        // Avoid "Y [Y [µm/m]]" if already formatted.
        if (unit.StartsWith("Y [", StringComparison.Ordinal)) return unit;
        return $"Y [{unit}]";
    }

    private static string Format(double v, string? unit = null)
        => EngineeringDisplay.FormatReading(v, unit);

    private static ChannelConfig ToConfig(ChannelRow row) => new()
    {
        Index = row.Index,
        DeviceIndex = row.DeviceIndex,
        Name = row.Name,
        Unit = row.Unit,
        Enabled = row.Enabled,
        RecordEnabled = row.RecordEnabled,
        Scale = row.Scale,
        Offset = row.Offset,
        TareValue = row.TareValue,
        SensorId = row.SensorId,
        SensorName = row.SensorName,
        Bridge = Enum.TryParse<BridgeType>(row.Bridge, out var b) ? b : BridgeType.None,
        RangeMvPerV = row.RangeMvPerV,
        FilterHz = row.FilterHz,
        ChannelSampleRateHz = row.ChannelSampleRateHz,
        ExcitationV = row.ExcitationV,
        ShuntKohm = row.ShuntKohm,
        GaugeFactor = row.GaugeFactor,
        GaugeOhm = row.GaugeOhm,
        HalfConfig = row.HalfConfig,
        PoissonRatio = row.PoissonRatio,
        ShuntEnabled = row.ShuntEnabled,
        LastShuntReading = row.LastShuntReading,
        AlarmEnabled = row.AlarmEnabled,
        AlarmLow = row.AlarmLow,
        AlarmHigh = row.AlarmHigh,
        Capacity = row.Capacity,
        SensorCategory = row.SensorCategory
    };

    /// <summary>
    /// Optional Contur / experiment hint for Simulator only — Serial/USB backends ignore it.
    /// </summary>
    private void PushSimulatorScenarioHint()
    {
        if (_device is not ISimulatorScenarioSink sink) return;
        var hint = new SimulatorScenarioHint
        {
            Kind = SimulatorScenarioKind.Auto,
            ExperimentType = ExperimentType
        };
        if (CylinderContour is { } cfg)
        {
            hint.ForceChannelIndex = cfg.ForceChannelIndex;
            hint.StrokeChannelIndex = cfg.StrokeChannelIndex;
            hint.RadialChannelIndices = cfg.SensorChannelIndices.ToList();
            hint.RadialAnglesDeg = cfg.EffectiveAnglesDeg().ToList();
            if (ExperimentTypes.IsCylinderContour(ExperimentType))
                hint.Kind = SimulatorScenarioKind.CylinderContour;
            if (_contourSimDemoEnabled
                && _contourSimAssignment is { } asg
                && cfg.SensorCount == 8
                && ExperimentTypes.IsCylinderContour(ExperimentType))
            {
                hint.Kind = SimulatorScenarioKind.CylinderContour;
                hint.CycleSeconds = ContourSimAssignment.DurationSeconds;
                hint.OneShotRamp = true;
                hint.RadialTargetMm = asg.TargetsMm();
            }
        }
        else if (ExperimentTypes.IsCylinderContour(ExperimentType))
        {
            hint.Kind = SimulatorScenarioKind.CylinderContour;
        }

        sink.SetScenarioHint(hint);
    }

    /// <summary>New random 8/1/3 mm draw on each Start of the 10 s Contur simulator run.</summary>
    private void DrawContourSimAssignmentIfNeeded()
    {
        if (!_contourSimDemoEnabled
            || !ExperimentTypes.IsCylinderContour(ExperimentType)
            || CylinderContour is not { SensorCount: 8 }
            || !string.Equals(SelectedBackend, "Simulator", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _contourSimAssignment = ContourSimAssignment.RandomDraw();
    }

    /// <summary>0-based HW index (CH0…CH7). Used by CalChannel and TareAsync(ch.Index).</summary>
    private int? ResolveHwIndex(int hwIndex)
        => hwIndex >= 0 && hwIndex < Channels.Count ? hwIndex : null;

    /// <summary>Apply CH# box: 1-based catman numbering (CH1→HW0, CH3→HW2 for grid „CH2”).</summary>
    private int? ResolveHwIndexFromUiSelector(int uiChannelNumber)
        => uiChannelNumber >= 1 && uiChannelNumber <= Channels.Count ? uiChannelNumber - 1 : null;

    private void LogLiveAcquisitionDiagnostics()
    {
        var lines = Channels
            .Where(c => c.Enabled && !c.Name.Contains("DI", StringComparison.OrdinalIgnoreCase))
            .Select(c =>
                $"{c.Name}(HW{c.Index}): raw→({c.TareValue:G6})×{c.Scale:G4}+{c.Offset:G4}→{c.Unit} " +
                $"bridge={c.Bridge} ASA-hint={c.RangeMvPerV:G4} mV/V");
        var msg = "Diag achiziție live: " + string.Join(" · ", lines);
        _journal.Info(msg);
    }

    /// <summary>Prefer selected grid row; else Apply CH# box (1-based → HW index).</summary>
    private ChannelRow? ResolveSelectedHwChannel()
    {
        if (SelectedChannelRow is not null)
            return SelectedChannelRow;
        return ResolveHwIndexFromUiSelector(SelectedChannelForSensor) is int i
            ? Channels[i]
            : null;
    }

    /// <summary>Scale × −1 on a concrete channel (menu / banner). Returns false if Scale≈0.</summary>
    private bool TryInvertChannelPolarity(ChannelRow ch, out string detail)
    {
        if (Math.Abs(ch.Scale) < 1e-15)
        {
            detail = $"{ch.Name}: Scale≈0 — aplicați senzorul înainte de inversare.";
            return false;
        }

        PushChannelEditUndo(ch);
        ch.Scale = -ch.Scale;
        if (_device is not null && IsConnected)
            _ = PushChannelConfigAsync(reapplyAcquisition: false);
        detail = $"Polaritate inversată pe {ch.Name} (Scale={ch.Scale:G6}).";
        return true;
    }
}
