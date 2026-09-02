using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using Spider8DAQ.Hardware;

namespace Spider8DAQ.App.ViewModels;

/// <summary>
/// Industrial workspace: Live / Configure / Review + device health badge + shortcuts.
/// Inspired by FlexLogger / Dewesoft / catman workflows — original UPET chrome.
/// </summary>
public partial class MainViewModel
{
    public const string ModeLive = "Live";
    public const string ModeConfigure = "Configure";
    public const string ModeReview = "Review";

    private string _workspaceMode = ModeLive;
    private bool _showShortcutsPanel;
    private string _connectionHealthText = "Deconectat";
    private string _connectionHealthDetail = "Alegeți backend (Simulator / Serial / Spider32) și Connect.";
    // Stronger fills so SIM / LIVE / REC do not disappear into lab gray.
    private static readonly Brush HealthBrushRec = FreezeBrush(0xB0, 0x0C, 0x28);
    private static readonly Brush HealthBrushLive = FreezeBrush(0x05, 0x5A, 0x28); // richer/darker LIVE — white text
    private static readonly Brush HealthBrushOk = FreezeBrush(0x00, 0x66, 0xB3);
    private static readonly Brush HealthBrushUsb = FreezeBrush(0xC4, 0x8A, 0x00);
    private static readonly Brush HealthBrushSim = FreezeBrush(0x1B, 0x4F, 0x72); // deep teal-blue — distinct from gray chrome
    private static readonly Brush HealthBrushLost = FreezeBrush(0xC8, 0x10, 0x2E);
    private static readonly Brush HealthBrushOff = FreezeBrush(0x5A, 0x65, 0x73);
    /// <summary>Connected idle — not running. Slate, never a green header wash.</summary>
    private static readonly Brush HealthBrushIdleConnected = FreezeBrush(0x3A, 0x45, 0x50);
    private Brush _connectionHealthBrush = HealthBrushOff;
    private Brush _sessionBadgeBrush = HealthBrushOff;
    private string _sessionBadge = "IDLE";

    private static Brush FreezeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public ObservableCollection<string> WorkspaceModes { get; } = new() { ModeLive, ModeConfigure, ModeReview };

    public ICommand SetWorkspaceLiveCommand { get; private set; } = null!;
    public ICommand SetWorkspaceConfigureCommand { get; private set; } = null!;
    public ICommand SetWorkspaceReviewCommand { get; private set; } = null!;
    public ICommand ToggleShortcutsPanelCommand { get; private set; } = null!;

    public event EventHandler? RequestMeasureTab;
    public event EventHandler? RequestDataViewerTab;
    public event EventHandler? RequestJobTab;
    public event EventHandler? RequestHealthTab;

