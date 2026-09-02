using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spider8DAQ.App.ViewModels;

namespace Spider8DAQ.App.Controls;

/// <summary>Dialog for one live dial: channel, scale, force unit (N/kN), display mode.</summary>
public sealed class LiveGaugeSettingsWindow : Window
{
    private readonly LiveGaugeItemViewModel _gauge;
    private readonly MainViewModel? _main;
    private readonly ComboBox _channelBox;
    private readonly ComboBox _modeBox;
    private readonly ComboBox _forceUnitBox;
    private readonly CheckBox _autoScaleBox;
    private readonly TextBox _minBox;
    private readonly TextBox _maxBox;
    private readonly TextBlock _hint;

    public LiveGaugeSettingsWindow(LiveGaugeItemViewModel gauge, MainViewModel? main)
    {
        _gauge = gauge;
        _main = main;

        Title = "Setări cadran — UPET AcqLab";
        Width = 420;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(20) };
        for (var i = 0; i < 10; i++)
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = gauge.Title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6)),
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var sub = new TextBlock
        {
            Text = "Scară, canal și unitate forță (N / kN).",
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 0, 0, 14)
        };
        Grid.SetRow(sub, 1);
        root.Children.Add(sub);

        _channelBox = MakeLabeledCombo(root, 2, "Canal", out _);
        if (main is not null)
        {
            foreach (var opt in main.LiveGaugeChannelOptions)
                _channelBox.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt.Index });
        }
        else
        {
            _channelBox.Items.Add(new ComboBoxItem { Content = "Auto", Tag = -1 });
            for (var i = 0; i < 8; i++)
                _channelBox.Items.Add(new ComboBoxItem { Content = $"CH{i}", Tag = i });
        }
        SelectByTag(_channelBox, gauge.ChannelIndex);

        _modeBox = MakeLabeledCombo(root, 3, "Mod afișare", out _);
        RebuildModeItems(gauge.ChannelIndex);
        SelectByTag(_modeBox, gauge.DisplayMode);

        _forceUnitBox = MakeLabeledCombo(root, 4, "Unitate forță (primar)", out _);
        _forceUnitBox.Items.Add(new ComboBoxItem { Content = "Auto (unitate canal)", Tag = LiveGaugeItemViewModel.ForceUnitAuto });
        _forceUnitBox.Items.Add(new ComboBoxItem { Content = "N (newton)", Tag = LiveGaugeItemViewModel.ForceUnitN });
        _forceUnitBox.Items.Add(new ComboBoxItem { Content = "kN", Tag = LiveGaugeItemViewModel.ForceUnitKn });
        SelectByTag(_forceUnitBox, string.IsNullOrWhiteSpace(gauge.ForceUnit)
            ? LiveGaugeItemViewModel.ForceUnitAuto
            : gauge.ForceUnit);

        _channelBox.SelectionChanged += (_, _) =>
        {
            if (SelectedTag(_channelBox) is int idx)
                RebuildModeItems(idx);
        };

        _autoScaleBox = new CheckBox
        {
            Content = "Scară automată (±Capacity / 0–50 t)",
            IsChecked = gauge.AutoScale,
            Margin = new Thickness(0, 10, 0, 8),
            FontWeight = FontWeights.SemiBold
        };
        Grid.SetRow(_autoScaleBox, 5);
        root.Children.Add(_autoScaleBox);

        var scaleRow = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        var maxLabel = new TextBlock { Text = "Max", VerticalAlignment = VerticalAlignment.Center, Width = 36 };
        var minLabel = new TextBlock { Text = "Min", VerticalAlignment = VerticalAlignment.Center, Width = 36, Margin = new Thickness(12, 0, 0, 0) };
        _minBox = new TextBox
        {
            Width = 100,
            Padding = new Thickness(6, 4, 6, 4),
            Text = FormatNum(gauge.GaugeMin),
            Margin = new Thickness(0, 0, 8, 0)
        };
        _maxBox = new TextBox
        {
            Width = 100,
            Padding = new Thickness(6, 4, 6, 4),
            Text = FormatNum(gauge.GaugeMax)
        };
        DockPanel.SetDock(maxLabel, Dock.Left);
        DockPanel.SetDock(_maxBox, Dock.Left);
        DockPanel.SetDock(minLabel, Dock.Left);
        scaleRow.Children.Add(minLabel);
        scaleRow.Children.Add(_minBox);
        scaleRow.Children.Add(maxLabel);
        scaleRow.Children.Add(_maxBox);
        Grid.SetRow(scaleRow, 6);
        root.Children.Add(scaleRow);

        var presets = new WrapPanel { Margin = new Thickness(0, 4, 0, 8) };
        presets.Children.Add(MakePresetButton("±Capacity", () => ApplyCapacityPreset()));
        presets.Children.Add(MakePresetButton("0 … 5 kN", () => ApplyFixedScale(0, 5, LiveGaugeItemViewModel.ForceUnitKn)));
        presets.Children.Add(MakePresetButton("0 … 5000 N", () => ApplyFixedScale(0, 5000, LiveGaugeItemViewModel.ForceUnitN)));
        presets.Children.Add(MakePresetButton("0 … 50 t", () => ApplyFixedScale(0, 50_000, LiveGaugeItemViewModel.ForceUnitAuto)));
        Grid.SetRow(presets, 7);
        root.Children.Add(presets);

        _hint = new TextBlock
        {
            Text = "Debifați «Scară automată» pentru Min/Max manuale. Unitatea forță afectează cifra primară pe cadran.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 8)
        };
        Grid.SetRow(_hint, 8);
        root.Children.Add(_hint);

        void SyncManualEnabled()
        {
            var manual = _autoScaleBox.IsChecked != true;
            _minBox.IsEnabled = manual;
            _maxBox.IsEnabled = manual;
        }
        _autoScaleBox.Checked += (_, _) => SyncManualEnabled();
        _autoScaleBox.Unchecked += (_, _) => SyncManualEnabled();
        SyncManualEnabled();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var ok = new Button
        {
            Content = "OK",
            Width = 100,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        var cancel = new Button { Content = "Anulează", Width = 100, Height = 32, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (!TryApply())
                return;
            DialogResult = true;
            Close();
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 10);
        root.Children.Add(buttons);

        Content = root;
    }

    private bool TryApply()
    {
        if (SelectedTag(_channelBox) is int ch)
            _gauge.ChannelIndex = ch;
        if (SelectedTag(_modeBox) is string mode)
            _gauge.DisplayMode = mode;
        if (SelectedTag(_forceUnitBox) is string fu)
            _gauge.ForceUnit = fu;

        var auto = _autoScaleBox.IsChecked == true;
        _gauge.AutoScale = auto;
        if (!auto)
        {
            if (!TryParseNum(_minBox.Text, out var min) || !TryParseNum(_maxBox.Text, out var max))
            {
                _hint.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x10, 0x2E));
                _hint.Text = "Min/Max trebuie să fie numere valide.";
                return false;
            }
            if (max <= min)
            {
                _hint.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x10, 0x2E));
                _hint.Text = "Max trebuie să fie mai mare decât Min.";
                return false;
            }
            _gauge.GaugeMin = min;
            _gauge.GaugeMax = max;
        }

        _main?.NotifyLiveGaugeSettingsApplied(_gauge);
        return true;
    }

    private void ApplyCapacityPreset()
    {
        _autoScaleBox.IsChecked = true;
        _main?.ResetLiveGaugeScaleFromSensor(_gauge);
        _minBox.Text = FormatNum(_gauge.GaugeMin);
        _maxBox.Text = FormatNum(_gauge.GaugeMax);
    }

    private void ApplyFixedScale(double min, double max, string forceUnit)
    {
        _autoScaleBox.IsChecked = false;
        SelectByTag(_forceUnitBox, forceUnit);
        _minBox.Text = FormatNum(min);
        _maxBox.Text = FormatNum(max);
        _minBox.IsEnabled = true;
        _maxBox.IsEnabled = true;
    }

    private static ComboBox MakeLabeledCombo(Grid root, int row, string label, out TextBlock labelBlock)
    {
        labelBlock = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        };
        var box = new ComboBox { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(4) };
        var panel = new StackPanel();
        panel.Children.Add(labelBlock);
        panel.Children.Add(box);
        Grid.SetRow(panel, row);
        root.Children.Add(panel);
        return box;
    }

    private static Button MakePresetButton(string text, Action action)
    {
        var b = new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, 6, 4),
            Padding = new Thickness(8, 3, 8, 3),
            FontSize = 11
        };
        b.Click += (_, _) => action();
        return b;
    }

    private void RebuildModeItems(int channelIndex)
    {
        var prev = SelectedTag(_modeBox) as string ?? _gauge.DisplayMode;
        _modeBox.Items.Clear();
        ChannelRow? ch = null;
        if (_main is not null)
        {
            if (channelIndex >= 0 && channelIndex < _main.Channels.Count)
                ch = _main.Channels[channelIndex];
            else if (channelIndex < 0)
                ch = _main.Channels.FirstOrDefault(c => c.Enabled);
        }
        foreach (var opt in MainViewModel.GetDisplayModesForChannel(ch))
            _modeBox.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt.Id });
        SelectByTag(_modeBox, prev);
    }

    private static void SelectByTag(ComboBox box, object? tag)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if (Equals(item.Tag, tag) ||
                (item.Tag is string s && tag is string t &&
                 string.Equals(s, t, StringComparison.OrdinalIgnoreCase)))
            {
                box.SelectedItem = item;
                return;
            }
        }
        if (box.Items.Count > 0)
            box.SelectedIndex = 0;
    }

    private static object? SelectedTag(ComboBox box) =>
        (box.SelectedItem as ComboBoxItem)?.Tag;

    private static string FormatNum(double v) =>
        v.ToString("0.####", CultureInfo.InvariantCulture);

    private static bool TryParseNum(string? text, out double value) =>
        double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);
}
