using System.ComponentModel;
using System.Windows;
using Spider8DAQ.App.ViewModels;

namespace Spider8DAQ.App.Controls;

/// <summary>Movable/resizable card hosting one live gauge; syncs layout with <see cref="LiveGaugeItemViewModel"/>.</summary>
public class LiveGaugeCard : MovableResizableCard
{
    private LiveGaugeItemViewModel? _vm;
    private bool _syncing;

    public LiveGaugeCard()
    {
        MinWidth = 200;
        MinHeight = 280;
        Loaded += (_, _) => SyncFromVm();
        LayoutChanged += (_, _) => SyncToVm();
        DataContextChanged += (_, _) => AttachVm();
        AddHandler(RadialGaugeControl.DialActivatedEvent, new RoutedEventHandler(OnDialActivated));
    }

    private void OnDialActivated(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_vm is null) return;

        var owner = Window.GetWindow(this);
        if (owner?.DataContext is MainViewModel mainVm)
            mainVm.SelectedLiveGauge = _vm;

        try
        {
            var main = owner?.DataContext as MainViewModel;
            var dlg = new LiveGaugeSettingsWindow(_vm, main)
            {
                Owner = owner
            };
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                owner,
                "Nu s-au putut deschide setările cadranului:\n" + ex.Message,
                "UPET AcqLab",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void AttachVm()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= Vm_PropertyChanged;
        _vm = DataContext as LiveGaugeItemViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += Vm_PropertyChanged;
        SyncFromVm();
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null) return;
        switch (e.PropertyName)
        {
            case nameof(LiveGaugeItemViewModel.Title):
                Title = _vm.Title;
                break;
            case nameof(LiveGaugeItemViewModel.Left):
            case nameof(LiveGaugeItemViewModel.Top):
            case nameof(LiveGaugeItemViewModel.Width):
            case nameof(LiveGaugeItemViewModel.Height):
                SyncFromVm();
                break;
        }
    }
    private void SyncFromVm()
    {
        if (_vm is null || _syncing) return;
        _syncing = true;
        CardId = "gauge." + _vm.Id;
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
            mainVm.OnLiveGaugeLayoutChanged(_vm);
    }
}
