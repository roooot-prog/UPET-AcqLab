using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using Spider8DAQ.App.Controls;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Display;
using Spider8DAQ.Core.MathChannels;
using Spider8DAQ.Core.Sensors;

namespace Spider8DAQ.App.ViewModels;

public sealed class ChannelRow : INotifyPropertyChanged
{
    private string _name = "";
    private string _unit = "";
    private bool _enabled = true;
    private bool _showOnPlot = true;
    private double _scale = 1;
    private double _offset;
    private double _tare;
    private bool _alarmEnabled;
    private double _alarmLow = -1e9;
    private double _alarmHigh = 1e9;
    private string _liveReading = "—";
    private bool _hasSignal;
    private string? _sensorName;
    private string? _sensorCategory;
    private double _capacity;
    private string _bridge = nameof(BridgeType.Half);
    private double _filterHz = 10;
    private int _channelSampleRateHz = 50;
    private bool _recordEnabled = true;
    private double _excitationV = 2.5;
    private double _shuntKohm;
    private double _gaugeFactor;
    private double _gaugeOhm;
    private string? _halfConfig;
    private double _poissonRatio;
    private double _bridgeFactor;
    private double _rangeMvPerV = 2;
    private bool _isOverflow;
    private bool _isLinkLost;

    private static readonly Brush LedOk = FreezeLed(0x00, 0xB3, 0x4A);
    private static readonly Brush LedWarn = FreezeLed(0xE6, 0xA0, 0x00);
    private static readonly Brush LedFault = FreezeLed(0xC8, 0x10, 0x2E);
    private static readonly Brush LedOff = FreezeLed(0x5A, 0x65, 0x73);