    public string WorkspaceMode
    {
        get => _workspaceMode;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == _workspaceMode) return;
            _workspaceMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsWorkspaceLive));
            OnPropertyChanged(nameof(IsWorkspaceConfigure));
            OnPropertyChanged(nameof(IsWorkspaceReview));
            ApplyWorkspaceMode();
        }
    }

    public bool IsWorkspaceLive => WorkspaceMode == ModeLive;
    public bool IsWorkspaceConfigure => WorkspaceMode == ModeConfigure;
    public bool IsWorkspaceReview => WorkspaceMode == ModeReview;

    public bool ShowShortcutsPanel
    {
        get => _showShortcutsPanel;
        set { _showShortcutsPanel = value; OnPropertyChanged(); }
    }

    public string ConnectionHealthText
    {
        get => _connectionHealthText;
        set { _connectionHealthText = value; OnPropertyChanged(); }
    }

    public string ConnectionHealthDetail
    {
        get => _connectionHealthDetail;
        set { _connectionHealthDetail = value; OnPropertyChanged(); }
    }

    public Brush ConnectionHealthBrush
    {
        get => _connectionHealthBrush;
        set { _connectionHealthBrush = value; OnPropertyChanged(); }
    }

    public string SessionBadge
    {
        get => _sessionBadge;
        set { _sessionBadge = value; OnPropertyChanged(); }
    }

    public Brush SessionBadgeBrush
    {
        get => _sessionBadgeBrush;
        set { _sessionBadgeBrush = value; OnPropertyChanged(); }
    }

    public string ShortcutsHelpText { get; } =
        "Scurtături tastatură — UPET AcqLab\n" +
        "─────────────────────────────────\n" +
        "F5     Start măsurare (streaming)\n" +
        "F6     Stop măsurare\n" +
        "F7     Start înregistrare (Record)\n" +
        "F8     Stop înregistrare\n" +
        "F9     Zero / Tare pe toate canalele\n" +
        "F10    Preflight (USB/catman + tare/shunt vs Timbru+Rsh, Diferență permisă 0.5–5%)\n" +
        "F11    Panel / fullscreen grafic\n" +
        "F12    ACK alarme\n" +
        "Pause  Hold afișaj (freeze live UI)\n" +
        "Ctrl+Shift+C  Copiază valori live\n" +
        "Ctrl+Shift+M  Marcaj CSV în timpul Record\n" +
        "F1     Afișează / ascunde această listă\n" +
        "Ctrl+1 Mod Live (măsurare + Y(t))\n" +
        "Ctrl+2 Mod Configure (job / canale / senzori)\n" +
        "Ctrl+3 Mod Review (DataViewer + analiză)\n" +
        "Ctrl+S Salvează proiect (.s8proj)\n" +
        "Ctrl+O Încarcă proiect\n" +
        "Ctrl+E Export Excel (ultima înregistrare)\n" +
        "Ctrl+P Export PDF raport\n" +
        "Ctrl+Shift+P Raport industrial PDF\n" +
        "Ctrl+R Start Record (ca F7)\n" +
        "Ctrl+J Tab Job măsurătoare\n" +
        "Ctrl+H Self-test / Health dispozitiv\n" +
        "\n" +
        "Grilă canale (Operator): On · LED · Nume · Citire · Unit · Semnal · Punte · Senzor\n" +
        "Expert: Graf · Rec · Scale · Hz · Rsh · Alm — în CANAL SELECTAT\n" +
        "Click dreapta pe canal: Zero CH · Shunt · Aplică Scale din shunt · Inversare polaritate · Aplică senzor\n" +
        "Click dreapta pe senzor: Aplică · Zero/Offset · Edit · Dup · Șterge";

    private void WireWorkspaceCommands()
    {
        SetWorkspaceLiveCommand = new RelayCommand(() => WorkspaceMode = ModeLive);
        SetWorkspaceConfigureCommand = new RelayCommand(() => WorkspaceMode = ModeConfigure);
        SetWorkspaceReviewCommand = new RelayCommand(() => WorkspaceMode = ModeReview);
        ToggleShortcutsPanelCommand = new RelayCommand(() =>
        {
            ShowShortcutsPanel = !ShowShortcutsPanel;
            if (ShowShortcutsPanel)
                HelpPanelText = "Scurtături tastatură — F1 pentru a ascunde panoul.";
        });
        RefreshConnectionHealth();
    }

    private void ApplyWorkspaceMode()
    {
        switch (WorkspaceMode)
        {
            case ModeLive:
                PanelMode = false;
                FollowLiveZoom = true;
                if (PlotMode is DisplayLayouts.Numeric)
                    PlotMode = DisplayLayouts.Yt;
                HelpPanelText = "Mod Live — streaming Y(t), valori numerice, Zero/Record. F5–F9.";
                Status = "Mod workspace: Live";
                RequestMeasureTab?.Invoke(this, EventArgs.Empty);
                break;
            case ModeConfigure:
                PanelMode = false;
                HelpPanelText = "Mod Configure — meta, rată/filtru, senzori, job, alarme. Ctrl+2.";
                Status = "Mod workspace: Configure";
                RequestJobTab?.Invoke(this, EventArgs.Empty);
                break;
            case ModeReview:
                HelpPanelText = "Mod Review — DataViewer playback, export CSV/Excel, Analysis. Ctrl+3.";
                Status = "Mod workspace: Review";
                RequestDataViewerTab?.Invoke(this, EventArgs.Empty);
                try { RefreshRecordings(); } catch { /* ignore */ }
                break;
        }
        RefreshConnectionHealth();
        UiLayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshConnectionHealth()
    {
        // PropertyChanged + Brush binding must land on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(RefreshConnectionHealth);
            return;
        }

        string text;
        string detail;
        Brush brush;

        if (_communicationLost && !IsConnected)
        {
            text = "Comunicare pierdută";
            detail = "USB/Spider8 indisponibil — apăsați Conn după power-on.";
            brush = HealthBrushLost;
            SessionBadge = "Comunicare pierdută";
        }
        else if (IsRecording)
        {
            text = "REC";
            detail = $"Înregistrare activă · {SelectedBackend} · {ConnectedDeviceLabel}";
            brush = HealthBrushRec;
            SessionBadge = "REC";
        }
        else if (IsStreaming)
        {
            text = "LIVE";
            var hasLive = Channels.Any(c => c.Enabled && c.HasSignal);
            detail = hasLive
                ? $"Streaming · {SampleRateHz} Hz · {ConnectedDeviceLabel}"
                : $"Streaming · {SampleRateHz} Hz — fără valori încă (CH0 Half, senzor, catman închis)";
            brush = HealthBrushLive;
            SessionBadge = "LIVE";
        }
        else if (IsConnecting)
        {
            text = "…";
            detail = string.IsNullOrWhiteSpace(ConnectStageText)
                ? "Se conectează la dispozitiv…"
                : ConnectStageText;
            brush = HealthBrushUsb;
            SessionBadge = "CONN…";
        }
        else if (IsConnected)
        {
            text = "OK";
            detail = $"Conectat — apăsați Start pentru semnal · {SelectedBackend} · {ConnectedDeviceLabel}";
            brush = HealthBrushOk;
            SessionBadge = "CONN";
        }
        else if (SelectedBackend.Contains("Spider32", StringComparison.OrdinalIgnoreCase)
                 && HbmUsbDeviceScanner.IsHbmUsbTarget(SelectedPort))
        {
            text = "USB";
            detail = $"USBHBM detectat — Connect cu HBM USB / Spider32 (nu Serial/COM). {SelectedPort}" +
                     (IsCatmanEasyRunning() ? " · ATENȚIE: catman Easy rulează" : "");
            brush = HealthBrushUsb;
            SessionBadge = "USB";
        }
        else if (SelectedBackend == "Simulator")
        {
            text = "SIM";
            detail = "Deconectat · Simulator — fără hardware. Ideal pentru laborator / demo.";
            brush = HealthBrushSim;
            SessionBadge = "SIM";
        }
        else
        {
            text = "OFF";
            detail = $"Deconectat · backend {SelectedBackend}" +
                     (string.IsNullOrWhiteSpace(SelectedPort) ? "" : $" · țintă {SelectedPort}");
            brush = HealthBrushOff;
            SessionBadge = "IDLE";
        }

        ConnectionHealthText = text;
        ConnectionHealthDetail = detail;
        ConnectionHealthBrush = brush;
        SessionBadgeBrush = brush;
        RefreshMachineStateBand();
    }

    /// <summary>Extended global keys: F-keys + Ctrl shortcuts (called from MainWindow).</summary>
    public void HandleGlobalKey(Key key, ModifierKeys modifiers)
    {
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.C)
        {
            CopyLiveValuesCommand?.Execute(null);
            return;
        }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.M)
        {
            MarkRecordingCommand?.Execute(null);
            return;
        }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.P)
        {
            ExportIndustrialReportCommand?.Execute(null);
            return;
        }

        if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.D1:
                case Key.NumPad1:
                    WorkspaceMode = ModeLive;
                    return;
                case Key.D2:
                case Key.NumPad2:
                    WorkspaceMode = ModeConfigure;
                    return;
                case Key.D3:
                case Key.NumPad3:
                    WorkspaceMode = ModeReview;
                    return;
                case Key.Z:
                    UndoChannelEditCommand?.Execute(null);
                    return;
                case Key.S:
                    SaveProjectCommand?.Execute(null);
                    return;
                case Key.O:
                    LoadProjectCommand?.Execute(null);
                    return;
                case Key.E:
                    ExportExcelCommand?.Execute(null);
                    return;
                case Key.P:
                    ExportPdfCommand?.Execute(null);
                    return;
                case Key.R:
                    StartRecordCommand?.Execute(null);
                    return;
                case Key.J:
                    RequestJobTab?.Invoke(this, EventArgs.Empty);
                    return;
                case Key.H:
                    RequestHealthTab?.Invoke(this, EventArgs.Empty);
                    SelfTestCommand?.Execute(null);
                    return;
            }
        }

        if (modifiers != ModifierKeys.None && modifiers != ModifierKeys.Shift)
            return;

        switch (key)
        {
            case Key.F1:
                ToggleShortcutsPanelCommand.Execute(null);
                break;
            case Key.F5:
                _ = StartAsync();
                break;
            case Key.F6:
                _ = StopAsync();
                break;
            case Key.F7:
                _ = StartRecordingAsync();
                break;
            case Key.F8:
                _ = StopRecordingAsync();
                break;
            case Key.F9:
                _ = TareAsync(null);
                break;
            case Key.F10:
                PreflightCommand?.Execute(null);
                break;
            case Key.F11:
                ToggleFullscreenCommand?.Execute(null);
                break;
            case Key.F12:
                AckAlarmsCommand?.Execute(null);
                break;
            case Key.Pause:
            case Key.MediaPlayPause:
                ToggleDisplayFreezeCommand?.Execute(null);
                break;
        }
    }
}
