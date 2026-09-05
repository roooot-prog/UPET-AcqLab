using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spider8DAQ.Core.Licensing;

namespace Spider8DAQ.App;

public class ApplicationKeyWindow : Window
{
    public bool ActivatedOk { get; private set; }

    public ApplicationKeyWindow(string serverUrl)
    {
        Title = "Cheie aplicație — UPET AcqLab";
        Width = 520;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Cheie aplicație",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6))
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var sub = new TextBlock
        {
            Text = "Introduceți cheia primită de la autor (drd. ing. Iucal Ilie / UPET). Fără cheie validă nu puteți folosi achiziția DAQ.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 16),
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73))
        };
        Grid.SetRow(sub, 1);
        root.Children.Add(sub);

        var keyLabel = new TextBlock { Text = "Cheie aplicație (UPET-APP-XXXX-XXXX-XXXX-XXXX)", FontWeight = FontWeights.SemiBold };
        Grid.SetRow(keyLabel, 2);
        root.Children.Add(keyLabel);

        var keyBox = new TextBox
        {
            Margin = new Thickness(0, 4, 0, 8),
            Padding = new Thickness(8, 6, 8, 6),
            FontFamily = new FontFamily("Consolas")
        };
        Grid.SetRow(keyBox, 3);
        root.Children.Add(keyBox);

        var status = new TextBlock
        {
            Text = "Prima activare necesită serverul de licențe.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(status, 4);
        root.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cont = new Button
        {
            Content = "Continuă",
            Width = 120,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        var exit = new Button { Content = "Ieșire", Width = 100, Height = 34, IsCancel = true };
        buttons.Children.Add(cont);
        buttons.Children.Add(exit);
        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);

        Content = root;

        exit.Click += (_, _) =>
        {
            ActivatedOk = false;
            DialogResult = false;
            Close();
        };

        cont.Click += async (_, _) =>
        {
            cont.IsEnabled = false;
            try
            {
                var key = keyBox.Text ?? "";
                if (!ApplicationKeyCrypto.LooksLikeApplicationKey(key))
                {
                    status.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x10, 0x2E));
                    status.Text = "Format așteptat: UPET-APP-XXXX-XXXX-XXXX-XXXX";
                    return;
                }

                status.Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73));
                status.Text = "Se verifică cheia pe server…";
                var result = await ApplicationKeyClient.ActivateRawKeyAsync(
                    serverUrl, key, TimeSpan.FromSeconds(12)).ConfigureAwait(true);

                if (result == ApplicationKeyHeartbeatResult.Ok)
                {
                    ActivatedOk = true;
                    DialogResult = true;
                    Close();
                    return;
                }

                status.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x10, 0x2E));
                status.Text = result switch
                {
                    ApplicationKeyHeartbeatResult.Ok => "",
                    ApplicationKeyHeartbeatResult.Expired =>
                        "Licența a expirat. Așteptați reaprobarea administratorului (Aprobă, 30 de zile).",
                    ApplicationKeyHeartbeatResult.Invalid =>
                        "Cheie invalidă. Nu puteți folosi achiziția DAQ fără o cheie acceptată de server.",
                    _ => "Serverul de licențe nu răspunde. Prima activare necesită conexiune la server."
                };
            }
            catch (Exception ex)
            {
                status.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x10, 0x2E));
                status.Text = "Eroare: " + ex.Message;
            }
            finally
            {
                cont.IsEnabled = true;
            }
        };
    }
}
