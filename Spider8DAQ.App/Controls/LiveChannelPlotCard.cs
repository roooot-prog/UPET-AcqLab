using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using ScottPlot.WPF;
using Spider8DAQ.App.ViewModels;

namespace Spider8DAQ.App.Controls;

/// <summary>Movable/resizable card hosting one live Y(t) plot for a single channel.</summary>
public class LiveChannelPlotCard : MovableResizableCard
{
    private LiveChannelPlotItemViewModel? _vm;
    private bool _syncing;

    public LiveChannelPlotCard()
    {
        MinWidth = 280;
        MinHeight = 200;
        Loaded += OnLoaded;
        LayoutChanged += (_, _) => SyncToVm();
        DataContextChanged += (_, _) => AttachVm();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SyncFromVm();
        TryAttachPlot();
    }

    private void AttachVm()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm = DataContext as LiveChannelPlotItemViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += Vm_PropertyChanged;
        SyncFromVm();
        TryAttachPlot();
    }

    private void TryAttachPlot()
    {
        if (_vm is null) return;
        var plot = FindVisualChild<WpfPlot>(this);
        if (plot is null) return;
        if (Window.GetWindow(this)?.DataContext is not MainViewModel mainVm) return;
        mainVm.AttachChannelPlot(_vm, plot);
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null) return;
        switch (e.PropertyName)
        {
            case nameof(LiveChannelPlotItemViewModel.Title):
                Title = _vm.Title;
                break;
            case nameof(LiveChannelPlotItemViewModel.Left):
            case nameof(LiveChannelPlotItemViewModel.Top):
            case nameof(LiveChannelPlotItemViewModel.Width):
            case nameof(LiveChannelPlotItemViewModel.Height):
                SyncFromVm();
                break;
        }
    }

    private void SyncFromVm()
    {
        if (_vm is null || _syncing) return;
        _syncing = true;
        CardId = "chplot." + _vm.Id;
        Title = _vm.Title;
        ApplyRect(_vm.Left, _vm.Top, _vm.Width, _vm.Height);
        _syncing = false;
    }

    private void SyncToVm()
    {
        if (_vm is null || _syncing || IsMinimized) return;
        var r = GetRect();
        if (r.Width < 40 || r.Height < 40) return;
        _syncing = true;
        _vm.SetLayout(r.Left, r.Top, r.Width, r.Height);
        _syncing = false;
        if (Window.GetWindow(this)?.DataContext is MainViewModel mainVm)
            mainVm.OnChannelPlotLayoutChanged(_vm);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
