using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spider8DAQ.Core.Updates;

namespace Spider8DAQ.App;

/// <summary>Centered progress while the GitHub zip downloads; AcqLab then restarts itself.</summary>
public sealed class UpdatingWindow : Window
{
    private readonly TextBlock _stage;
    private readonly TextBlock _detail;
    private readonly TextBlock _bytes;
    private readonly ProgressBar _bar;
    private readonly TextBlock _hint;

    public UpdatingWindow(string fromVersion, string toVersion)
    {
        Title = "Actualizare UPET AcqLab";
        Width = 520;
        MinHeight = 220;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.None;
        Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF0));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x6E, 0x77, 0x80));
        BorderThickness = new Thickness(1);
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;

        var ink = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        var muted = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
        var accent = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6));

        _stage = new TextBlock
        {
            Text = "Se pregătește actualizarea…",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = ink,
            TextWrapping = TextWrapping.Wrap
        };
        _detail = new TextBlock
        {
            Text = "Versiunea " + fromVersion + " → " + toVersion,
            FontSize = 13,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = muted,
            TextWrapping = TextWrapping.Wrap
        };
        _bar = new ProgressBar
        {
            Height = 18,
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true,
            Margin = new Thickness(0, 16, 0, 0)
        };
        _bytes = new TextBlock
        {
            Text = "",
            FontSize = 12,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(0, 8, 0, 0),
            Foreground = muted
        };
        _hint = new TextBlock
        {
            Text = "Aplicația se închide și se redeschide singură.",
            FontSize = 12,
            Margin = new Thickness(0, 14, 0, 0),
            Foreground = accent,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        };

        Content = new Border
        {
            Padding = new Thickness(28, 24, 28, 22),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "UPET AcqLab",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = accent,
                        Margin = new Thickness(0, 0, 0, 8)
                    },
                    _stage,
                    _detail,
                    _bar,
                    _bytes,
                    _hint
                }
            }
        };
    }

    public static UpdatingWindow? TryShow(Window? owner, string fromVersion, string toVersion)
    {
        try
        {
            var w = new UpdatingWindow(fromVersion, toVersion);
            if (owner is not null)
            {
                w.Owner = owner;
                w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            w.Show();
            if (owner is not null)
            {
                w.Left = owner.Left + Math.Max(0, (owner.ActualWidth - w.Width) / 2);
                w.Top = owner.Top + Math.Max(0, (owner.ActualHeight - w.ActualHeight) / 2);
            }
            return w;
        }
        catch
        {
            return null;
        }
    }

    public void SetStage(string stage, string? detail = null, bool complete = false)
    {
        OnUi(() =>
        {
            _stage.Text = stage;
            if (!string.IsNullOrWhiteSpace(detail))
                _detail.Text = detail;
            if (complete)
            {
                _bar.IsIndeterminate = false;
                _bar.Value = 100;
            }
        });
    }

    public void SetDownload(GitHubDownloadProgress p)
    {
        OnUi(() =>
        {
            if (p.HasTotal)
            {
                _bar.IsIndeterminate = false;
                _bar.Value = p.Percent;
                _bytes.Text = FormatBytes(p.BytesReceived) + " din " + FormatBytes(p.TotalBytes!.Value)
                              + "  (" + p.Percent.ToString("0", CultureInfo.InvariantCulture) + "%)";
            }
            else
            {
                _bar.IsIndeterminate = true;
                _bytes.Text = FormatBytes(p.BytesReceived) + " descărcați";
            }
        });
    }

    private void OnUi(Action action)
    {
        if (Dispatcher.CheckAccess())
            action();
        else
            Dispatcher.BeginInvoke(action);
    }

    internal static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return bytes + " o";
        if (bytes < 1024 * 1024)
            return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
    }
}
