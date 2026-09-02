using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Spider8DAQ.App;

/// <summary>One-sentence notice + Actualizează. X / Esc skips this session.</summary>
public sealed class UpdateAvailableWindow : Window
{
    public bool ApplyClicked { get; private set; }

    public UpdateAvailableWindow(string versionText)
    {
        Title = "UPET AcqLab";
        Width = 420;
        Height = 168;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.Height;

        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 16) };

        var apply = new Button
        {
            Content = "Actualizează",
            Width = 140,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            Margin = new Thickness(0, 16, 0, 0)
        };
        DockPanel.SetDock(apply, Dock.Bottom);
        root.Children.Add(apply);

        root.Children.Add(new TextBlock
        {
            Text = "Este disponibilă versiunea " + versionText + ".",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x31))
        });
        Content = root;

        apply.Click += (_, _) =>
        {
            ApplyClicked = true;
            DialogResult = true;
            Close();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            ApplyClicked = false;
            DialogResult = false;
            Close();
        };
    }
}
