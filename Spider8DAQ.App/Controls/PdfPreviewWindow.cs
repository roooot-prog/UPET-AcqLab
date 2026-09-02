using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Spider8DAQ.App.Controls;

/// <summary>
/// In-app PDF export preview: page thumbnails (Y(t) / CWT JPEGs) + optional temp PDF open.
/// Confirm → Save As; Cancel → discard.
/// </summary>
public sealed class PdfPreviewWindow : Window
{
    private readonly string _tempPdfPath;
    private readonly List<string> _ownedTempFiles = new();

    public string? ConfirmedSavePath { get; private set; }

    /// <summary>Default Save As file name (e.g. proba_20260815_153012_raport.pdf).</summary>
    public string SuggestedFileName { get; set; } = "report.pdf";

    public PdfPreviewWindow(
        string tempPdfPath,
        IReadOnlyList<string> previewImagePaths,
        string summary)
    {
        _tempPdfPath = tempPdfPath;
        Title = "Previzualizare raport PDF — UPET AcqLab";
        Width = 920;
        Height = 720;
        MinWidth = 640;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xC8, 0xCE, 0xD6));
        Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1F, 0x26));

        var root = new DockPanel { Margin = new Thickness(12) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = "Previzualizare raport PDF",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = summary,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0x58, 0x64)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        var cancel = new Button
        {
            Content = "Anulează",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 100,
            Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE2, 0xE8)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1F, 0x26))
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        var openExt = new Button
        {
            Content = "Deschide PDF temporar",
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(0, 0, 8, 0),
            MinWidth = 140,
            Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE2, 0xE8)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1F, 0x26)),
            ToolTip = "Deschide PDF-ul generat în vizualizatorul sistemului (Edge / Acrobat)."
        };
        openExt.Click += (_, _) => TryOpenExternal(_tempPdfPath);
        var confirm = new Button
        {
            Content = "Confirmă salvare…",
            Padding = new Thickness(14, 6, 14, 6),
            MinWidth = 140,
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6)),
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold
        };
        confirm.Click += (_, _) => ConfirmSave();
        DockPanel.SetDock(cancel, Dock.Right);
        DockPanel.SetDock(confirm, Dock.Right);
        DockPanel.SetDock(openExt, Dock.Left);
        buttons.Children.Add(openExt);
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xEC, 0xF0)),
            Padding = new Thickness(10)
        };
        var pages = new StackPanel();
        var shown = 0;
        foreach (var path in previewImagePaths.Where(File.Exists).Take(8))
        {
            shown++;
            pages.Children.Add(BuildPageCard(path, shown == 1 ? "Pagina 1 — Y(t) / antet" : $"Pagină / scalogramă {shown}"));
        }

        if (shown == 0)
        {
            pages.Children.Add(new TextBlock
            {
                Text = "Nu există previzualizare imagine. Folosiți „Deschide PDF temporar” sau Confirmați pentru salvare.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8),
                Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0x58, 0x64))
            });
        }

        // Embedded document host (IE/Edge PDF handler when available).
        try
        {
            if (File.Exists(_tempPdfPath))
            {
                var browserHost = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAD)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 12, 0, 0),
                    Height = 360,
                    Background = Brushes.White
                };
                var browser = new WebBrowser();
                browserHost.Child = browser;
                pages.Children.Add(new TextBlock
                {
                    Text = "Vizualizare PDF (dacă sistemul are handler PDF):",
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 4)
                });
                pages.Children.Add(browserHost);
                Loaded += (_, _) =>
                {
                    try { browser.Navigate(new Uri(_tempPdfPath)); }
                    catch { /* non-fatal — thumbnails remain */ }
                };
            }
        }
        catch
        {
            /* WebBrowser optional */
        }

        scroll.Content = pages;
        root.Children.Add(scroll);
        Content = root;
    }

    private static Border BuildPageCard(string imagePath, string caption)
    {
        var card = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAD)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 12)
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = caption,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(imagePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            stack.Children.Add(new Image
            {
                Source = bmp,
                Stretch = Stretch.Uniform,
                MaxHeight = 420,
                HorizontalAlignment = HorizontalAlignment.Center
            });
        }
        catch
        {
            stack.Children.Add(new TextBlock { Text = $"(Nu s-a putut încărca: {Path.GetFileName(imagePath)})" });
        }
        card.Child = stack;
        return card;
    }

    private void ConfirmSave()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PDF|*.pdf",
            FileName = string.IsNullOrWhiteSpace(SuggestedFileName) ? "report.pdf" : SuggestedFileName,
            InitialDirectory = Path.GetDirectoryName(_tempPdfPath) is { } d && Directory.Exists(d)
                ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        // Prefer recordings dir if caller set it via Tag.
        if (Tag is string dir && Directory.Exists(dir))
            dlg.InitialDirectory = dir;

        if (dlg.ShowDialog(this) != true) return;
        try
        {
            File.Copy(_tempPdfPath, dlg.FileName, overwrite: true);
            ConfirmedSavePath = dlg.FileName;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Salvare PDF eșuată:\n" + ex.Message, "UPET AcqLab",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void TryOpenExternal(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Nu s-a putut deschide PDF-ul:\n" + ex.Message, "UPET AcqLab",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>Deletes temp PDF and optional owned files after dialog closes.</summary>
    public void CleanupOwnedFiles(IEnumerable<string>? extraFiles = null)
    {
        foreach (var f in _ownedTempFiles.Concat(extraFiles ?? Array.Empty<string>()).Append(_tempPdfPath).Distinct())
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { /* ignore */ }
        }
    }
}
