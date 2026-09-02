using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Spider8DAQ.App.Controls;

namespace Spider8DAQ.App.ViewModels;

/// <summary>One live CWT scalogram panel bound to an enabled DAQ channel.</summary>
public sealed class LiveCwtPanelViewModel : INotifyPropertyChanged
{
    private int _channelIndex;
    private string _channelName = "CH0";
    private string _unit = "";
    private string _subtitle = "Morlet · live";

    public LiveCwtPanelViewModel(string id) => Id = id;

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
            OnPropertyChanged(nameof(AccentBrush));
        }
    }

    /// <summary>Same stable channel color as Y(t) / grid.</summary>
    public Brush AccentBrush => ChannelPalette.GetWpfBrush(ChannelIndex);

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

    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value; OnPropertyChanged(); }
    }

    public string Title => string.IsNullOrWhiteSpace(Unit)
        ? $"CWT — {ChannelName}"
        : $"CWT — {ChannelName} [{Unit}]";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
