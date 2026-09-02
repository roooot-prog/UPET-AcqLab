using System.Linq;
using System.Windows.Media;

namespace Spider8DAQ.App.ViewModels;

/// <summary>
/// Industrial HMI: machine-state band, live Citire strip, Expert/Dark prefs, fault banner.
/// Display-only — does not change acquisition, shunt, or Scale physics.
/// </summary>
public partial class MainViewModel
{
    private bool _isExpertMode;
    private bool _isDarkTheme;
    private bool _isJournalExpanded;
    private string _machineStateText = "DECONECTAT";
    private string _machineStateHeadline = "DECONECTAT  ·  Spider8  ·  50 Hz";
    private string _machineBandTooltip = "";
    private string _faultBannerText = "";
    private bool _showFaultBanner;
    private Brush _machineStateBrush = HealthBrushOff;
    private Brush _faultBannerBrush = HealthBrushLost;

    public bool IsExpertMode
    {
        get => _isExpertMode;
        set
        {
            if (_isExpertMode == value) return;
            _isExpertMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOperatorMode));
            LabUiPrefs.ExpertMode = value;
            OnPropertyChanged(nameof(DominantLiveChannel));
            OnPropertyChanged(nameof(HasDominantLiveChannel));
        }
    }

    public bool IsOperatorMode => !_isExpertMode;

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (_isDarkTheme == value) return;
            _isDarkTheme = value;
            OnPropertyChanged();
            LabUiPrefs.DarkTheme = value;
            try { LabTheme.Apply(value); } catch { /* theme is best-effort */ }
            try { RestyleAttachedPlotsForTheme(); } catch { /* plots may not be attached yet */ }
        }
    }

    public bool IsJournalExpanded
    {
        get => _isJournalExpanded;
        set
        {
            if (_isJournalExpanded == value) return;
            _isJournalExpanded = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(JournalPaneHeight));
            OnPropertyChanged(nameof(JournalExpandLabel));
        }
    }

    public double JournalPaneHeight => IsJournalExpanded ? 180 : 26;

    public string JournalExpandLabel => IsJournalExpanded ? "Restrânge" : "Extinde";

    public string LastJournalLine => JournalLines.Count > 0 ? JournalLines[0] : "";

    public ChannelRow? DominantLiveChannel =>
        SelectedChannelRow is { Enabled: true } ? SelectedChannelRow
        : Channels.FirstOrDefault(c => c.Enabled);

    public bool HasDominantLiveChannel => DominantLiveChannel is not null;

    public string MachineStateText
    {
        get => _machineStateText;
        private set { _machineStateText = value; OnPropertyChanged(); }
    }

    /// <summary>Short LIVE band: <c>LIVE  ·  Spider8  ·  50 Hz</c>. Serial / DEST stay in tooltip.</summary>
    public string MachineStateHeadline
    {
        get => _machineStateHeadline;
        private set { _machineStateHeadline = value; OnPropertyChanged(); }
    }

    public string MachineBandTooltip
    {
        get => _machineBandTooltip;
        private set { _machineBandTooltip = value; OnPropertyChanged(); }
    }

    public Brush MachineStateBrush
    {
        get => _machineStateBrush;
        private set { _machineStateBrush = value; OnPropertyChanged(); }
    }

    public bool ShowFaultBanner
    {
        get => _showFaultBanner;
        private set { _showFaultBanner = value; OnPropertyChanged(); }
    }

    public string FaultBannerText
    {
        get => _faultBannerText;
        private set { _faultBannerText = value; OnPropertyChanged(); }
    }

    public Brush FaultBannerBrush
    {
        get => _faultBannerBrush;
        private set { _faultBannerBrush = value; OnPropertyChanged(); }
    }

    public bool HasSelectedChannel => SelectedChannelRow is not null;

    public bool ShowRecDot => IsRecording;

    private void WireHmiPrefs()
    {
        try
        {
            _isExpertMode = LabUiPrefs.ExpertMode;
            _isDarkTheme = LabUiPrefs.DarkTheme;
            OnPropertyChanged(nameof(IsExpertMode));
            OnPropertyChanged(nameof(IsOperatorMode));
            OnPropertyChanged(nameof(IsDarkTheme));
            LabTheme.Apply(_isDarkTheme);
        }
        catch
        {
            /* defaults: Operator + light */
        }
    }

    private void RefreshMachineStateBand()
    {
        string text;
        Brush brush;

        if (_communicationLost && !IsConnected)
        {
            text = "COMUNICARE PIERDUTĂ";
            brush = HealthBrushLost;
        }
        else if (IsRecording)
        {
            text = "REC";
            brush = HealthBrushRec;
        }
        else if (IsStreaming)
        {
            text = "LIVE";
            brush = HealthBrushLive;
        }
        else if (IsConnecting)
        {
            text = "CONECTARE…";
            brush = HealthBrushUsb;
        }
        else if (IsConnected)
        {
            text = "CONECTAT";
            brush = HealthBrushIdleConnected;
        }
        else
        {
            text = "DECONECTAT";
            brush = HealthBrushOff;
        }

        MachineStateText = text;
        MachineStateBrush = brush;
        MachineStateHeadline = $"{text}  ·  {ShortDeviceFamily}  ·  {SampleRateHz} Hz";
        MachineBandTooltip = BuildMachineBandTooltip();
        OnPropertyChanged(nameof(ShowRecDot));
        RefreshFaultBanner();
    }

    /// <summary>Family name only — never COM serial or DEST transport string.</summary>
    private string ShortDeviceFamily
    {
        get
        {
            if (SelectedBackend.Contains("Simulator", StringComparison.OrdinalIgnoreCase))
                return "Simulator";
            var display = _device?.DisplayName;
            if (!string.IsNullOrWhiteSpace(display) &&
                display.Contains("Simulator", StringComparison.OrdinalIgnoreCase))
                return "Simulator";
            return "Spider8";
        }
    }

    private string BuildMachineBandTooltip()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(ConnectionHealthDetail))
            parts.Add(ConnectionHealthDetail.Trim());
        if (!string.IsNullOrWhiteSpace(ConnectedDeviceLabel)
            && (parts.Count == 0 || !parts[0].Contains(ConnectedDeviceLabel, StringComparison.Ordinal)))
            parts.Add(ConnectedDeviceLabel.Trim());
        if (!string.IsNullOrWhiteSpace(HardwareSummary))
            parts.Add(HardwareSummary.Trim());
        return string.Join("\n", parts);
    }

    internal void RefreshFaultBanner()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(RefreshFaultBanner);
            return;
        }

        if (_communicationLost && !IsConnected)
        {
            ShowFaultBanner = true;
            FaultBannerText = "Comunicare pierdută — USB/Spider8 indisponibil. Apăsați Conn după power-on.";
            FaultBannerBrush = HealthBrushLost;
            return;
        }

        var overflow = Channels.Where(c => c.Enabled && c.IsOverflow).Select(c => c.Name).ToList();
        if (overflow.Count > 0)
        {
            ShowFaultBanner = true;
            FaultBannerText = "Overflow — " + string.Join(", ", overflow);
            FaultBannerBrush = HealthBrushLost;
            return;
        }

        var badSignal = Channels
            .Where(c => c.Enabled && c.HasMeasurementSetup
                        && (c.SignalStatus is "lipsă" or "pierdut" or "lost"))
            .Select(c => c.Name)
            .ToList();
        if (IsStreaming && badSignal.Count > 0)
        {
            ShowFaultBanner = true;
            FaultBannerText = "Semnal nu OK / punte deschisă — " + string.Join(", ", badSignal);
            FaultBannerBrush = HealthBrushUsb;
            return;
        }

        ShowFaultBanner = false;
        FaultBannerText = "";
    }

    private void SetChannelsLinkLost(bool lost)
    {
        foreach (var ch in Channels)
            ch.IsLinkLost = lost;
        RefreshFaultBanner();
    }
}
