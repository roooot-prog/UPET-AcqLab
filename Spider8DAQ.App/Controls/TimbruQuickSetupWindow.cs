using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spider8DAQ.App.ViewModels;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Sensors;

namespace Spider8DAQ.App.Controls;

/// <summary>
/// Guided Timbru setup: pick experiment type → CH / GF / R → apply automatically.
/// Targets lab users who should not wrestle Bridge/BF terminology.
/// </summary>
public sealed class TimbruQuickSetupWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ComboBox _presetBox;
    private readonly TextBox _chBox;
    private readonly TextBox _gfBox;
    private readonly TextBox _rBox;
    private readonly TextBox _nuBox;
    private readonly TextBlock _preview;
    private readonly TextBlock _steps;

    private sealed record Preset(
        string Title,
        string Bridge,
        string HalfConfig,
        string Explain,
        bool NeedsNu);

    private static readonly Preset[] Presets =
    {
        new(
            "Activ pe grindă + timbru pasiv (compensare T°) pe placă (același canal)",
            nameof(BridgeType.Half),
            StrainScale.HalfConfigActivDummyT,
            "Echivalent NI Quarter Bridge II. Timbrul pasiv compensează temperatura. Scale = 4000/GF (BF=1).",
            false),
        new(
            "Axial + transversal pe piesă (Poisson)",
            nameof(BridgeType.Half),
            StrainScale.HalfConfigPoisson,
            "Ambele timbre pe grindă. Scale = 2000/(GF·(1+ν)).",
            true),
        new(
            "Încovoiere (sus / jos pe grindă)",
            nameof(BridgeType.Half),
            StrainScale.HalfConfigIncovoiere,
            "Unul sus, unul jos. Scale = 1000/GF.",
            false),
        new(
            "Half Simplu (2 activi, fără timbru pasiv dedicat)",
            nameof(BridgeType.Half),
            StrainScale.HalfConfigSimplu,
            "Scale = 2000/GF (BF≈2).",
            false),
        new(
            "Quarter + rezistență de completare în aparat",
            nameof(BridgeType.Quarter),
            StrainScale.HalfConfigSimplu,
            "Un singur timbru activ + Rcomp în Spider8. Scale = 4000/GF.",
            false),
    };

    public TimbruQuickSetupWindow(MainViewModel vm)
    {
        _vm = vm;
        Title = "Asistent Timbru — UPET AcqLab";
        Width = 560;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(18) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = "Setare rapidă tensometrie",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6))
        });
        header.Children.Add(new TextBlock
        {
            Text = "Alege tipul de experiment. Softul completează Tip punte, Config, Scale și aplică pe canal.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var apply = new Button
        {
            Content = "Configurează automat",
            Padding = new Thickness(16, 8, 16, 8),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        apply.Click += (_, _) => ApplyPreset();
        var cancel = new Button
        {
            Content = "Închide",
            Padding = new Thickness(14, 8, 14, 8),
            IsCancel = true
        };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(apply);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var form = new StackPanel();

        form.Children.Add(Label("1. Ce măsori?"));
        _presetBox = new ComboBox { Margin = new Thickness(0, 4, 0, 8), MinHeight = 28 };
        foreach (var p in Presets)
            _presetBox.Items.Add(new ComboBoxItem { Content = p.Title, Tag = p });
        _presetBox.SelectedIndex = 0;
        _presetBox.SelectionChanged += (_, _) => RefreshPreview();
        form.Children.Add(_presetBox);

        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(Field("2. Canal (CH0…CH7)", out _chBox, _vm.TimbruChannelUi.ToString(CultureInfo.InvariantCulture), 72));
        row.Children.Add(Field("3. GF (de pe pachet)", out _gfBox, FormatNum(_vm.TimbruGaugeFactor), 72));
        row.Children.Add(Field("4. R Ω", out _rBox, FormatNum(_vm.TimbruResistanceOhm), 72));
        row.Children.Add(Field("ν (doar Poisson)", out _nuBox, FormatNum(_vm.TimbruPoissonRatio), 64));
        _chBox.LostFocus += (_, _) => RefreshPreview();
        _gfBox.LostFocus += (_, _) => RefreshPreview();
        _gfBox.TextChanged += (_, _) => RefreshPreview();
        _rBox.LostFocus += (_, _) => RefreshPreview();
        _nuBox.LostFocus += (_, _) => RefreshPreview();
        form.Children.Add(row);

        _preview = new TextBlock
        {
            Margin = new Thickness(0, 14, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x24, 0x30))
        };
        form.Children.Add(_preview);

        _steps = new TextBlock
        {
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            FontSize = 12,
            LineHeight = 18
        };
        form.Children.Add(_steps);

        root.Children.Add(form);
        Content = root;
        RefreshPreview();
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 2, 0, 0)
    };

    private static UIElement Field(string label, out TextBox box, string value, double width)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 14, 8) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
        box = new TextBox { Text = value, Width = width, Padding = new Thickness(4, 4, 4, 4) };
        sp.Children.Add(box);
        return sp;
    }

    private Preset? SelectedPreset()
    {
        if (_presetBox.SelectedItem is ComboBoxItem { Tag: Preset p })
            return p;
        return Presets[0];
    }

    private static string FormatNum(double v) =>
        v.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool TryParseFlex(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private void RefreshPreview()
    {
        var p = SelectedPreset();
        if (p is null) return;

        if (!TryParseFlex(_gfBox.Text, out var gf) || gf <= 0)
            gf = 2.0;
        if (!TryParseFlex(_nuBox.Text, out var nu) || nu <= 0 || nu >= 1)
            nu = 0.3;

        var scale = StrainScale.FromGaugeFactor(p.Bridge, gf, p.HalfConfig, nu);
        _preview.Text = $"→ Bridge={p.Bridge}" +
                        (p.Bridge == nameof(BridgeType.Half) ? $" / {p.HalfConfig}" : "") +
                        $" · Scale={scale:0.####} µm/m per mV/V\n{p.Explain}";

        _nuBox.IsEnabled = p.NeedsNu;
        _steps.Text =
            "După Configurează automat:\n" +
            "1) Cablare: activ + timbru pasiv (compensare T°) pe același canal Half — butonul Arată cablare.\n" +
            "2) Connect → Start (stream).\n" +
            "3) Fără sarcină: Zero (F9).\n" +
            "4) Aplică sarcina → Record.\n" +
            "Canalul e CH0…CH7 ca în grila Măsurare (CH0 = primul canal hardware).\n" +
            "Notă: softul nu „detectează” timbrul pasiv; el e în cablaj. Configul setează doar scara corectă (BF)." +
            (_vm.TimbruAutorange
                ? "\nAutorange ON: după configurare se alege domeniul 2000/5000/10000/20000 µm/m (vârf live sau epruvetă) — nu se forțează 2000 peste un domeniu mai larg."
                : "");
    }

    private void ApplyPreset()
    {
        var p = SelectedPreset();
        if (p is null) return;

        if (!int.TryParse(_chBox.Text.Trim(), out var ch) || ch < 0 || ch > 7)
        {
            MessageBox.Show(this, "Canal invalid — folosește CH0…CH7 (ca în grila Măsurare).", "Asistent Timbru",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!TryParseFlex(_gfBox.Text, out var gf) || gf <= 0)
        {
            MessageBox.Show(this, "GF invalid — ia valoarea de pe pachetul timbrului.", "Asistent Timbru",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!TryParseFlex(_rBox.Text, out var r) || r <= 0)
            r = 350;
        if (!TryParseFlex(_nuBox.Text, out var nu) || nu <= 0 || nu >= 1)
            nu = 0.3;

        _vm.ApplyTimbruQuickSetup(ch, p.Bridge, p.HalfConfig, gf, r, nu,
            p.Bridge == nameof(BridgeType.Quarter) ? (int)Math.Round(r) : null);

        var scale = StrainScale.FromGaugeFactor(p.Bridge, gf, p.HalfConfig, nu);
        var ar = _vm.TimbruAutorange && _vm.TimbruAutorangeDomain > 0
            ? $"\nAutorange: domeniu {_vm.TimbruAutorangeDomain:0} µm/m (Scale GF={scale:0.####}, nu s-a forțat 2000 peste un domeniu mai larg)."
            : "";
        MessageBox.Show(this,
            $"Canal CH{ch} configurat.\n\n" +
            $"Bridge={p.Bridge}" + (p.Bridge == nameof(BridgeType.Half) ? $" / {p.HalfConfig}" : "") + "\n" +
            $"GF={gf:0.###} · R={r:0.#} Ω · Scale={scale:0.####} µm/m per mV/V · Exc=2.5 V{ar}\n\n" +
            "Următorii pași: Connect → Start → Zero (F9) fără sarcină → Record.",
            "Asistent Timbru — gata",
            MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
        Close();
    }
}
