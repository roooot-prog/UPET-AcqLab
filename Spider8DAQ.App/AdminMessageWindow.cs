using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Spider8DAQ.App;

/// <summary>
/// Overlay in the center of the AcqLab window. Modeless (Show, not ShowDialog) so OK/X always work
/// and the heartbeat thread is not nested inside a dispatcher frame.
/// </summary>
public sealed class AdminMessageWindow : Window
{
    public const string OverlayTitle = "Mesaj Mentenanță ADMIN";
    private static AdminMessageWindow? _open;
    private readonly string _text;

    public AdminMessageWindow(string text)
    {
        _text = text ?? "";
        Title = OverlayTitle;
        Width = 560;
        MinHeight = 220;
        MaxWidth = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = true;
        Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF8));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6));
        BorderThickness = new Thickness(2);

        var root = new DockPanel();

        var header = new DockPanel
        {
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6)),
            LastChildFill = true
        };
        var closeX = new Button
        {
            Content = "✕",
            Width = 44,
            Height = 44,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            ToolTip = "Închide mesajul"
        };
        closeX.Click += (_, _) => Dismiss();
        DockPanel.SetDock(closeX, Dock.Right);
        header.Children.Add(closeX);
        header.Children.Add(new TextBlock
        {
            Text = OverlayTitle,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(22, 16, 8, 16)
        });
        var headerBorder = new Border { Child = header };
        DockPanel.SetDock(headerBorder, Dock.Top);
        root.Children.Add(headerBorder);

        var ok = new Button
        {
            Content = "OK",
            Width = 120,
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true,
            IsCancel = true,
            Margin = new Thickness(22, 0, 22, 18),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        };
        ok.Click += (_, _) => Dismiss();
        DockPanel.SetDock(ok, Dock.Bottom);
        root.Children.Add(ok);

        root.Children.Add(new TextBlock
        {
            Text = _text,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(22, 18, 22, 16),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x31))
        });
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape && e.Key != Key.Enter && e.Key != Key.Return)
                return;
            e.Handled = true;
            Dismiss();
        };

        Loaded += (_, _) => BringInFront(ok);
        ContentRendered += (_, _) => BringInFront(ok);
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, this))
                _open = null;
        };
    }

    private void Dismiss()
    {
        try { Close(); }
        catch { /* already closing */ }
    }

    private void BringInFront(Button ok)
    {
        Topmost = false;
        Topmost = true;
        Activate();
        ok.Focus();
    }

    /// <summary>Must run on the WPF UI thread. Centers on AcqLab even if that window is not focused.</summary>
    public static void ShowOnUi(string text, Window? owner)
    {
        var body = (text ?? "").Trim();
        if (body.Length == 0) return;

        if (_open is not null)
        {
            if (string.Equals(_open._text, body, StringComparison.Ordinal))
            {
                _open.Activate();
                _open.Topmost = false;
                _open.Topmost = true;
                return;
            }

            try { _open.Close(); } catch { /* replace */ }
            _open = null;
        }

        var w = new AdminMessageWindow(body);
        if (owner is not null)
        {
            if (owner.WindowState == WindowState.Minimized)
                owner.WindowState = WindowState.Normal;
            try { owner.Activate(); } catch { /* still show overlay */ }
            w.Owner = owner;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _open = w;
        w.Show();
        w.Activate();
    }
}
