using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Export;

namespace Spider8DAQ.App.Controls;

/// <summary>
/// Închide experimentul; poza după și observațiile sunt opționale.
/// </summary>
public sealed class CloseExperimentWindow : Window
{
    private readonly Image _preview;
    private readonly TextBlock _photoHint;
    private readonly TextBox _notesBox;
    private readonly string _sampleId;
    private readonly DateTime _fileStamp;
    private readonly string _recordingsDir;
    private string _afterPhotoPath;
    private DateTime? _afterCapturedAt;

    public bool Confirmed { get; private set; }
    public string MontagePhotoAfterPathValue => _afterPhotoPath;
    public string MontageAfterNotesValue { get; private set; } = "";
    public DateTime? MontageAfterCapturedAtValue => _afterCapturedAt;

    public CloseExperimentWindow(
        string projectName,
        string operatorName,
        string sampleId,
        DateTime? startedAt,
        string? beforePhotoPath,
        string? afterPhotoPath = null,
        string? afterNotes = null,
        DateTime? afterCapturedAt = null,
        string? recordingsDirectory = null)
    {
        _sampleId = sampleId ?? "";
        _fileStamp = startedAt ?? DateTime.Now;
        _recordingsDir = string.IsNullOrWhiteSpace(recordingsDirectory)
            ? AppPaths.EnsureWritable(AppPaths.Recordings)
            : AppPaths.EnsureWritable(recordingsDirectory);
        _afterPhotoPath = afterPhotoPath ?? "";
        _afterCapturedAt = afterCapturedAt;

        Title = "Închide experiment — UPET AcqLab";
        Width = 580;
        Height = 620;
        MinWidth = 460;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = "Închidere experiment",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA3, 0x3B, 0x2B))
        });
        header.Children.Add(new TextBlock
        {
            Text =
                $"{projectName} · {operatorName} · {sampleId}" +
                (startedAt is { } s ? $" · start {s:yyyy-MM-dd HH:mm:ss}" : ""),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(beforePhotoPath) || !File.Exists(beforePhotoPath)
                ? "Montaj înainte: — (opțional, neluat)"
                : $"Montaj înainte: {Path.GetFileName(beforePhotoPath)}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 6, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button
        {
            Content = "Închide experiment",
            Padding = new Thickness(16, 8, 16, 8),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xE6, 0xE4))
        };
        ok.Click += (_, _) =>
        {
            MontageAfterNotesValue = _notesBox.Text.Trim();
            Confirmed = true;
            DialogResult = true;
            Close();
        };
        var cancel = new Button
        {
            Content = "Anulare",
            Padding = new Thickness(14, 8, 14, 8),
            IsCancel = true
        };
        cancel.Click += (_, _) => { Confirmed = false; Close(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "Poză probă după experiment (opțional)",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 4)
        });
        body.Children.Add(new TextBlock
        {
            Text = "Dacă doriți, fotografiați epruveta după măsurare. Dacă nu — lăsați gol și apăsați Închide.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 12
        });

        var photoBtns = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var camBtn = new Button
        {
            Content = "Cameră — probă după…",
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 8, 4),
            FontWeight = FontWeights.SemiBold
        };
        camBtn.Click += (_, _) => OpenAfterCamera();
        var clearBtn = new Button
        {
            Content = "Șterge poza după",
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 4)
        };
        clearBtn.Click += (_, _) =>
        {
            _afterPhotoPath = "";
            _afterCapturedAt = null;
            _preview.Source = null;
            _photoHint.Text = "Fără poză după — închiderea e permisă oricum.";
        };
        photoBtns.Children.Add(camBtn);
        photoBtns.Children.Add(clearBtn);
        body.Children.Add(photoBtns);

        _photoHint = new TextBlock
        {
            Text = BuildPhotoHint(),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        body.Children.Add(_photoHint);

        _preview = new Image
        {
            MaxHeight = 180,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        body.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xCD, 0xD6)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = _preview,
            MinHeight = 100,
            Margin = new Thickness(0, 0, 0, 12)
        });

        body.Children.Add(new TextBlock
        {
            Text = "Observații operator — după (opțional)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        });
        body.Children.Add(new TextBlock
        {
            Text = "Ex.: fisură, deformare, demontare, aspect neschimbat…",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 0, 0, 4)
        });
        _notesBox = new TextBox
        {
            Text = afterNotes ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 72,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6, 4, 6, 4)
        };
        body.Children.Add(_notesBox);

        scroll.Content = body;
        root.Children.Add(scroll);
        Content = root;

        if (!string.IsNullOrWhiteSpace(_afterPhotoPath))
            ShowPreview(_afterPhotoPath);
    }

    private string BuildPhotoHint()
    {
        if (string.IsNullOrWhiteSpace(_afterPhotoPath))
            return "Opțional — fără poză puteți închide experimentul.";
        var name = Path.GetFileName(_afterPhotoPath);
        return _afterCapturedAt is { } t
            ? $"După: {name} · {t:yyyy-MM-dd HH:mm:ss}"
            : $"După: {name}";
    }

    private void OpenAfterCamera()
    {
        var baseName = ExperimentFileNaming.BuildBaseName(
            _sampleId,
            _fileStamp,
            ExperimentFileNaming.RoleAfter);
        var cam = new CameraCaptureWindow(
            string.IsNullOrWhiteSpace(_afterPhotoPath) ? null : _afterPhotoPath,
            title: "Probă după — cameră",
            heading: "Poză probă după experiment (opțional)",
            hint: "Fotografiați epruveta după măsurare dacă doriți. Fișier: " + baseName + ".jpg (lângă CSV).",
            saveDirectory: _recordingsDir,
            preferredBaseName: baseName)
        {
            Owner = this
        };
        if (cam.ShowDialog() == true && !string.IsNullOrWhiteSpace(cam.CapturedPath))
        {
            _afterPhotoPath = cam.CapturedPath!;
            _afterCapturedAt = DateTime.Now;
            _photoHint.Text = BuildPhotoHint();
            ShowPreview(_afterPhotoPath);
        }
    }

    private void ShowPreview(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _preview.Source = null;
                return;
            }
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
}
