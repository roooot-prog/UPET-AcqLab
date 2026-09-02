using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Spider8DAQ.App.ViewModels;

/// <summary>One draggable Y(t) panel bound to a single DAQ channel.</summary>
public sealed class LiveChannelPlotItemViewModel : INotifyPropertyChanged
{
    private int _channelIndex;
    private double _left = 620;
    private double _top = 480;
    private double _width = 420;
    private double _height = 280;
    private bool _followLive = true;
    private string _channelName = "CH0";
    private string _unit = "";

    public LiveChannelPlotItemViewModel(string id)
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
        }
    }

    public string ChannelName
    {
        get => _channelName;
        set
        {
            if (_channelName == value) return;
            _channelName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
        }
    }

    public string Unit
    {
        get => _unit;
        set
        {
            if (_unit == value) return;
            _unit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Title));
        }
    }

    public string Title => string.IsNullOrWhiteSpace(Unit)
        ? $"Grafic — {ChannelName}"
        : $"Grafic — {ChannelName} [{Unit}]";

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
        set { _width = Math.Max(280, value); OnPropertyChanged(); }
    }

    public double Height
    {
        get => _height;
        set { _height = Math.Max(200, value); OnPropertyChanged(); }
    }

    public bool FollowLive
    {
        get => _followLive;
        set { _followLive = value; OnPropertyChanged(); }
    }

    public ICommand? AutoscaleCommand { get; set; }
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
