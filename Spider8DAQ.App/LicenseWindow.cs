using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spider8DAQ.Core.Licensing;

namespace Spider8DAQ.App;

public class LicenseWindow : Window
{
    public bool ActivatedOk { get; private set; }

    public LicenseWindow()
    {
        Title = "Activare licență — UPET AcqLab";
        Width = 520;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "UPET AcqLab",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6))
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var sub = new TextBlock
        {
            Text = "Universitatea din Petroșani — activare licență personală",
            Margin = new Thickness(0, 4, 0, 16),
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73))
        };
        Grid.SetRow(sub, 1);
        root.Children.Add(sub);

        var nameLabel = new TextBlock { Text = "Nume titular", FontWeight = FontWeights.SemiBold };
        Grid.SetRow(nameLabel, 2);
        root.Children.Add(nameLabel);

        var nameBox = new TextBox { Margin = new Thickness(0, 4, 0, 12), Padding = new Thickness(8, 6, 8, 6) };
        Grid.SetRow(nameBox, 3);
        root.Children.Add(nameBox);

        var keyLabel = new TextBlock { Text = "Cheie licență (UPET-ACQLAB-XXXX-XXXX-XXXX-XXXX)", FontWeight = FontWeights.SemiBold };
        Grid.SetRow(keyLabel, 4);
        root.Children.Add(keyLabel);

        var keyBox = new TextBox { Margin = new Thickness(0, 4, 0, 8), Padding = new Thickness(8, 6, 8, 6), FontFamily = new FontFamily("Consolas") };
        Grid.SetRow(keyBox, 5);
        root.Children.Add(keyBox);

        var status = new TextBlock
        {
            Text = "Introduceți cheia personală primită pentru UPET AcqLab. Fără licență validă aplicația nu pornește.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(status, 6);
        root.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var activate = new Button
        {
            Content = "Activează",
            Width = 120,
            Height = 34,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        var exit = new Button { Content = "Ieșire", Width = 100, Height = 34, IsCancel = true };
        buttons.Children.Add(activate);
        buttons.Children.Add(exit);
        Grid.SetRow(buttons, 7);
        root.Children.Add(buttons);

        Content = root;

        exit.Click += (_, _) =>
        {
            ActivatedOk = false;
            DialogResult = false;
            Close();
        };

        activate.Click += (_, _) =>
        {
            try
            {
                if (!UpetLicense.TryValidateKey(keyBox.Text, nameBox.Text, out var err))
                {
                    status.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x10, 0x2E));
                    status.Text = err;
                    return;
                }

                UpetLicense.SaveActivation(nameBox.Text, keyBox.Text);
                ActivatedOk = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                status.Foreground = new SolidColorBrush(Color.FromRgb(0xC8, 0x10, 0x2E));
                status.Text = "Eroare activare: " + ex.Message;
            }
        };
    }
}
