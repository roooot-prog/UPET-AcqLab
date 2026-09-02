using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Spider8DAQ.App.Controls;

/// <summary>Yes/No confirm with optional «Nu mai întreba» checkbox.</summary>
public sealed class SoftConfirmWindow : Window
{
    private readonly CheckBox _dontAsk;

    public bool Confirmed { get; private set; }
    public bool DontAskAgain => _dontAsk.IsChecked == true;

    public SoftConfirmWindow(string title, string message, string confirmLabel = "Continuați")
    {
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        MinWidth = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));

        var root = new DockPanel { Margin = new Thickness(16) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var ok = new Button
        {
            Content = confirmLabel,
            Padding = new Thickness(14, 8, 14, 8),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0xD4, 0xE5, 0xDA))
        };
        ok.Click += (_, _) =>
        {
            Confirmed = true;
            DialogResult = true;
            Close();
        };
        var cancel = new Button
        {
            Content = "Anulați",
            Padding = new Thickness(14, 8, 14, 8),
            IsCancel = true
        };
        cancel.Click += (_, _) =>
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 12)
        });
        _dontAsk = new CheckBox
        {
            Content = "Nu mai întreba",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        body.Children.Add(_dontAsk);
        root.Children.Add(body);
        Content = root;
    }
}
