using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ScottPlot.WPF;
using Spider8DAQ.App.ViewModels;

namespace Spider8DAQ.App.Controls;

/// <summary>Hosts a WpfPlot for one live CWT panel and attaches it to <see cref="MainViewModel"/>.</summary>
public sealed class LiveCwtPlotHost : Border
{
    private LiveCwtPanelViewModel? _vm;
    private WpfPlot? _plot;

    public LiveCwtPlotHost()
    {
        BorderBrush = new SolidColorBrush(Color.FromRgb(197, 206, 214));
        BorderThickness = new Thickness(1);
        Background = Brushes.White;
        Padding = new Thickness(4);
        CornerRadius = new CornerRadius(4);
        MinHeight = 220;
        Margin = new Thickness(4);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        DataContextChanged += (_, _) =>
        {
            _vm = DataContext as LiveCwtPanelViewModel;
            TryAttach();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_plot is null)
        {
            _plot = new WpfPlot();
            Child = _plot;
        }
        _vm = DataContext as LiveCwtPanelViewModel;
        TryAttach();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (Window.GetWindow(this)?.DataContext is MainViewModel main)
            main.DetachLiveCwtPlot(_vm);
    }

    private void TryAttach()
    {
        if (_vm is null || _plot is null) return;
        if (Window.GetWindow(this)?.DataContext is not MainViewModel main) return;
        main.AttachLiveCwtPlot(_vm, _plot);
    }
}
