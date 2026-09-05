using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Spider8DAQ.App;

/// <summary>Non-modal notice while GitHub zip downloads and the helper restarts AcqLab.</summary>
public sealed class UpdatingWindow : Window
{
    public UpdatingWindow(string text)
    {
        Title = "UPET AcqLab";
        Width = 440;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;

        Content = new TextBlock
        {
            Text = text,
            Margin = new Thickness(24, 22, 24, 22),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x31))
        };
    }

    public static UpdatingWindow? TryShow(Window? owner, string text)
    {
        try
        {
            var w = new UpdatingWindow(text);
            if (owner is not null)
                w.Owner = owner;
            w.Show();
            return w;
        }
        catch
        {
            return null;
        }
    }
}
