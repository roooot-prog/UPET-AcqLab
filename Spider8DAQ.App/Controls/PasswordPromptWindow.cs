using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Spider8DAQ.App.Controls;

/// <summary>Password prompt for opening .upetlab packages.</summary>
public sealed class PasswordPromptWindow : Window
{
    private readonly PasswordBox _password;

    public bool Confirmed { get; private set; }
    public string Password => _password.Password ?? "";

    public PasswordPromptWindow(string title, string message)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));

        var root = new DockPanel { Margin = new Thickness(16) };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button
        {
            Content = "Deschide",
            Padding = new Thickness(14, 8, 14, 8),
            FontWeight = FontWeights.SemiBold,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0xD4, 0xE5, 0xDA))
        };
        ok.Click += (_, _) =>
        {
            Confirmed = true;
            DialogResult = true;
            Close();
        };
        var cancel = new Button { Content = "Anulați", Padding = new Thickness(14, 8, 14, 8), IsCancel = true };
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
            Margin = new Thickness(0, 0, 0, 10)
        });
        _password = new PasswordBox { Padding = new Thickness(6, 4, 6, 4) };
        body.Children.Add(_password);
        root.Children.Add(body);
        Content = root;
        Loaded += (_, _) => _password.Focus();
    }
}
