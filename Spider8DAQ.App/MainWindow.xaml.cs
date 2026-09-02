using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Spider8DAQ.App.Controls;
using Spider8DAQ.App.ViewModels;

namespace Spider8DAQ.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private WindowState _restoreState = WindowState.Normal;
    private bool _prevFullscreenPanel;
    private DispatcherTimer? _layoutSaveTimer;
    private bool _layoutLoaded;

    public MainWindow(string? openPath = null)
    {
        // VM in ctor body so init failures don't become fatal XamlParseException.
        try
        {
            _vm = new MainViewModel();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Nu s-a putut inițializa UPET AcqLab:\n\n" + ex.Message,
                "UPET AcqLab",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _vm = MainViewModel.CreateSafeFallback();
        }

        InitializeComponent();
        TrySetWindowIcon();
        DataContext = _vm;
        if (Resources["VmProxy"] is BindingProxy proxy)
            proxy.Data = _vm;
        Loaded += (_, _) =>
        {
            try { _vm.EnsureStartupAssets(); }
            catch { /* non-fatal */ }
            try { _vm.EnsureSensorsLoadedForUi(); }
            catch { /* non-fatal */ }
            try
            {
                _vm.AttachPlots(PlotA, PlotB, PlotAnalysis);
                if (PlotDataViewer is not null)
                    _vm.AttachDataViewerPlot(PlotDataViewer);
                if (PlotDefectExample is not null)
                    _vm.AttachDefectPlot(PlotDefectExample);
                WireMovableCards();
                LoadCardLayouts();
                ApplyUiLayout();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Inițializare UI: " + ex.Message, "UPET AcqLab",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            if (!string.IsNullOrWhiteSpace(openPath))
            {
                try { _vm.TryOpenUpetReportPath(openPath); }
                catch { /* non-fatal */ }
            }

            var smokeUi = Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, "--smoke-ui", StringComparison.OrdinalIgnoreCase));
            if (!smokeUi)
            {
                try { _vm.MaybeCheckUpdatesOnStartup(); }
                catch { /* never block startup */ }
            }

            Dispatcher.BeginInvoke(FitChannelAutoColumns, DispatcherPriority.Loaded);
        };
        Closed += async (_, _) =>
        {
            SaveCardLayouts();
            await _vm.DisposeAsync();
        };
        PreviewKeyDown += (_, e) => _vm.HandleGlobalKey(e.Key, Keyboard.Modifiers);
        _vm.UiLayoutChanged += (_, _) => Dispatcher.Invoke(() =>
        {
            SyncFullscreenWindowState();
            ApplyUiLayout();
        });
        _vm.RequestFocusLivePlotCard += (_, _) => Dispatcher.Invoke(FocusLivePlotCard);
        _vm.LivePlotCardVisibilityChanged += (_, _) => Dispatcher.Invoke(ScheduleSaveCardLayouts);
        _vm.RequestAnalysisTab += (_, _) => Dispatcher.Invoke(() => SelectTab(AnalysisTabItem));
        _vm.RequestSensorLibraryTab += (_, _) => Dispatcher.Invoke(() => SelectTab(SensorLibraryTabItem));
        _vm.RequestMeasureTab += (_, _) => Dispatcher.Invoke(() => SelectTab(MeasureTabItem));
        _vm.RequestDataViewerTab += (_, _) => Dispatcher.Invoke(() => SelectTab(DataViewerTabItem));
        _vm.RequestJobTab += (_, _) => Dispatcher.Invoke(() => SelectTab(JobTabItem));
        _vm.RequestHealthTab += (_, _) => Dispatcher.Invoke(() => SelectTab(HealthTabItem));
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void NavigateSecondaryTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string name }) return;
        var tab = FindName(name) as TabItem;
        SelectTab(tab);
    }

    private void SelectTab(TabItem? tab)
    {
        if (tab is null) return;
        if (IsExpertOnlyTab(tab) && !_vm.IsExpertMode)
            _vm.IsExpertMode = true;
        if (LogicalTreeHelper.GetParent(tab) is TabControl inner
            && LogicalTreeHelper.GetParent(inner) is TabItem outer)
            outer.IsSelected = true;
        tab.IsSelected = true;
    }

    private static bool IsExpertOnlyTab(TabItem tab) =>
        tab.Name is "JobTabItem" or "DevicesTabItem" or "MacrosTabItem"
            or "IntegrationsTabItem" or "AlarmsTabItem" or "TemplatesTabItem"
            or "HealthTabItem" or "SyncTabItem";

    private bool IsExpertOnlyTabSelected() =>
        JobTabItem?.IsSelected == true
        || DevicesTabItem?.IsSelected == true
        || MacrosTabItem?.IsSelected == true
        || IntegrationsTabItem?.IsSelected == true
        || AlarmsTabItem?.IsSelected == true
        || TemplatesTabItem?.IsSelected == true
        || HealthTabItem?.IsSelected == true
        || SyncTabItem?.IsSelected == true;

    private void HideSecondaryTabHeadersExcept(TabItem? keep)
    {
        // Operator hides expert peers via IsExpertMode bindings (3.3.85+). Contrast punch 3.3.87.
    }

    private IEnumerable<MovableResizableCard> AllMovableCards()
    {
        foreach (var c in new MovableResizableCard?[]
                 {
                     CardChannels, CardSensors, CardTools, CardWorkflow, CardPlot
                 })
        {
            if (c is not null) yield return c;
        }
    }

    private void WireMovableCards()
    {
        foreach (var card in AllMovableCards())
        {
            card.LayoutChanged += (_, _) => ScheduleSaveCardLayouts();
            card.MinimizedChanged += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                ApplyCardVisibilityOnly();
                RefreshMinimizedDock();
            }, DispatcherPriority.ApplicationIdle);
        }

        // Carduri dinamice (grafice canal / cadrane) — re-scan la schimbări colecție
        _vm.ChannelPlots.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(WireDynamicCardsAndRefreshDock);
        _vm.LiveGauges.CollectionChanged += (_, _) => Dispatcher.BeginInvoke(WireDynamicCardsAndRefreshDock);
        Dispatcher.BeginInvoke(WireDynamicCardsAndRefreshDock, DispatcherPriority.Loaded);
    }

    private readonly HashSet<MovableResizableCard> _wiredDynamicCards = new();

    private void WireDynamicCardsAndRefreshDock()
    {
        if (MeasureWorkspaceCanvas is null) return;
        foreach (var card in FindVisualChildren<MovableResizableCard>(MeasureWorkspaceCanvas))
        {
            if (_wiredDynamicCards.Contains(card)) continue;
            if (AllMovableCards().Contains(card)) continue;
            card.LayoutChanged += (_, _) => ScheduleSaveCardLayouts();
            card.MinimizedChanged += (_, _) => Dispatcher.BeginInvoke(RefreshMinimizedDock, DispatcherPriority.ApplicationIdle);
            _wiredDynamicCards.Add(card);
        }
        RefreshMinimizedDock();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is null) yield break;
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;
            foreach (var nested in FindVisualChildren<T>(child))
                yield return nested;
        }
    }

    private IEnumerable<MovableResizableCard> EnumerateWorkspaceCards()
    {
        foreach (var c in AllMovableCards())
            yield return c;
        if (MeasureWorkspaceCanvas is null) yield break;
        foreach (var c in FindVisualChildren<MovableResizableCard>(MeasureWorkspaceCanvas))
        {
            if (!AllMovableCards().Contains(c))
                yield return c;
        }
    }

    private void RefreshMinimizedDock()
    {
        if (MinimizedCardsBar is null || MinimizedCardsDock is null) return;

        MinimizedCardsBar.Items.Clear();
        foreach (var card in EnumerateWorkspaceCards())
        {
            if (!card.IsMinimized) continue;
            var title = string.IsNullOrWhiteSpace(card.Title) ? "Panou" : card.Title;
            var btn = new Button
            {
                Content = title,
                Tag = card,
                Margin = new Thickness(0, 0, 4, 0),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = "Click = restaurează panoul pe spațiul de lucru"
            };
            if (TryFindResource("MinimizedChipButton") is Style chipStyle)
                btn.Style = chipStyle;
            else
            {
                btn.Foreground = Brushes.Black;
                btn.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xCD));
                btn.BorderBrush = new SolidColorBrush(Color.FromRgb(0x1F, 0x6A, 0xB2));
                btn.BorderThickness = new Thickness(1.5);
            }
            btn.Click += RestoreMinimizedCard_Click;
            MinimizedCardsBar.Items.Add(btn);
        }

        MinimizedCardsDock.Visibility = MinimizedCardsBar.Items.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RestoreMinimizedCard_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: MovableResizableCard card }) return;

        // Restaurează doar cardul — fără ApplyUiLayout (nu atinge WindowState).
        card.IsMinimized = false;
        card.Visibility = Visibility.Visible;
        card.BringCardToFront();
        ApplyCardVisibilityOnly();

        // Amână actualizarea barei: dacă o ascundem în același click, MouseUp poate
        // „cădea” pe taskbar-ul Windows și minimizează aplicația.
        Dispatcher.BeginInvoke(() =>
        {
            RefreshMinimizedDock();
            ScheduleSaveCardLayouts();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void LoadCardLayouts()
    {
        var doc = UiLayoutStore.Load();
        var defaults = UiLayoutStore.DefaultLayouts();
        foreach (var card in AllMovableCards())
        {
            var id = card.CardId;
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (doc.Cards.TryGetValue(id, out var entry) || defaults.TryGetValue(id, out entry))
            {
                card.ApplyRect(entry.Left, entry.Top, entry.Width, entry.Height);
                if (entry.Minimized == true)
                    card.IsMinimized = true;
                if (string.Equals(id, "measure.plot", StringComparison.OrdinalIgnoreCase))
                {
                    var visible = entry.Visible ?? defaults.GetValueOrDefault(id)?.Visible ?? false;
                    _vm.ShowLivePlotCard = visible;
                }
            }
        }
        _layoutLoaded = true;
        RefreshMinimizedDock();
    }

    private void SaveCardLayouts()
    {
        if (!_layoutLoaded) return;
        var doc = new UiLayoutDocument { Version = UiLayoutStore.CurrentVersion };
        foreach (var card in AllMovableCards())
        {
            var id = card.CardId;
            if (string.IsNullOrWhiteSpace(id)) continue;
            var r = card.GetRect();
            doc.Cards[id] = new CardLayoutEntry
            {
                Left = r.Left,
                Top = r.Top,
                Width = r.Width,
                Height = r.Height,
                Minimized = card.IsMinimized,
                Visible = string.Equals(id, "measure.plot", StringComparison.OrdinalIgnoreCase)
                    ? _vm.ShowLivePlotCard
                    : null
            };
        }
        UiLayoutStore.Save(doc);
    }

    private void ScheduleSaveCardLayouts()
    {
        _layoutSaveTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _layoutSaveTimer.Stop();
        _layoutSaveTimer.Tick -= LayoutSaveTimer_Tick;
        _layoutSaveTimer.Tick += LayoutSaveTimer_Tick;
        _layoutSaveTimer.Start();
    }

    private void LayoutSaveTimer_Tick(object? sender, EventArgs e)
    {
        _layoutSaveTimer?.Stop();
        SaveCardLayouts();
    }

    private void ResetLayout_Click(object sender, RoutedEventArgs e)
    {
        UiLayoutStore.Clear();
        var defaults = UiLayoutStore.DefaultLayouts();
        foreach (var card in AllMovableCards())
        {
            var id = card.CardId;
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (!defaults.TryGetValue(id, out var entry)) continue;
            card.ApplyRect(entry.Left, entry.Top, entry.Width, entry.Height);
            card.IsMinimized = false;
        }
        SaveCardLayouts();
        _vm.ShowLivePlotCard = defaults.GetValueOrDefault("measure.plot")?.Visible ?? false;
        RefreshMinimizedDock();
        _vm.Status = "Layout resetat — poziții implicite ale cardurilor din tab Măsurare.";
    }

    private void WorkspaceCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep canvas large enough so cards below the fold remain reachable via scroll of parent if needed.
        if (sender is not Canvas canvas) return;
        double maxBottom = 0;
        foreach (UIElement child in canvas.Children)
        {
            if (child is not FrameworkElement fe) continue;
            var top = Canvas.GetTop(fe);
            if (double.IsNaN(top)) top = 0;
            var h = fe.ActualHeight > 0 ? fe.ActualHeight : (double.IsNaN(fe.Height) ? 0 : fe.Height);
            maxBottom = Math.Max(maxBottom, top + h);
        }
        if (maxBottom > canvas.MinHeight)
            canvas.Height = Math.Max(e.NewSize.Height, maxBottom + 24);
    }

    private void TrySetWindowIcon()
    {
        // Must NOT be set in XAML: a missing Content/pack icon throws XamlParseException and blocks startup.
        try
        {
            var pack = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(pack);
            return;
        }
        catch { /* fall through */ }

        foreach (var rel in new[] { "app.ico", System.IO.Path.Combine("Assets", "app.ico") })
        {
            try
            {
                var loose = System.IO.Path.Combine(AppContext.BaseDirectory, rel);
                if (System.IO.File.Exists(loose))
                {
                    Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(loose, UriKind.Absolute));
                    return;
                }
            }
            catch { /* try next */ }
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ShowPlots) or nameof(MainViewModel.ShowPlotB)
            or nameof(MainViewModel.LeftPanelVisible) or nameof(MainViewModel.PlotMode)
            or nameof(MainViewModel.ShowNumericPanel) or nameof(MainViewModel.ShowLivePlotCard)
            or nameof(MainViewModel.ShowLiveCwt) or nameof(MainViewModel.IsExpertMode))
        {
            Dispatcher.Invoke(() =>
            {
                ApplyUiLayout();
                if (e.PropertyName == nameof(MainViewModel.IsExpertMode)
                    && !_vm.IsExpertMode && IsExpertOnlyTabSelected())
                    SelectTab(MeasureTabItem);
            });
        }
    }

    private void ApplyCardVisibilityOnly()
    {
        var leftVisible = _vm.LeftPanelVisible;
        if (CardChannels is not null)
            CardChannels.Visibility = leftVisible && !CardChannels.IsMinimized ? Visibility.Visible : Visibility.Collapsed;
        if (CardSensors is not null)
            CardSensors.Visibility = leftVisible && !CardSensors.IsMinimized && _vm.IsExpertMode
                ? Visibility.Visible : Visibility.Collapsed;
        if (CardTools is not null)
            CardTools.Visibility = leftVisible && !CardTools.IsMinimized && _vm.IsExpertMode
                ? Visibility.Visible : Visibility.Collapsed;
        if (CardWorkflow is not null)
            CardWorkflow.Visibility = CardWorkflow.IsMinimized ? Visibility.Collapsed : Visibility.Visible;
        if (CardPlot is not null)
            CardPlot.Visibility = _vm.ShowLivePlotCard && !CardPlot.IsMinimized ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyUiLayout()
    {
        ApplyCardVisibilityOnly();

        if (NumericHost is not null)
        {
            NumericHost.Visibility = _vm.ShowNumericPanel && _vm.ShowLivePlotCard && !_vm.ShowLiveCwt
                ? Visibility.Visible : Visibility.Collapsed;
            if (_vm.ShowNumericPanel && _vm.ShowPlots && _vm.ShowLivePlotCard)
            {
                NumericHost.VerticalAlignment = VerticalAlignment.Bottom;
                NumericHost.Height = 168;
                NumericHost.Opacity = 0.96;
                Grid.SetRow(NumericHost, 1);
                Grid.SetRowSpan(NumericHost, 3);
            }
            else
            {
                NumericHost.VerticalAlignment = VerticalAlignment.Stretch;
                NumericHost.Height = double.NaN;
                NumericHost.Opacity = 1;
                Grid.SetRow(NumericHost, 1);
                Grid.SetRowSpan(NumericHost, 3);
            }
        }

        if (PlotHostA is not null)
            PlotHostA.Visibility = _vm.ShowPlots && _vm.ShowLivePlotCard && !_vm.ShowLiveCwt
                ? Visibility.Visible : Visibility.Collapsed;
        if (PlotHostB is not null)
        {
            PlotHostB.Visibility = _vm.ShowPlots && _vm.ShowPlotB && _vm.ShowLivePlotCard && !_vm.ShowLiveCwt
                ? Visibility.Visible : Visibility.Collapsed;
            if (PlotRowB is not null)
                PlotRowB.Height = _vm.ShowPlots && _vm.ShowPlotB && _vm.ShowLivePlotCard && !_vm.ShowLiveCwt
                    ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            if (PlotSplitterRow is not null)
                PlotSplitterRow.Height = _vm.ShowPlots && _vm.ShowPlotB && _vm.ShowLivePlotCard && !_vm.ShowLiveCwt
                    ? new GridLength(8) : new GridLength(0);
        }

        if (CwtHost is not null)
            CwtHost.Visibility = _vm.ShowLiveCwt && _vm.ShowLivePlotCard ? Visibility.Visible : Visibility.Collapsed;

        // WindowState NU se modifică aici — vezi SyncFullscreenWindowState (doar la FullscreenPanel).
        RefreshMinimizedDock();
    }

    private void SyncFullscreenWindowState()
    {
        if (_vm.FullscreenPanel == _prevFullscreenPanel) return;

        if (_vm.FullscreenPanel)
        {
            if (WindowState != WindowState.Maximized)
            {
                // Nu memora Minimized — altfel la ieșire din fullscreen app-ul ar pleca în taskbar.
                _restoreState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            }
            if (WindowState != WindowState.Maximized)
                WindowState = WindowState.Maximized;
        }
        else
        {
            if (_restoreState is WindowState.Normal or WindowState.Maximized)
                WindowState = _restoreState;
        }

        _prevFullscreenPanel = _vm.FullscreenPanel;
    }

    private void FocusLivePlotCard()
    {
        if (CardPlot is null || !_vm.ShowLivePlotCard) return;
        CardPlot.BringCardToFront();
    }

    private void SensorTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is SensorListRow row)
            _vm.SelectedSensorRow = row;
    }

    private void SensorTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item is null) return;
        item.IsSelected = true;
        item.Focus();
        if (item.DataContext is SensorListRow row)
            _vm.SelectedSensorRow = row;
    }

    private void SensorGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is SensorListRow sensor)
        {
            row.IsSelected = true;
            _vm.SelectedSensorRow = sensor;
        }
    }

    private bool _channelColsHooked;
    private bool _fittingChannelColumns;

    private void ChannelGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is ChannelRow ch)
        {
            row.IsSelected = true;
            _vm.SelectedChannelRow = ch;
        }
    }

    private void ChannelsGrid_Loaded(object sender, RoutedEventArgs e)
    {
        HookChannelCollectionForColumnFit();
        Dispatcher.BeginInvoke(FitChannelAutoColumns, DispatcherPriority.Loaded);
    }

    private void ChannelsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || _fittingChannelColumns) return;
        Dispatcher.BeginInvoke(FitChannelAutoColumns, DispatcherPriority.Loaded);
    }

    private void HookChannelCollectionForColumnFit()
    {
        if (_channelColsHooked) return;
        if (_vm.Channels is not INotifyCollectionChanged ncc) return;
        ncc.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(FitChannelAutoColumns, DispatcherPriority.Background);
        _channelColsHooked = true;
    }

    /// <summary>
    /// Re-apply star weights so every column grows/shrinks with the card (WPF otherwise
    /// freezes ActualWidth). Citire 1.4*, Senzor 2*; others *. No horizontal scrollbar.
    /// </summary>
    private static readonly (string Header, double Star, double Min)[] ChannelStarLayout =
    [
        ("On", 1, 40),
        ("LED", 1, 32),
        ("Nume", 1, 72),
        ("Citire", 1.4, 100),
        ("Unit", 1, 56),
        ("Semnal", 1, 72),
        ("Punte", 1, 64),
        ("Senzor", 2, 120),
    ];

    private void FitChannelAutoColumns()
    {
        if (ChannelsGrid is null || _fittingChannelColumns) return;
        _fittingChannelColumns = true;
        try
        {
            ApplyChannelStarColumns();
            ChannelsGrid.UpdateLayout();
            CompactChannelGridFontIfNeeded();
        }
        finally
        {
            _fittingChannelColumns = false;
        }
    }

    private void ApplyChannelStarColumns()
    {
        if (ChannelsGrid is null) return;
        foreach (var col in ChannelsGrid.Columns)
        {
            var header = col.Header?.ToString() ?? "";
            foreach (var spec in ChannelStarLayout)
            {
                if (!string.Equals(spec.Header, header, StringComparison.Ordinal)) continue;
                col.MinWidth = spec.Min;
                col.Width = new DataGridLength(spec.Star, DataGridLengthUnitType.Star);
                break;
            }
        }
    }

    private void CompactChannelGridFontIfNeeded()
    {
        if (ChannelsGrid is null) return;
        var avail = ChannelsGrid.ActualWidth;
        if (avail <= 0) return;

        var minSum = 0.0;
        foreach (var col in ChannelsGrid.Columns)
            minSum += col.MinWidth;

        var overflow = minSum > avail + 1;
        var target = overflow ? 11.0 : 12.0;
        if (Math.Abs(ChannelsGrid.FontSize - target) > 0.05)
            ChannelsGrid.FontSize = target;
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SensorLibraryTabItem?.IsSelected == true)
            _vm.EnsureSensorsLoadedForUi();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void ChannelsGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Column is DataGridTextColumn col)
        {
            var header = col.Header?.ToString() ?? "";
            if (header.Contains("Scale", StringComparison.OrdinalIgnoreCase)
                || header.Equals("S", StringComparison.OrdinalIgnoreCase))
                _vm.PushSelectedChannelUndo();
        }
    }

    private void ExpertScale_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _vm.PushSelectedChannelUndo();
    }
}
