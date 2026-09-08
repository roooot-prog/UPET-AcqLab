using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Spider8DAQ.App.Controls;

/// <summary>Options for lab package export: optional password → .upetlab.</summary>
public sealed class LabPackageOptionsWindow : Window
{
    private readonly CheckBox _usePassword;
    private readonly PasswordBox _password;
    private readonly PasswordBox _password2;
    private readonly TextBlock _hint;

    public bool Confirmed { get; private set; }
    public bool UsePassword => _usePassword.IsChecked == true;
    public string Password => _password.Password ?? "";

    public LabPackageOptionsWindow()
    {
        Title = "Pachet laborator — UPET AcqLab";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        MinWidth = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));

        var root = new DockPanel { Margin = new Thickness(16) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var ok = new Button
        {
            Content = "Creează pachet",
            Padding = new Thickness(14, 8, 14, 8),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0xD4, 0xE5, 0xDA))
        };
        ok.Click += (_, _) =>
        {
            if (!Validate(out var err))
            {
                _hint.Text = err;
                _hint.Foreground = new SolidColorBrush(Color.FromRgb(0xA3, 0x3B, 0x2B));
                return;
            }

            Confirmed = true;
            DialogResult = true;
            Close();
        };
        var cancel = new Button
        {
            Content = "Anulați",
            Padding = new Thickness(14, 8, 14, 8),
            IsCancel = true
        };
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
            Text = "Pachetul include: README, CSV, poze montaj, clip film (dacă ați filmat la Rec), raport .upet, Excel și HTML (dacă reușesc).",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
            FontSize = 13
        });
        body.Children.Add(new TextBlock
        {
            Text =
                "Implicit: ZIP fără parolă (se deschide în Explorer).\n" +
                "Opțional: .upetlab criptat AES cu parolă (doar UPET AcqLab) — nu ZipCrypto slab.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12)
        });

        _usePassword = new CheckBox
        {
            Content = "Protejează cu parolă (.upetlab)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _usePassword.Checked += (_, _) => UpdatePasswordEnabled();
        _usePassword.Unchecked += (_, _) => UpdatePasswordEnabled();
        body.Children.Add(_usePassword);

        body.Children.Add(new TextBlock { Text = "Parolă", Margin = new Thickness(0, 4, 0, 2), FontSize = 12 });
        _password = new PasswordBox { Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(6, 4, 6, 4) };
        body.Children.Add(_password);
        body.Children.Add(new TextBlock { Text = "Confirmare parolă", Margin = new Thickness(0, 0, 0, 2), FontSize = 12 });
        _password2 = new PasswordBox { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6, 4, 6, 4) };
        body.Children.Add(_password2);

        _hint = new TextBlock
        {
            Text = "Lăsați nebifat pentru ZIP obișnuit.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73))
        };
        body.Children.Add(_hint);
        root.Children.Add(body);
        Content = root;
        UpdatePasswordEnabled();
    }

    private void UpdatePasswordEnabled()
    {
        var on = _usePassword.IsChecked == true;
        _password.IsEnabled = on;
        _password2.IsEnabled = on;
    }

    private bool Validate(out string error)
    {
        error = "";
        if (_usePassword.IsChecked != true) return true;
        if (string.IsNullOrEmpty(_password.Password))
        {
            error = "Introduceți o parolă sau debifați protecția.";
            return false;
        }

        if (_password.Password.Length < 4)
        {
            error = "Parola trebuie să aibă cel puțin 4 caractere.";
            return false;
        }

        if (!string.Equals(_password.Password, _password2.Password, StringComparison.Ordinal))
        {
            error = "Parolele nu coincid.";
            return false;
        }

        return true;
    }
}