    private static Brush FreezeLed(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private int _index;
    public int Index
    {
        get => _index;
        set
        {
            if (_index == value) return;
            _index = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ChannelAccentBrush));
            OnPropertyChanged(nameof(ChannelAccentHex));
        }
    }
    public int DeviceIndex { get; set; }
    /// <summary>Stable color for CH index (grid accent, plots, CWT).</summary>
    public Brush ChannelAccentBrush => ChannelPalette.GetWpfBrush(Index);
    public string ChannelAccentHex => ChannelPalette.GetHex(Index);
    public string Name
    {
        get => _name;
        set
        {
            _name = value;
            OnPropertyChanged();
            NotifyMeasurementSetup();
        }
    }
    public string Unit
    {
        get => _unit;
        set
        {
            _unit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScaleLooksAbsurd));
            OnPropertyChanged(nameof(ScaleHint));
            OnPropertyChanged(nameof(CitireUnitDisplay));
            NotifyMeasurementSetup();
        }
    }
    public bool Enabled
    {
        get => _enabled;
        set
        {
            _enabled = value;
            OnPropertyChanged();
            NotifySignal();
            if (!value)
                HideInvalidLiveReading();
            if (value && !ShowOnPlot) ShowOnPlot = true;
            if (value && !RecordEnabled) RecordEnabled = true;
        }
    }
    /// <summary>Show channel on live Y(t) plot (independent of DAQ On).</summary>
    public bool ShowOnPlot
    {
        get => _showOnPlot;
        set { _showOnPlot = value; OnPropertyChanged(); }
    }
    /// <summary>Include channel in CSV recording (Easy-style store flag).</summary>
    public bool RecordEnabled
    {
        get => _recordEnabled;
        set { _recordEnabled = value; OnPropertyChanged(); }
    }
    public double Scale
    {
        get => _scale;
        set
        {
            _scale = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScaleLooksAbsurd));
            OnPropertyChanged(nameof(ScaleHint));
            OnPropertyChanged(nameof(ScaleDisplay));
            OnPropertyChanged(nameof(ScaleExactDisplay));
        }
    }

    /// <summary>Rounded Scale for the grid / selected panel (underlying double unchanged).</summary>
    public string ScaleDisplay => EngineeringDisplay.FormatScale(_scale);

    /// <summary>Full-precision Scale for Expert tooltip / optional line.</summary>
    public string ScaleExactDisplay => EngineeringDisplay.FormatScale(_scale, fullPrecision: true);

    /// <summary>True when grid Scale is not GF conversion (91885, domain 10k…).</summary>
    public bool ScaleLooksAbsurd =>
        StrainScale.IsStrainUnit(Unit) && StrainScale.LooksAbsurdTimbruScale(_scale);

    public string ScaleHint =>
        ScaleLooksAbsurd
            ? ShuntCheck.ScaleInvalidLabel + " (4000/GF, nu Autorange/Capacity)"
            : "";
    public double Offset { get => _offset; set { _offset = value; OnPropertyChanged(); } }
    public double TareValue
    {
        get => _tare;
        set { _tare = value; OnPropertyChanged(); OnPropertyChanged(nameof(ZeroDisplay)); }
    }
    public string? SensorId { get; set; }
    public string? SensorName
    {
        get => _sensorName;
        set
        {
            _sensorName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SensorFunctionDisplay));
            NotifyMeasurementSetup();
        }
    }
    public string? SensorCategory
    {
        get => _sensorCategory;
        set { _sensorCategory = value; OnPropertyChanged(); }
    }
    public double Capacity
    {
        get => _capacity;
        set
        {
            _capacity = value;
            OnPropertyChanged();
            NotifyMeasurementSetup();
        }
    }
    public string Bridge
    {
        get => _bridge;
        set
        {
            _bridge = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SensorFunctionDisplay));
            OnPropertyChanged(nameof(DummySensorDisplay));
            OnPropertyChanged(nameof(BridgeGfDisplay));
            NotifyMeasurementSetup();
        }
    }
    public double RangeMvPerV
    {
        get => _rangeMvPerV;
        set
        {
            _rangeMvPerV = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RangeDisplay));
        }
    }
    public bool IsOverflow
    {
        get => _isOverflow;
        set
        {
            if (_isOverflow == value) return;
            _isOverflow = value;
            OnPropertyChanged();
            NotifySignal();
            if (value)
                HideLiveReadingNow();
        }
    }

    /// <summary>UI-only: USB/Spider8 link lost for this row (does not change DAQ physics).</summary>
    public bool IsLinkLost
    {
        get => _isLinkLost;
        set
        {
            if (_isLinkLost == value) return;
            _isLinkLost = value;
            OnPropertyChanged();
            NotifySignal();
            if (value)
                HideLiveReadingNow();
        }
    }
    public double FilterHz
    {
        get => _filterHz;
        set { _filterHz = value; OnPropertyChanged(); OnPropertyChanged(nameof(RateFilterDisplay)); }
    }
    public int ChannelSampleRateHz
    {
        get => _channelSampleRateHz;
        set { _channelSampleRateHz = value; OnPropertyChanged(); OnPropertyChanged(nameof(RateFilterDisplay)); }
    }
    public double ExcitationV
    {
        get => _excitationV;
        set
        {
            _excitationV = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExcitationDisplay));
        }
    }
    private bool _assigningInternalShunt;
    private bool _shuntKohmFromDevice;

    public double ShuntKohm
    {
        get => _shuntKohm;
        set
        {
            _shuntKohm = value;
            if (!_assigningInternalShunt)
                _shuntKohmFromDevice = false;
            OnPropertyChanged();
        }
    }

    /// <summary>True when Rsh was filled from Spider8-30 internal-shunt table (not typed by the user).</summary>
    public bool ShuntKohmFromDevice => _shuntKohmFromDevice;

    /// <summary>Fill empty Rsh, or replace a previous device auto-fill. Never overwrites a user/sensor value.</summary>
    public bool TryApplyInternalShuntKohm(double kohm)
    {
        if (kohm < 1.0) return false;
        var empty = _shuntKohm < 1.0;
        if (!empty && !_shuntKohmFromDevice)
            return false;
        if (!empty && Math.Abs(_shuntKohm - kohm) < 1e-9)
            return false;
        _assigningInternalShunt = true;
        try
        {
            _shuntKohmFromDevice = true;
            ShuntKohm = kohm;
        }
        finally
        {
            _assigningInternalShunt = false;
        }
        return true;
    }
    /// <summary>GF written by Asistent Timbru / Aplică pe canal (0 = not applied).</summary>
    public double GaugeFactor
    {
        get => _gaugeFactor;
        set
        {
            _gaugeFactor = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GaugeFactorDisplay));
            OnPropertyChanged(nameof(BridgeGfDisplay));
            NotifyMeasurementSetup();
        }
    }
    /// <summary>Gauge R (Ω) from Timbru.</summary>
    public double GaugeOhm
    {
        get => _gaugeOhm;
        set { _gaugeOhm = value; OnPropertyChanged(); }
    }
    public string? HalfConfig
    {
        get => _halfConfig;
        set
        {
            _halfConfig = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DummySensorDisplay));
            NotifyMeasurementSetup();
        }
    }
    public double PoissonRatio
    {
        get => _poissonRatio;
        set { _poissonRatio = value; OnPropertyChanged(); }
    }
    /// <summary>Lab BF from Asistent Timbru (0 = derive from Bridge/HalfConfig/ν).</summary>
    public double BridgeFactor
    {
        get => _bridgeFactor;
        set { _bridgeFactor = value; OnPropertyChanged(); }
    }
    public bool ShuntEnabled { get; set; }
    private double _lastShuntReading;
    public double LastShuntReading
    {
        get => _lastShuntReading;
        set { _lastShuntReading = value; OnPropertyChanged(); }
    }
    public double LastShuntExpectedUe { get; set; }
    public bool LastShuntCanApplyScale { get; set; }
    public bool AlarmEnabled { get => _alarmEnabled; set { _alarmEnabled = value; OnPropertyChanged(); } }
    public double AlarmLow { get => _alarmLow; set { _alarmLow = value; OnPropertyChanged(); } }
    public double AlarmHigh { get => _alarmHigh; set { _alarmHigh = value; OnPropertyChanged(); } }

    /// <summary>Live value shown in the DAQ channel grid (updated while streaming).</summary>
    public string LiveReading
    {
        get => _liveReading;
        set
        {
            _liveReading = value;
            var ok = Enabled
                && !IsOverflow
                && !IsLinkLost
                && HasMeasurementSetup
                && !LiveCitire.IsPlaceholder(value);
            // Zero after tare is a valid live reading (P15/bar at atmosphere; CH1 dummy ≈ 0.4).
            if (_hasSignal != ok)
            {
                _hasSignal = ok;
                OnPropertyChanged(nameof(HasSignal));
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(CitireUnitDisplay));
            NotifySignal();
        }
    }

    public bool HasSignal => _hasSignal;

    /// <summary>True when Timbru / catalog sensor / process amp is assigned — empty unused slots are not.</summary>
    public bool HasMeasurementSetup =>
        LiveCitire.HasMeasurementSetup(
            SensorName, SensorId, GaugeFactor, HalfConfig, Bridge, Capacity, Unit, Name);

    /// <summary>Unit next to Citire; blank when the reading is a placeholder.</summary>
    public string CitireUnitDisplay =>
        LiveCitire.IsPlaceholder(_liveReading) ? "" : Unit;

    /// <summary>Compact status: OK / aștept / lipsă / off / lost. Unused On (no sensor) is lipsă, not OK.</summary>
    public string SignalStatus =>
        !Enabled ? "off"
        : IsLinkLost ? "pierdut"
        : IsOverflow ? "Overflow"
        : !HasMeasurementSetup ? "lipsă"
        : _hasSignal ? "OK"
        : LiveReading is "—" or "" or null ? "aștept"
        : "lipsă";

    /// <summary>Instrument headline for the selected-channel pane (not a grid chip).</summary>
    public string SignalHeadline =>
        SignalStatus switch
        {
            "OK" => "Semnal OK",
            "off" => "Off",
            "pierdut" or "lost" => "Pierdut",
            "Overflow" => "Overflow",
            "lipsă" => "Lipsă",
            "aștept" => "Aștept",
            _ => SignalStatus
        };

    public Brush SignalHeadlineBrush =>
        SignalStatus == "OK" ? LedOk
        : SignalStatus is "lipsă" or "Overflow" or "pierdut" or "lost" ? LedFault
        : SignalStatus == "aștept" ? LedWarn
        : LedOff;

    public string BridgeGfDisplay
    {
        get
        {
            var gf = GaugeFactorDisplay;
            return gf == "—" ? Bridge : $"{Bridge} · GF {gf}";
        }
    }

    public Brush ChannelLedBrush =>
        !Enabled ? LedOff
        : IsLinkLost || IsOverflow || SignalStatus is "lipsă" ? LedFault
        : SignalStatus == "OK" ? LedOk
        : SignalStatus == "aștept" ? LedWarn
        : LedOff;

    public string ChannelLedTooltip =>
        !Enabled ? "inactiv"
        : IsLinkLost ? "comunicare pierdută"
        : IsOverflow ? "overflow"
        : SignalStatus == "lipsă" ? "semnal nu OK / punte deschisă"
        : SignalStatus == "OK" ? "OK"
        : SignalStatus == "aștept" ? "aștept semnal"
        : SignalStatus;

    public string GaugeFactorDisplay => EngineeringDisplay.FormatGaugeFactor(_gaugeFactor);
    public string ExcitationDisplay => EngineeringDisplay.FormatExcitation(_excitationV) + " V";
    public string RangeDisplay => EngineeringDisplay.FormatRange(_rangeMvPerV) + " mV/V";
    public string DummySensorDisplay =>
        string.IsNullOrWhiteSpace(_halfConfig)
            ? (string.IsNullOrWhiteSpace(SensorName) ? "—" : SensorName)
            : _halfConfig;

    public string RateFilterDisplay => $"{ChannelSampleRateHz} Hz · BE {FilterHz:0.#}";
    public string SensorFunctionDisplay =>
        string.IsNullOrWhiteSpace(SensorName)
            ? LiveCitire.Placeholder
            : string.IsNullOrWhiteSpace(Bridge) ? SensorName : $"{SensorName} · {Bridge}";
    public string ZeroDisplay => $"{TareValue:0.###}";

    private void NotifyLed()
    {
        OnPropertyChanged(nameof(ChannelLedBrush));
        OnPropertyChanged(nameof(ChannelLedTooltip));
    }

    private void NotifySignal()
    {
        OnPropertyChanged(nameof(SignalStatus));
        OnPropertyChanged(nameof(SignalHeadline));
        OnPropertyChanged(nameof(SignalHeadlineBrush));
        NotifyLed();
    }

    private void NotifyMeasurementSetup()
    {
        OnPropertyChanged(nameof(HasMeasurementSetup));
        NotifySignal();
        HideInvalidLiveReading();
    }

    /// <summary>Off or unused row: freeze hidden — do not leave a leftover engineering Citire.</summary>
    private void HideInvalidLiveReading()
    {
        if (Enabled && HasMeasurementSetup && !IsOverflow && !IsLinkLost)
            return;
        HideLiveReadingNow();
    }

    private void HideLiveReadingNow()
    {
        if (_liveReading == LiveCitire.Placeholder)
            return;
        LiveReading = LiveCitire.Placeholder;
    }

    public bool IsInAlarm(double physical)
    {
        if (!AlarmEnabled || double.IsNaN(physical)) return false;
        return physical < AlarmLow || physical > AlarmHigh;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public sealed class MathChannelRow
{
    public string Name { get; set; } = "Math";
    public string Unit { get; set; } = "";
    public string Operation { get; set; } = nameof(MathOp.Average);
    public int SourceA { get; set; }
    public int SourceB { get; set; }
    public int SourceC { get; set; }
    public int WindowSize { get; set; } = 25;
    public bool Enabled { get; set; } = true;
    public string Formula { get; set; } = "";
}

public sealed class LiveValueRow : INotifyPropertyChanged
{
    private string _display = "—";
    private bool _isAlarm;
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Display
    {
        get => _display;
        set { _display = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display))); }
    }
    public bool IsAlarm
    {
        get => _isAlarm;
        set { _isAlarm = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAlarm))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class StatsRow
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public string Min { get; set; } = "";
    public string Max { get; set; } = "";
    public string Mean { get; set; } = "";
    public string StdDev { get; set; } = "";
    public string Rms { get; set; } = "";
    public string PeakToPeak { get; set; } = "";
    public string Crest { get; set; } = "";
}

