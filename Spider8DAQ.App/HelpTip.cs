using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Spider8DAQ.App;

/// <summary>Attached help text for Info mode (hover descriptions).</summary>
public static class HelpTip
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text",
            typeof(string),
            typeof(HelpTip),
            new PropertyMetadata(null, OnTextChanged));

    public static void SetText(DependencyObject element, string? value) => element.SetValue(TextProperty, value);
    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        el.MouseEnter -= OnEnter;
        el.MouseLeave -= OnLeave;
        if (e.NewValue is string s && !string.IsNullOrWhiteSpace(s))
        {
            el.MouseEnter += OnEnter;
            el.MouseLeave += OnLeave;
            ToolTipService.SetToolTip(el, s);
            ToolTipService.SetShowDuration(el, 20000);
        }
    }

    private static void OnEnter(object sender, MouseEventArgs e)
    {
        if (sender is not DependencyObject d) return;
        var text = GetText(d);
        if (string.IsNullOrWhiteSpace(text)) return;
        if (Window.GetWindow(d) is MainWindow { DataContext: ViewModels.MainViewModel vm } && vm.InfoMode)
            vm.HelpPanelText = text;
    }

    private static void OnLeave(object sender, MouseEventArgs e)
    {
        if (sender is not DependencyObject d) return;
        if (Window.GetWindow(d) is MainWindow { DataContext: ViewModels.MainViewModel vm } && vm.InfoMode)
        {
            // keep last tip visible until another control is hovered
        }
    }
}

public static class UiIcon
{
    public static TextBlock Glyph(string code, double size = 14) => new()
    {
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        Text = code,
        FontSize = size,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(0, 0, 6, 0)
    };
}
