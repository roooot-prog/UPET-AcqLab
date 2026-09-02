using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Integrations;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;

namespace Spider8DAQ.App.Controls;

/// <summary>
/// Capture montage photo from laptop/tablet camera, Windows Camera app, or file.
/// </summary>
public sealed class CameraCaptureWindow : Window
{
    private readonly Image _preview;
    private readonly TextBlock _status;
    private readonly Button _shotBtn;
    private readonly string _saveDirectory;
    private readonly string? _preferredBaseName;
    private MediaCapture? _capture;
    private bool _cameraReady;

    public string? CapturedPath { get; private set; }

    public CameraCaptureWindow(
        string? existingPath = null,
        string? title = null,
        string? heading = null,
        string? hint = null,
        string? saveDirectory = null,
        string? preferredBaseName = null)
    {
        _saveDirectory = string.IsNullOrWhiteSpace(saveDirectory)
            ? AppPaths.EnsureWritable(Path.Combine(AppPaths.Camera, "montage"))
            : AppPaths.EnsureWritable(saveDirectory);
        _preferredBaseName = string.IsNullOrWhiteSpace(preferredBaseName)
            ? null
            : preferredBaseName.Trim();

        Title = string.IsNullOrWhiteSpace(title) ? "Foto — cameră" : title;
        Width = 560;
        Height = 560;
        MinWidth = 440;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(heading) ? "Foto epruvetă / banc" : heading,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6))
        });
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(hint)
                ? "Aplicația cere acces la camera dispozitivului. Pe tabletă/laptop: permiteți camera în Windows Settings dacă e cerut."
                : hint,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 4, 0, 0),
            FontSize = 12
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button
        {
            Content = "Folosește poza",
            Padding = new Thickness(14, 8, 14, 8),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0xD4, 0xE5, 0xDA))
        };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(CapturedPath) || !File.Exists(CapturedPath))
            {
                MessageBox.Show(this, "Nu există nicio poză. Fotografiați sau alegeți un fișier.",
                    "UPET AcqLab", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            DialogResult = true;
            Close();
        };
        var cancel = new Button
        {
            Content = "Anulare",
            Padding = new Thickness(14, 8, 14, 8),
            IsCancel = true
        };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        footer.Children.Add(ok);
        footer.Children.Add(cancel);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var body = new DockPanel();
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        _shotBtn = new Button
        {
            Content = "Fotografiază (cameră)",
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 6),
            FontWeight = FontWeights.SemiBold,
            IsEnabled = false
        };
        _shotBtn.Click += async (_, _) => await CaptureAsync();
        var fileBtn = new Button
        {
            Content = "Din fișier…",
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 6)
        };
        fileBtn.Click += (_, _) => PickFile();
        var winCamBtn = new Button
        {
            Content = "Camera Windows",
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 6)
        };
        winCamBtn.Click += (_, _) =>
        {
            if (LabCameraHub.TryOpenWindowsCamera())
            {
                _status.Text = "Camera Windows deschisă — salvați poza, apoi «Din fișier…».";
                PickFile();
            }
            else
                _status.Text = "Nu s-a putut deschide Camera Windows.";
        };
        actions.Children.Add(_shotBtn);
        actions.Children.Add(fileBtn);
        actions.Children.Add(winCamBtn);
        DockPanel.SetDock(actions, Dock.Top);
        body.Children.Add(actions);

        _status = new TextBlock
        {
            Text = "Se inițializează camera…",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73))
        };
        DockPanel.SetDock(_status, Dock.Top);
        body.Children.Add(_status);

        _preview = new Image
        {
            Stretch = Stretch.Uniform,
            MaxHeight = 320,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var border = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xCD, 0xD6)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = _preview,
            MinHeight = 200
        };
        body.Children.Add(border);
        root.Children.Add(body);
        Content = root;

        if (!string.IsNullOrWhiteSpace(existingPath) && File.Exists(existingPath))
        {
            CapturedPath = existingPath;
            ShowPreview(existingPath);
            _status.Text = "Poză existentă — puteți reface captura.";
        }

        Loaded += async (_, _) => await InitCameraAsync();
        Closed += (_, _) => DisposeCapture();
    }

    private async Task InitCameraAsync()
    {
        try
        {
            _capture = new MediaCapture();
            await _capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                PhotoCaptureSource = PhotoCaptureSource.Auto
            });
            _cameraReady = true;
            _shotBtn.IsEnabled = true;
            _status.Text = "Cameră accesată. Apăsați «Fotografiază (cameră)».";
        }
        catch (UnauthorizedAccessException)
        {
            _cameraReady = false;
            _shotBtn.IsEnabled = false;
            _status.Text =
                "Acces cameră refuzat. Activați Camera pentru desktop apps în Setări Windows → Privacy, " +
                "sau folosiți «Camera Windows» / «Din fișier…».";
        }
        catch (Exception ex)
        {
            _cameraReady = false;
            _shotBtn.IsEnabled = false;
            _status.Text =
                "Cameră indisponibilă (" + Truncate(ex.Message, 120) + "). " +
                "Folosiți «Camera Windows» sau «Din fișier…».";
        }
    }

    private async Task CaptureAsync()
    {
        if (!_cameraReady || _capture is null)
        {
            _status.Text = "Camera nu e gata.";
            return;
        }

        try
        {
            _shotBtn.IsEnabled = false;
            _status.Text = "Captură…";
            var dir = AppPaths.EnsureWritable(_saveDirectory);
            var folder = await StorageFolder.GetFolderFromPathAsync(dir);
            var name = string.IsNullOrWhiteSpace(_preferredBaseName)
                ? $"montage_{DateTime.Now:yyyyMMdd_HHmmss}.jpg"
                : _preferredBaseName + ".jpg";
            var file = await folder.CreateFileAsync(name, CreationCollisionOption.GenerateUniqueName);
            await _capture.CapturePhotoToStorageFileAsync(ImageEncodingProperties.CreateJpeg(), file);
            CapturedPath = file.Path;
            ShowPreview(CapturedPath);
            _status.Text = "Poză salvată: " + Path.GetFileName(CapturedPath);
        }
        catch (Exception ex)
        {
            _status.Text = "Captură eșuată: " + Truncate(ex.Message, 160);
        }
        finally
        {
            _shotBtn.IsEnabled = _cameraReady;
        }
    }

    private void PickFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Alege foto / schiță montaj",
            Filter = "Imagini|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.webp|Toate|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var dir = AppPaths.EnsureWritable(_saveDirectory);
            var ext = Path.GetExtension(dlg.FileName);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            var baseName = string.IsNullOrWhiteSpace(_preferredBaseName)
                ? $"montage_{DateTime.Now:yyyyMMdd_HHmmss}"
                : _preferredBaseName;
            var dest = Path.Combine(dir, baseName + ext);
            if (File.Exists(dest))
                dest = Path.Combine(dir, $"{baseName}_{DateTime.Now:HHmmss}{ext}");
            File.Copy(dlg.FileName, dest, overwrite: true);
            CapturedPath = dest;
            ShowPreview(dest);
            _status.Text = "Imagine atașată: " + Path.GetFileName(dest);
        }
        catch (Exception ex)
        {
            _status.Text = "Copiere eșuată: " + Truncate(ex.Message, 140);
        }
    }

    private void ShowPreview(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            _preview.Source = bmp;
        }
        catch
        {
            _preview.Source = null;
        }
    }

    private void DisposeCapture()
    {
        try
        {
            _capture?.Dispose();
        }
        catch { /* ignore */ }
        _capture = null;
        _cameraReady = false;
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
}
