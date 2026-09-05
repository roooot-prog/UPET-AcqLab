using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Spider8DAQ.Core.Licensing;

namespace Spider8DAQ.App;

/// <summary>
/// Shown when LicenseServerUrl is set and this PC has not been approved yet,
/// the 30-day window ended, or Admin pressed Revocă. No DAQ until Aprobă.
/// </summary>
public class ApplicationKeyPendingWindow : Window
{
    public bool ActivatedOk { get; private set; }

    private readonly string _serverUrl;
    private readonly ApplicationKeyGateReason _reason;
    private readonly DispatcherTimer _timer;
    private readonly TextBlock _status;
    private bool _busy;

    public ApplicationKeyPendingWindow(string serverUrl, bool expired = false)
        : this(serverUrl, expired ? ApplicationKeyGateReason.Expired : ApplicationKeyGateReason.Pending)
    {
    }

    public ApplicationKeyPendingWindow(string serverUrl, ApplicationKeyGateReason reason)
    {
        _serverUrl = serverUrl;
        _reason = reason;
        Title = reason switch
        {
            ApplicationKeyGateReason.Revoked => "Licența a fost revocată — UPET AcqLab",
            ApplicationKeyGateReason.Expired => "Licența a expirat — UPET AcqLab",
            _ => "Așteaptă aprobarea — UPET AcqLab"
        };
        Width = 540;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = reason switch
            {
                ApplicationKeyGateReason.Revoked => "Licența a fost revocată",
                ApplicationKeyGateReason.Expired => "Licența a expirat — așteaptă reaprobare",
                _ => "Așteaptă aprobarea administratorului"
            },
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6))
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var sub = new TextBlock
        {
            Text = reason switch
            {
                ApplicationKeyGateReason.Revoked =>
                    "Administratorul a oprit imediat accesul pe acest PC. Achiziția DAQ este închisă. Măsurarea nu pornește până la o nouă apăsare pe Aprobă în UPET AcqLab Admin.",
                ApplicationKeyGateReason.Expired =>
                    "Perioada de 30 de zile s-a încheiat. Achiziția DAQ nu pornește până când administratorul apasă din nou Aprobă în UPET AcqLab Admin.",
                _ =>
                    "Acest PC s-a înregistrat pe serverul de licențe. Achiziția DAQ nu pornește până când administratorul apasă Aprobă în UPET AcqLab Admin. Aprobarea este valabilă 30 de zile."
            },
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 16),
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73))
        };
        Grid.SetRow(sub, 1);
        root.Children.Add(sub);

        _status = new TextBlock
        {
            Text = WaitingStatus(),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73))
        };
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);

        var exit = new Button
        {
            Content = "Ieșire",
            Width = 100,
            Height = 34,
            HorizontalAlignment = HorizontalAlignment.Right,
            IsCancel = true
        };
        Grid.SetRow(exit, 3);
        root.Children.Add(exit);
        Content = root;

        exit.Click += (_, _) =>
        {
            ActivatedOk = false;
            DialogResult = false;
            Close();
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += async (_, _) => await PollOnceAsync().ConfigureAwait(true);
        Closed += (_, _) => _timer.Stop();

        Loaded += async (_, _) =>
        {
            await PollOnceAsync().ConfigureAwait(true);
            _timer.Start();
        };
    }

    private string WaitingStatus() => _reason switch
    {
        ApplicationKeyGateReason.Revoked =>
            "Revocat — fără măsurare. Se așteaptă o nouă aprobare… (PC: " + Environment.MachineName + ")",
        ApplicationKeyGateReason.Expired =>
            "Se anunță serverul (expirat — așteaptă reaprobare)…",
        _ => "Se trimite cererea de aprobare…"
    };

    private async Task PollOnceAsync()
    {
        if (_busy || ActivatedOk) return;
        _busy = true;
        try
        {
            var timeout = TimeSpan.FromSeconds(5);
            var urls = UrlsToTry();
            var pending = await ApplicationKeyClient.RegisterPendingFirstAsync(urls, timeout)
                .ConfigureAwait(true);
            if (!pending.Ok || string.IsNullOrWhiteSpace(pending.UsedUrl))
            {
                SetStatus(
                    "Serverul de licențe nu răspunde. Se încearcă URL public și IP-urile LAN din license-server.json.",
                    error: true);
                return;
            }

            var status = await ApplicationKeyClient.GetStatusAsync(pending.UsedUrl, timeout)
                .ConfigureAwait(true);

            if (ApplicationKeyClient.TryAcceptApprovedKey(status))
            {
                _timer.Stop();
                ActivatedOk = true;
                DialogResult = true;
                Close();
                return;
            }

            if (string.Equals(status.Status, "approved", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(
                    "Administratorul a aprobat PC-ul, dar cheia nu a putut fi salvată. Cereți o nouă apăsare pe Aprobă.",
                    error: true);
                return;
            }

            if (string.Equals(status.Status, "revoked", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(
                    "Revocat — fără măsurare. Se așteaptă o nouă aprobare… (PC: " + Environment.MachineName + ")",
                    error: true);
                return;
            }

            if (string.Equals(status.Status, "expired", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(
                    "Expirat — așteaptă reaprobare… (PC: " + Environment.MachineName + ")",
                    error: false);
                return;
            }

            if (string.Equals(status.Status, "pending", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status.Status, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus(
                    _reason == ApplicationKeyGateReason.Revoked
                        ? "Revocat — fără măsurare. Se așteaptă o nouă aprobare… (PC: " + Environment.MachineName + ")"
                        : _reason == ApplicationKeyGateReason.Expired
                            ? "Expirat — așteaptă reaprobare… (PC: " + Environment.MachineName + ")"
                            : "Așteaptă aprobarea administratorului… (PC: " + Environment.MachineName + ")",
                    error: false);
                return;
            }

            SetStatus("Se așteaptă răspunsul serverului…", error: false);
        }
        catch (Exception ex)
        {
            SetStatus("Eroare: " + ex.Message, error: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private IReadOnlyList<string> UrlsToTry()
    {
        var list = new List<string>();
        void add(string? raw)
        {
            var s = (raw ?? "").Trim().TrimEnd('/');
            if (s.Length == 0) return;
            foreach (var x in list)
            {
                if (string.Equals(x, s, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(s);
        }

        add(_serverUrl);
        foreach (var u in ApplicationKeyConfig.Load().CandidateUrls)
            add(u);
        return list;
    }

    private void SetStatus(string text, bool error)
    {
        _status.Text = text;
        _status.Foreground = new SolidColorBrush(error
            ? Color.FromRgb(0xC8, 0x10, 0x2E)
            : Color.FromRgb(0x5A, 0x65, 0x73));
    }
}
