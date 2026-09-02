using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Spider8DAQ.App.ViewModels;

/// <summary>One draggable live gauge bound to a DAQ channel (CH0–CH7 or Auto).</summary>
public sealed class LiveGaugeItemViewModel : INotifyPropertyChanged
{
    public const string ForceUnitAuto = "Auto";
    public const string ForceUnitN = "N";
    public const string ForceUnitKn = "kN";

    private int _channelIndex = -1;
    private string _displayMode = MainViewModel.GaugeModeNative;
    private double _left = 600;
    private double _top = 420;
    private double _width = 240;
    private double _height = 300;
    private double _value = double.NaN;
    private string _display = "—";
    private string _unit = "";
    private double _gaugeMin;
    private double _gaugeMax = 100;
    private string _channelLabel = "Auto";
    private double _sourcePhysical = double.NaN;
    private string _sourceBarDisplay = "";
    private string _subtitle = "";
    private bool _showPressureDual;
    private bool _autoScale = true;
    private string _secondaryDisplay = "";
    private bool _showForceDual;
    private string _forceUnit = ForceUnitAuto;

    public LiveGaugeItemViewModel(string id)
    {
        Id = id;
    }

    public string Id { get; }

    public int ChannelIndex
    {
        get => _channelIndex;
        set
        {
            if (_channelIndex == value) return;
            _channelIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(ShowPressureToKgOption));
        }
    }

    public string DisplayMode
    {
        get => _displayMode;
        set
        {
            var mode = string.IsNullOrWhiteSpace(value) ? MainViewModel.GaugeModeNative : value;
            if (_displayMode == mode) return;
            _displayMode = mode;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowPressureToKgOption));
        }
    }

    public double Left
    {
        get => _left;
        set { _left = value; OnPropertyChanged(); }
    }

    public double Top
    {
        get => _top;
        set { _top = value; OnPropertyChanged(); }
    }

    public double Width
    {
        get => _width;
        set { _width = Math.Max(200, value); OnPropertyChanged(); }
    }

    public double Height
    {
        get => _height;
        set { _height = Math.Max(280, value); OnPropertyChanged(); }
    }

    public double Value
    {
        get => _value;
        set { _value = value; OnPropertyChanged(); }
    }

    public string Display
    {
        get => _display;
        set { _display = value; OnPropertyChanged(); }
    }

    public string Unit
    {
        get => _unit;
        set { _unit = value; OnPropertyChanged(); }
    }

    public double GaugeMin
    {
        get => _gaugeMin;
        set { _gaugeMin = value; OnPropertyChanged(); }
    }

    public double GaugeMax
    {
        get => _gaugeMax;
        set { _gaugeMax = value; OnPropertyChanged(); }
    }

    public string ChannelLabel
    {
        get => _channelLabel;
        set { _channelLabel = value; OnPropertyChanged(); OnPropertyChanged(nameof(Title)); }
    }

    public string Title => ChannelIndex < 0
        ? "Cadran — Auto"
        : $"Cadran — CH{ChannelIndex}";

    internal double SourcePhysical
    {
        get => _sourcePhysical;
        set => _sourcePhysical = value;
    }

    private bool _showPressureToKgOption;
    private readonly ObservableCollection<LiveGaugeDisplayModeOption> _availableDisplayModes = new()
    {
        new(MainViewModel.GaugeModeNative, "Nativ (unitate senzor)")
    };

    public ObservableCollection<LiveGaugeDisplayModeOption> AvailableDisplayModes => _availableDisplayModes;

    public void SetAvailableDisplayModes(IReadOnlyList<LiveGaugeDisplayModeOption> modes)
    {
        _availableDisplayModes.Clear();
        foreach (var m in modes)
            _availableDisplayModes.Add(m);
        OnPropertyChanged(nameof(AvailableDisplayModes));
    }

    public bool ShowPressureToKgOption
    {
        get => _showPressureToKgOption;
        set { _showPressureToKgOption = value; OnPropertyChanged(); }
    }

    /// <summary>When in presiune→kg mode: formatted bar reading from channel.</summary>
    public string SourceBarDisplay
    {
        get => _sourceBarDisplay;
        set { _sourceBarDisplay = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowPressureDual)); }
    }

    /// <summary>Subtitle under gauge (e.g. A=201.06 cm²).</summary>
    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; OnPropertyChanged(); }
    }

    public bool ShowPressureDual
    {
        get => _showPressureDual;
        set { _showPressureDual = value; OnPropertyChanged(); }
    }


    public bool AutoScale
    {
        get => _autoScale;
        set { if (_autoScale == value) return; _autoScale = value; OnPropertyChanged(); }
    }

    /// <summary>Primary force readout: Auto (channel unit), N, or kN.</summary>
    public string ForceUnit
    {
        get => _forceUnit;
        set
        {
            var u = string.IsNullOrWhiteSpace(value) ? ForceUnitAuto : value.Trim();
            if (_forceUnit == u) return;
            _forceUnit = u;
            OnPropertyChanged();
        }
    }

    /// <summary>Secondary line e.g. "1.114 kN" or "113.5 kg" when primary is N.</summary>
    public string SecondaryDisplay
    {
        get => _secondaryDisplay;
        set { _secondaryDisplay = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowForceDual)); }
    }

    public bool ShowForceDual
    {
        get => _showForceDual;
        set { _showForceDual = value; OnPropertyChanged(); }
    }

    public bool ShowSecondaryLine => ShowPressureDual || ShowForceDual;
    public ICommand? RemoveCommand { get; set; }

    public void SetLayout(double left, double top, double width, double height)
    {
        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class LiveGaugeChannelOption
{
    public LiveGaugeChannelOption(string label, int index)
    {
        Label = label;
        Index = index;
    }

    public string Label { get; }
    public int Index { get; }
}

public sealed class LiveGaugeDisplayModeOption
{
    public LiveGaugeDisplayModeOption(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }
}
