using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UPETAcqLab.Admin;

public partial class MainWindow : Window
{
    private LicenseApiClient? _client;

    public MainWindow()
    {
        InitializeComponent();
        var local = AdminLocalSettings.Load();
        UrlBox.Text = string.IsNullOrWhiteSpace(local.LicenseServerUrl)
            ? "http://127.0.0.1:5088"
            : local.LicenseServerUrl;
        if (!string.IsNullOrEmpty(local.AdminPassword))
            PasswordBox.Password = local.AdminPassword;
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Se conectează…");
            var client = new LicenseApiClient(UrlBox.Text, PasswordBox.Password);
            await client.PingAsync().ConfigureAwait(true);
            _client = client;
            var save = AdminLocalSettings.Load();
            save.LicenseServerUrl = client.BaseUrl;
            save.AdminPassword = PasswordBox.Password;
            save.Save();
            GenerateButton.IsEnabled = true;
            RefreshButton.IsEnabled = true;
            await LoadRowsAsync().ConfigureAwait(true);
            SetBusy(false, "Conectat. Cheile se generează aici; IP-ul e cel văzut de server.");
        }
        catch (Exception ex)
        {
            _client = null;
            GenerateButton.IsEnabled = false;
            RefreshButton.IsEnabled = false;
            SetBusy(false, ex.Message, error: true);
        }
    }

    private async void Generate_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null) return;
        try
        {
            SetBusy(true, "Se generează cheia…");
            var key = await _client.GenerateKeyAsync().ConfigureAwait(true);
            ShowKeyOnce(key);
            await LoadRowsAsync().ConfigureAwait(true);
            SetBusy(false, "Cheie generată. Copiați-o acum — nu se mai afișează.");
        }
        catch (Exception ex)
        {
            SetBusy(false, ex.Message, error: true);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "Se reîncarcă…");
            await LoadRowsAsync().ConfigureAwait(true);
            SetBusy(false, "Listă actualizată.");
        }
        catch (Exception ex)
        {
            SetBusy(false, ex.Message, error: true);
        }
    }

    private async Task LoadRowsAsync()
    {
        if (_client is null) return;
        var snap = await _client.ListAsync().ConfigureAwait(true);
        CountText.Text = snap.InstallCount == 1
            ? "1 instalare"
            : snap.InstallCount + " instalări";
        GridActivations.ItemsSource = snap.Activations;
    }

    private void ShowKeyOnce(string key)
    {
        var dlg = new Window
        {
            Title = "Cheie aplicație (o singură dată)",
            Owner = this,
            Width = 560,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0xDC, 0xE0, 0xE6))
        };
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = "Copiați cheia acum. Serverul păstrează doar hash SHA256.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        var box = new TextBox
        {
            Text = key,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 16,
            IsReadOnly = true,
            Padding = new Thickness(8)
        };
        root.Children.Add(box);
        var copy = new Button
        {
            Content = "Copiază",
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(key); } catch { /* ignore */ }
            dlg.DialogResult = true;
            dlg.Close();
        };
        root.Children.Add(copy);
        dlg.Content = root;
        dlg.ShowDialog();
    }

    private void SetBusy(bool busy, string message, bool error = false)
    {
        ConnectButton.IsEnabled = !busy;
        GenerateButton.IsEnabled = !busy && _client is not null;
        RefreshButton.IsEnabled = !busy && _client is not null;
        StatusText.Text = message;
        StatusText.Foreground = new SolidColorBrush(error
            ? Color.FromRgb(0xC8, 0x10, 0x2E)
            : Color.FromRgb(0x55, 0x55, 0x55));
    }
}
