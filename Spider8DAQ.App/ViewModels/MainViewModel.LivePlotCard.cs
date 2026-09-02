using System.Windows.Input;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    private bool _showLivePlotCard;

    /// <summary>Main Y(t)/Dual/FFT Grafic live card on the measure workspace.</summary>
    public bool ShowLivePlotCard
    {
        get => _showLivePlotCard;
        set
        {
            if (_showLivePlotCard == value) return;
            _showLivePlotCard = value;
            OnPropertyChanged();
            LivePlotCardVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ICommand ToggleLivePlotCardCommand { get; private set; } = null!;
    public ICommand HideLivePlotCardCommand { get; private set; } = null!;

    /// <summary>Raised when the plot card should be brought to front (already visible).</summary>
    public event EventHandler? RequestFocusLivePlotCard;

    /// <summary>Raised when ShowLivePlotCard changes — persist layout.</summary>
    internal event EventHandler? LivePlotCardVisibilityChanged;

    internal void WireLivePlotCardCommands()
    {
        ToggleLivePlotCardCommand = new RelayCommand(ToggleLivePlotCard);
        HideLivePlotCardCommand = new RelayCommand(HideLivePlotCard);
    }

    private void ToggleLivePlotCard()
    {
        if (ShowLivePlotCard)
        {
            RequestFocusLivePlotCard?.Invoke(this, EventArgs.Empty);
            Status = "Grafic live — adus în față.";
            return;
        }

        ShowLivePlotCard = true;
        RequestFocusLivePlotCard?.Invoke(this, EventArgs.Empty);
        Status = "Grafic live deschis — ribbon VIZUALIZARE → Grafic live sau ✕ pentru ascundere.";
    }

    private void HideLivePlotCard()
    {
        if (!ShowLivePlotCard) return;
        ShowLivePlotCard = false;
        Status = "Grafic live ascuns — ribbon VIZUALIZARE → Grafic live.";
    }
}