public sealed class FormulaRow
{
    public string Name { get; set; } = "";
    public string Expression { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class DeviceRow
{
    public int Index { get; set; }
    public string Name { get; set; } = "Spider8";
    public bool Enabled { get; set; } = true;
    public string? ComPort { get; set; }
    public string Notes { get; set; } = "";
}

public sealed class MacroStepRow
{
    public string Type { get; set; } = "WaitMs";
    public int IntParam { get; set; }
    public string Text { get; set; } = "";
}

public sealed class SensorListRow
{
    public string Code { get; set; } = "";
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public double Capacity { get; set; }
    public string Bridge { get; set; } = "";
    public double Scale { get; set; }
    public double Offset { get; set; }
    public double Sensitivity { get; set; } = 1;
    public double ExcitationV { get; set; }
    public double ShuntKohm { get; set; }
    public string Notes { get; set; } = "";
    public string Detail => $"{Code}  ·  {Unit}  ·  cap {Capacity}  ·  {Bridge}";
    public string DetailsText =>
        $"Cod: {Code}\nNume: {Name}\nCategorie: {Category}\nUnitate: {Unit}\n" +
        $"Sensibilitate / Scale: {Sensitivity:G6} / {Scale:G6}\nOffset: {Offset:G6}\n" +
        $"Capacitate: {Capacity}\nPunte: {Bridge}\nExcitație: {ExcitationV:0.##} V\n" +
        (ShuntKohm > 0 ? $"R-Shunt: {ShuntKohm:0.##} kΩ\n" : "") +
        (string.IsNullOrWhiteSpace(Notes) ? "" : $"Note: {Notes}");
}

public sealed class SensorCategoryGroup
{
    public string Category { get; set; } = "";
    public ObservableCollection<SensorListRow> Sensors { get; } = new();
    public int Count => Sensors.Count;
    public string Header => $"{Category}  ({Count})";
}

public sealed class RelayCommand : ICommand
{
    private readonly Func<Task>? _asyncExecute;
    private readonly Action? _execute;
    private readonly Action<object?>? _paramExecute;
    private readonly Func<bool>? _canExecute;
    private readonly Func<object?, bool>? _paramCanExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _asyncExecute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _paramExecute = execute;
        _paramCanExecute = canExecute;
    }

    public bool CanExecute(object? parameter) =>
        _paramCanExecute?.Invoke(parameter) ?? _canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter)
    {
        try
        {
            if (_paramExecute is not null) _paramExecute(parameter);
            else if (_asyncExecute is not null) await _asyncExecute();
            else _execute?.Invoke();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                Spider8DAQ.Core.AppPaths.FriendlyIoMessage(ex),
                "UPET AcqLab",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
