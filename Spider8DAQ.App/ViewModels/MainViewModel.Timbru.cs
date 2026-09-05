using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Spider8DAQ.Core.Acquisition;
using Spider8DAQ.Core.Calibration;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Sensors;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    private int _timbruChannelUi = 0;
    private string _timbruBridge = nameof(BridgeType.Half);
    private double _timbruGaugeFactor = 2.0;
    private double _timbruResistanceOhm = 350;
    private int _timbruQuarterCompOhm = 350;
    private string _timbruHalfConfig = StrainScale.HalfConfigActivDummyT;
    private double _timbruPoissonRatio = 0.3;
    private double _timbruBridgeFactor = 1.0;
    private bool _timbruBfOverride;
    private double _timbruComputedScale = 2000;
    private string _timbruStatus = "Timbru: Half + Activ + timbru pasiv (compensare T°) — GF, Calculează Scale, Aplică.";
    private bool _timbruAutorange;
    private double _timbruAutorangeDomain;
    private bool _autorangeLocked;
    private bool _autorangePickedFromLive;
    private bool _autorangePendingLive;
    private DateTime _lastAutorangeOverflowUtc = DateTime.MinValue;
    private string? _autorangeOverflowStatus;
    private double _autorangeSessionPeak;

    public ObservableCollection<string> TimbruBridgeTypes { get; } = new()
    {
        nameof(BridgeType.Quarter), nameof(BridgeType.Half), nameof(BridgeType.Full)
    };

    public ObservableCollection<int> TimbruQuarterCompOptions { get; } = new() { 120, 350, 700 };

    public ObservableCollection<string> TimbruHalfConfigs { get; } = new()
    {
        StrainScale.HalfConfigActivDummyT,
        StrainScale.HalfConfigSimplu,
        StrainScale.HalfConfigPoisson,
        "Încovoiere"
    };

    public ICommand TimbruCalculateScaleCommand { get; private set; } = null!;
    public ICommand TimbruApplyChannelCommand { get; private set; } = null!;
    public ICommand TimbruShowWiringCommand { get; private set; } = null!;
    public ICommand TimbruQuickSetupCommand { get; private set; } = null!;

    public int TimbruChannelUi
    {
        get => _timbruChannelUi;
        set
        {
            // Same numbering as grid: CH0…CH7 (hardware index).
            var max = Channels.Count > 0 ? Channels.Count - 1 : 7;
            _timbruChannelUi = Math.Clamp(value, 0, max);
            OnPropertyChanged();
        }
    }

    public string TimbruBridge
    {
        get => _timbruBridge;
        set
        {
            _timbruBridge = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimbruShowQuarterComp));
            OnPropertyChanged(nameof(TimbruShowHalfConfig));
            OnPropertyChanged(nameof(TimbruShowPoissonNu));
            ResetTimbruBridgeFactorFromExperiment();
            RecalcTimbruScaleSilent();
        }
    }

    public double TimbruGaugeFactor
    {
        get => _timbruGaugeFactor;
        set { _timbruGaugeFactor = value <= 0 ? 2.0 : value; OnPropertyChanged(); RecalcTimbruScaleSilent(); }
    }

    public double TimbruResistanceOhm
    {
        get => _timbruResistanceOhm;
        set { _timbruResistanceOhm = value; OnPropertyChanged(); }
    }

    public int TimbruQuarterCompOhm
    {
        get => _timbruQuarterCompOhm;
        set { _timbruQuarterCompOhm = value; OnPropertyChanged(); }
    }

    public string TimbruHalfConfig
    {
        get => _timbruHalfConfig;
        set
        {
            _timbruHalfConfig = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimbruShowPoissonNu));
            ResetTimbruBridgeFactorFromExperiment();
            RecalcTimbruScaleSilent();
        }
    }

    public double TimbruPoissonRatio
    {
        get => _timbruPoissonRatio;
        set
        {
            _timbruPoissonRatio = value;
            OnPropertyChanged();
            if (!_timbruBfOverride)
                ResetTimbruBridgeFactorFromExperiment();
            RecalcTimbruScaleSilent();
        }
    }

    public double TimbruBridgeFactor
    {
        get => _timbruBridgeFactor;
        set
        {
            _timbruBridgeFactor = value > 1e-9 ? value : 1.0;
            OnPropertyChanged();
            RecalcTimbruScaleSilent();
        }
    }

    public double TimbruComputedScale
    {
        get => _timbruComputedScale;
        set { _timbruComputedScale = value; OnPropertyChanged(); }
    }

    public string TimbruStatus
    {
        get => _timbruStatus;
        set { _timbruStatus = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Autorange: pick discrete µm/m domain before Rec, lock Scale+ASA during Rec,
    /// autoscale plot Y. Not AGC — range does not change mid-Rec.
    /// </summary>
    public bool TimbruAutorange
    {
        get => _timbruAutorange;
        set
        {
            if (_timbruAutorange == value) return;
            _timbruAutorange = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimbruShowAutorangeDomain));
            if (value)
            {
                _autorangeSessionPeak = 0;
                EnableAutorangePlotFollow();
                TryApplyStrainAutorange(push: IsConnected, preferLive: true);
            }
            else
            {
                _autorangePendingLive = false;
                _autorangePickedFromLive = false;
                _autorangeSessionPeak = 0;
                _autorangeOverflowStatus = null;
                ClearStrainOverflowFlags();
            }
        }
    }

    public double TimbruAutorangeDomain
    {
        get => _timbruAutorangeDomain;
        private set
        {
            _timbruAutorangeDomain = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimbruShowAutorangeDomain));
        }
    }

    public bool TimbruShowAutorangeDomain =>
        TimbruAutorange && TimbruAutorangeDomain > 0;

    public bool TimbruShowQuarterComp =>
        TimbruBridge.Equals(nameof(BridgeType.Quarter), StringComparison.OrdinalIgnoreCase);

    public bool TimbruShowHalfConfig =>
        TimbruBridge.Equals(nameof(BridgeType.Half), StringComparison.OrdinalIgnoreCase);

    public bool TimbruShowPoissonNu =>
        TimbruShowHalfConfig
        && TimbruHalfConfig.Contains("Poiss", StringComparison.OrdinalIgnoreCase);

    internal void WireTimbruCommands()
    {
        TimbruCalculateScaleCommand = new RelayCommand(CalculateTimbruScale);
        TimbruApplyChannelCommand = new RelayCommand(ApplyTimbruToChannel);
        TimbruShowWiringCommand = new RelayCommand(ShowTimbruWiring);
        TimbruQuickSetupCommand = new RelayCommand(OpenTimbruQuickSetup);
        RecalcTimbruScaleSilent();
    }

    private void OpenTimbruQuickSetup()
    {
        var owner = Application.Current?.MainWindow;
        var dlg = new Controls.TimbruQuickSetupWindow(this);
        if (owner is not null)
            dlg.Owner = owner;
        dlg.ShowDialog();
    }

    /// <summary>Called by <see cref="Controls.TimbruQuickSetupWindow"/> — fills Timbru fields and applies to channel.</summary>
    public void ApplyTimbruQuickSetup(int channelHw, string bridge, string halfConfig, double gf, double rOhm, double nu, int? quarterCompOhm, double? bridgeFactor = null)
    {
        TimbruChannelUi = channelHw;
        _timbruBfOverride = false;
        TimbruBridge = bridge;
        TimbruHalfConfig = NormalizeHalfConfig(halfConfig);
        TimbruGaugeFactor = gf;
        TimbruResistanceOhm = rOhm;
        TimbruPoissonRatio = nu;
        if (quarterCompOhm is int qc)
            TimbruQuarterCompOhm = qc;
        var bf = bridgeFactor is double userBf && userBf > 1e-9
            ? userBf
            : StrainScale.LabBridgeFactor(TimbruBridge, NormalizeHalfConfig(TimbruHalfConfig), TimbruPoissonRatio);
        _timbruBfOverride = true;
        TimbruBridgeFactor = bf;
        CalculateTimbruScale();
        ApplyTimbruToChannel();
        if (TimbruAutorange)
            TryApplyStrainAutorange(push: IsConnected, preferLive: true);
        HelpPanelText =
            "Asistent Timbru aplicat. Connect → Start → Zero (F9) fără sarcină → Record. " +
            "Timbrul pasiv (compensare T°) e în cablaj; softul a setat doar scara BF corectă." +
            (TimbruAutorange
                ? " Autorange a ales domeniul µm/m (nu forțează 2000 peste un domeniu mai larg)."
                : "");
    }

    private void ResetTimbruBridgeFactorFromExperiment()
    {
        _timbruBfOverride = false;
        _timbruBridgeFactor = StrainScale.LabBridgeFactor(
            TimbruBridge, NormalizeHalfConfig(TimbruHalfConfig), TimbruPoissonRatio);
        OnPropertyChanged(nameof(TimbruBridgeFactor));
    }

    private void RecalcTimbruScaleSilent()
    {
        var half = NormalizeHalfConfig(TimbruHalfConfig);
        TimbruComputedScale = StrainScale.FromLabBridgeFactor(
            TimbruBridge, TimbruGaugeFactor, half, TimbruBridgeFactor);
    }

    private static string NormalizeHalfConfig(string? cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg)) return StrainScale.HalfConfigActivDummyT;
        if (StrainScale.IsActivDummyT(cfg)) return StrainScale.HalfConfigActivDummyT;
        if (cfg.Contains("Poiss", StringComparison.OrdinalIgnoreCase)) return StrainScale.HalfConfigPoisson;
        if (cfg.Contains("ncovoi", StringComparison.OrdinalIgnoreCase)
            || cfg.Equals("Incovoiere", StringComparison.OrdinalIgnoreCase))
            return StrainScale.HalfConfigIncovoiere;
        if (cfg.Equals(StrainScale.HalfConfigSimplu, StringComparison.OrdinalIgnoreCase))
            return StrainScale.HalfConfigSimplu;
        return StrainScale.HalfConfigSimplu;
    }

    private void CalculateTimbruScale()
    {
        RecalcTimbruScaleSilent();
        TimbruStatus =
            $"Scale={TimbruComputedScale:0.####} µm/m per mV/V · GF={TimbruGaugeFactor:0.##} · BF={TimbruBridgeFactor:0.###} · {TimbruBridge}" +
            (TimbruShowHalfConfig ? $" / {TimbruHalfConfig}" : "") +
            (TimbruShowQuarterComp ? $" · Rcomp={TimbruQuarterCompOhm} Ω" : "");
        Status = "Timbru: " + TimbruStatus;
    }

    private void ApplyTimbruToChannel()
    {
        var hw = ResolveHwIndex(TimbruChannelUi);
        if (hw is null)
        {
            Status = "Timbru: canal invalid — folosește CH0…CH7 (ca în grila Măsurare).";
            return;
        }
        var ch = Channels[hw.Value];
        if (_autorangeLocked)
        {
            Status = "Autorange: Rec în curs — domeniul e blocat (nu se schimbă treapta din mers).";
            TimbruStatus = Status;
            return;
        }
        PushChannelEditUndo(ch);
        RecalcTimbruScaleSilent();
        ch.Bridge = TimbruBridge;
        ch.Unit = "µm/m";
        ch.ExcitationV = 2.5;
        ch.FilterHz = 5;
        ch.SensorName = StrainScale.IsActivDummyT(TimbruHalfConfig)
            ? "Timbru activ + pasiv (compensare T°)"
            : "Timbru SG";
        ch.SensorCategory = "tensometrie";
        ch.Enabled = true;
        ch.GaugeFactor = TimbruGaugeFactor;
        ch.GaugeOhm = TimbruResistanceOhm;
        ch.PoissonRatio = TimbruPoissonRatio;
        ch.BridgeFactor = TimbruBridgeFactor;
        ch.HalfConfig = TimbruShowHalfConfig ? NormalizeHalfConfig(TimbruHalfConfig) : null;
        // Rsh kΩ stays the grid/sensor shunt field — never store Rcomp here.
        TryFillInternalShuntOnTimbruChannel(ch);

        if (TimbruAutorange)
        {
            // Conversion Scale stays 4000/GF; domain (Capacity) + ASA range are picked.
            // Do not silently replace a wider Autorange domain with formula 2000.
            ch.Scale = TimbruComputedScale;
            ApplyAutorangeToChannel(ch, TryGetLiveStrainPeak());
        }
        else
        {
            ch.Scale = TimbruComputedScale;
        }

        if (_device is not null && IsConnected && !_autorangeLocked)
            _ = PushChannelConfigAsync(reapplyAcquisition: IsStreaming);
        TimbruStatus = $"Aplicat pe {ch.Name} (HW{ch.Index}): Scale={ch.Scale:0.####}, Bridge={ch.Bridge}" +
                       (TimbruShowHalfConfig ? $"/{TimbruHalfConfig}" : "") +
                       $", GF={ch.GaugeFactor:0.##}, BF={ch.BridgeFactor:0.###}, R={ch.GaugeOhm:0.#} Ω" +
                       (ch.ShuntKohm >= ShuntCheck.MinShuntKohm
                           ? $", Rsh={ch.ShuntKohm.ToString("0.##", CultureInfo.InvariantCulture)} kΩ"
                             + (ch.ShuntKohmFromDevice ? " (intern Spider8-30)" : "")
                           : "") +
                       (TimbruAutorange && TimbruAutorangeDomain > 0
                           ? $", Autorange {TimbruAutorangeDomain:0} µm/m · {ch.RangeMvPerV:0} mV/V (domeniul nu e treapta de shunt)"
                           : "") +
                       ", Exc=2.5 V, Filtru=5 Hz. Shunt CH folosește GF/R/punte + coloana Rsh kΩ.";
        Status = TimbruStatus;
        HelpPanelText = StrainScale.IsActivDummyT(TimbruHalfConfig)
            ? "Activ pe piesă + timbru pasiv (compensare T°) pe același canal — Zero (F9) fără sarcină după Start. Shunt intern NU e în punte (nu pin 120); PASS (Half+dummy) la residual, nu FAIL 1893. Verificare: apăsare pe activ."
            : "Timbru aplicat — Zero (F9) fără sarcină după Start. Shunt CH folosește aceste valori + Rsh kΩ.";
        _journal.Setup(Status);
        foreach (var g in LiveGauges)
        {
            if (ResolveGaugeChannelIndex(g) == hw.Value)
                RefreshGaugeAvailableModes(g);
        }
    }

    private void ShowTimbruWiring()
    {
        var bridge = Enum.TryParse<BridgeType>(TimbruBridge, true, out var b) ? b : BridgeType.Half;
        var half = NormalizeHalfConfig(TimbruHalfConfig);
        var text = WiringDiagrams.Describe(bridge, half) + Environment.NewLine + Environment.NewLine +
                   WiringDiagrams.AsciiArt(bridge, half) + Environment.NewLine + Environment.NewLine +
                   Db15WiringGuide.FullText();
        WiringText = text;
        HelpPanelText = "Cablare timbru / punte — " + TimbruBridge +
                        (TimbruShowHalfConfig ? " / " + TimbruHalfConfig : "");
        MessageBox.Show(text, "Cablare timbru / punte tensometrică", MessageBoxButton.OK, MessageBoxImage.Information);
        Status = "Cablare timbru afișată.";
    }

    internal bool TryApplyStrainAutorange(bool push, bool preferLive)
    {
        if (!TimbruAutorange || _autorangeLocked)
            return false;

        var peak = preferLive ? TryGetLiveStrainPeak() : null;
        if (peak is null && preferLive && IsStreaming && !_autorangePickedFromLive)
            _autorangePendingLive = true;

        var changed = false;
        foreach (var ch in Channels)
        {
            if (!ch.Enabled || !IsAutorangeStrainChannel(ch))
                continue;
            changed |= ApplyAutorangeToChannel(ch, peak);
        }

        if (changed && TimbruAutorangeDomain > 0)
        {
            EnableAutorangePlotFollow();
            var src = peak is double p && p > 0 ? $"vârf live |ε|={p:0.#}" : "epruvetă / GF";
            var msg =
                $"Autorange: domeniu {TimbruAutorangeDomain:0} µm/m ({src}) · " +
                $"Scale GF={TimbruComputedScale:0.####} · blocat la Rec.";
            TimbruStatus = msg;
            if (!IsRecording)
                Status = msg;
        }

        if (changed && push && _device is not null && IsConnected && !_autorangeLocked)
            _ = PushChannelConfigAsync(reapplyAcquisition: IsStreaming);

        if (peak is double live && live > 0)
        {
            _autorangePickedFromLive = true;
            _autorangePendingLive = false;
        }

        return changed;
    }

    internal void LockStrainAutorangeForRecord()
    {
        // Range already written on Aplică / Connect / Start / live peak. Rec only locks.
        _autorangeLocked = true;
        _autorangePendingLive = false;
    }

    internal void UnlockStrainAutorangeAfterRecord()
    {
        _autorangeLocked = false;
        _autorangeOverflowStatus = null;
    }

    internal void MaybeRefineAutorangeFromLive()
    {
        if (!TimbruAutorange || _autorangeLocked || _autorangePickedFromLive)
            return;
        if (!_autorangePendingLive && !IsStreaming)
            return;
        if (TryGetLiveStrainPeak() is null)
            return;
        TryApplyStrainAutorange(push: true, preferLive: true);
    }

    internal string? CheckStrainAutorangeOverflow(ProcessedSample sample)
    {
        if (!TimbruAutorange)
            return null;

        string? first = null;
        foreach (var ch in Channels)
        {
            if (!ch.Enabled || !IsAutorangeStrainChannel(ch))
                continue;
            if (ch.Index < 0 || ch.Index >= sample.Physical.Length)
                continue;
            var v = sample.Physical[ch.Index];
            var domain = ch.Capacity > 0
                ? ch.Capacity
                : StrainAutorange.SnapToList(Math.Max(StrainAutorange.DomainStepsUmPerM[0], ResolveTimbruFormulaScale(ch)));
            var overflow = StrainAutorange.IsOverflow(v, domain);
            ch.IsOverflow = overflow;
            if (!overflow || !IsRecording)
                continue;
            first ??= StrainAutorange.OverflowMessage(ch.Name);
        }

        if (first is null)
        {
            _autorangeOverflowStatus = null;
            return null;
        }

        _autorangeOverflowStatus = first;
        if ((DateTime.UtcNow - _lastAutorangeOverflowUtc).TotalSeconds >= 4)
        {
            _lastAutorangeOverflowUtc = DateTime.UtcNow;
            try { RefreshLabAdvisorFromStatus(first); } catch { /* advisor must never break live */ }
        }

        return first;
    }

    private bool ApplyAutorangeToChannel(ChannelRow ch, double? livePeak)
    {
        if (_autorangeLocked)
            return false;

        RecalcTimbruScaleSilent();
        var formula = ResolveTimbruFormulaScale(ch);

        var domain = StrainAutorange.PickDomain(livePeak, SpecimenClass, formula);
        // Asistent / Connect without a real live peak must not shrink a wider domain to 2000.
        if (!StrainAutorange.IsMeaningfulPeak(livePeak, formula)
            && ch.Capacity >= domain
            && ch.Capacity >= StrainAutorange.DomainStepsUmPerM[0])
            domain = StrainAutorange.SnapToList(ch.Capacity);
        var range = StrainAutorange.HardwareRangeMvPerV(domain, formula);
        var pinned = StrainScale.PinTimbruScale(ch.Scale, formula);
        var changed = Math.Abs(ch.Capacity - domain) > 0.5
                      || Math.Abs(ch.RangeMvPerV - range) > 0.05
                      || Math.Abs(ch.Scale - pinned) > 0.05;

        // Conversion Scale stays GF-based (4000/GF). Domain is Capacity + ASA range — never Scale.
        ch.Scale = pinned;
        ch.Capacity = domain;
        ch.RangeMvPerV = range;
        ch.AlarmEnabled = true;
        ch.AlarmHigh = domain * StrainAutorange.OverflowFraction;
        ch.AlarmLow = -domain * StrainAutorange.OverflowFraction;
        TimbruAutorangeDomain = domain;
        return changed;
    }

    private double? TryGetLiveStrainPeak()
    {
        var peak = 0.0;
        var any = false;
        var latest = Volatile.Read(ref _latestUiSample);
        if (latest is not null)
        {
            foreach (var ch in Channels)
            {
                if (!ch.Enabled || !IsAutorangeStrainChannel(ch))
                    continue;
                if (ch.Index < 0 || ch.Index >= latest.Physical.Length)
                    continue;
                var v = latest.Physical[ch.Index];
                if (!double.IsFinite(v)) continue;
                peak = Math.Max(peak, Math.Abs(v));
                any = true;
            }
        }

        foreach (var ch in Channels)
        {
            if (!ch.Enabled || !IsAutorangeStrainChannel(ch))
                continue;
            if (!_liveYValueByChannel.TryGetValue(ch.Index, out var q) || q.Count == 0)
                continue;
            foreach (var v in q)
            {
                if (!double.IsFinite(v)) continue;
                peak = Math.Max(peak, Math.Abs(v));
                any = true;
            }
        }

        if (any && peak > _autorangeSessionPeak)
            _autorangeSessionPeak = peak;
        var use = Math.Max(peak, _autorangeSessionPeak);
        return use > 0 ? use : null;
    }

    private static bool IsAutorangeStrainChannel(ChannelRow ch)
    {
        if (StrainScale.IsStrainUnit(ch.Unit))
            return true;
        var cat = ch.SensorCategory ?? "";
        if (cat.Contains("tensometr", StringComparison.OrdinalIgnoreCase)
            || cat.Equals(global::Spider8DAQ.Core.Sensors.SensorCategories.Strain, StringComparison.OrdinalIgnoreCase))
            return true;
        var name = ch.SensorName ?? "";
        return name.Contains("Timbru", StringComparison.OrdinalIgnoreCase)
               || name.Contains("tensometr", StringComparison.OrdinalIgnoreCase);
    }

    private void EnableAutorangePlotFollow()
    {
        if (_followLiveZoom)
        {
            foreach (var panel in ChannelPlots)
            {
                if (Channels.FirstOrDefault(c => c.Index == panel.ChannelIndex) is { } ch
                    && IsAutorangeStrainChannel(ch))
                    panel.FollowLive = true;
            }
            return;
        }

        _followLiveZoom = true;
        OnPropertyChanged(nameof(FollowLiveZoom));
        foreach (var panel in ChannelPlots)
            panel.FollowLive = true;
    }

    private void ClearStrainOverflowFlags()
    {
        foreach (var ch in Channels)
            ch.IsOverflow = false;
    }

    /// <summary>GF formula only — never Capacity, ASA range, or a polluted grid Scale (91885).</summary>
    internal double ResolveTimbruFormulaScale(ChannelRow ch)
    {
        RecalcTimbruScaleSilent();
        if (ch.GaugeFactor > 1e-9)
        {
            var nu = ch.PoissonRatio > 0 ? ch.PoissonRatio : 0.3;
            var labBf = ch.BridgeFactor > 1e-9
                ? ch.BridgeFactor
                : StrainScale.LabBridgeFactor(ch.Bridge, ch.HalfConfig, nu);
            var fromCh = StrainScale.FromLabBridgeFactor(ch.Bridge, ch.GaugeFactor, ch.HalfConfig, labBf);
            if (fromCh >= 10)
                return fromCh;
        }

        return TimbruComputedScale >= 10 ? TimbruComputedScale : 4000.0 / 2.12;
    }

    /// <summary>Rewrite domain / 91885 out of Timbru Scale. Returns true if any channel changed.</summary>
    internal bool SanitizeTimbruScales()
    {
        var changed = false;
        foreach (var ch in Channels)
        {
            if (!IsAutorangeStrainChannel(ch) && !IsStrainOrTimbruChannel(ch))
                continue;
            var formula = ResolveTimbruFormulaScale(ch);
            var pinned = StrainScale.PinTimbruScale(ch.Scale, formula);
            if (Math.Abs(ch.Scale - pinned) < 0.05)
                continue;
            ch.Scale = pinned;
            changed = true;
        }

        return changed;
    }
}
