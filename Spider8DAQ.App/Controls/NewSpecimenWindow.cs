using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Spider8DAQ.Core.Specimens;

namespace Spider8DAQ.App.Controls;

/// <summary>Dialog for a custom specimen card (saved locally next to app data).</summary>
public sealed class NewSpecimenWindow : Window
{
    private readonly TextBox _nameBox;
    private readonly ComboBox _classBox;
    private readonly ComboBox _packBox;
    private readonly TextBox _aliasBox;
    private readonly TextBox _l0Box;
    private readonly TextBox _d0Box;
    private readonly TextBox _eBox;
    private readonly TextBox _nuBox;
    private readonly TextBox _rhoBox;
    private readonly TextBox _strengthBox;
    private readonly TextBox _standardBox;
    private readonly TextBox _notesBox;

    public bool Confirmed { get; private set; }
    public SpecimenCard? Card { get; private set; }

    public NewSpecimenWindow(SpecimenCard? seed = null)
    {
        Title = "Epruvetă nouă — UPET AcqLab";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MinWidth = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));

        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = "Epruvetă personalizată",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6))
        });
        header.Children.Add(new TextBlock
        {
            Text = "Opțional. Valorile sunt indicative — confirmați pe proba măsurată. Se salvează local. Anulare revine fără epruvetă.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 4, 0, 0)
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
            Content = "Salvează epruveta",
            Padding = new Thickness(14, 8, 14, 8),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(0xD4, 0xE5, 0xDA))
        };
        ok.Click += (_, _) => Confirm();
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

        var form = new Grid();
        for (var i = 0; i < 10; i++)
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _nameBox = AddText(form, 0, "Nume", seed?.NameRo ?? "");
        _classBox = new ComboBox { Margin = new Thickness(0, 0, 0, 8), MinHeight = 28 };
        foreach (var c in SpecimenClasses.All)
            _classBox.Items.Add(c);
        _classBox.SelectedItem = seed?.Class ?? SpecimenClasses.Personalizat;
        AddCtl(form, 1, "Clasă", _classBox);

        _packBox = new ComboBox { Margin = new Thickness(0, 0, 0, 8), MinHeight = 28 };
        _packBox.Items.Add(FormulaPacks.LabelSteelMetal);
        _packBox.Items.Add(FormulaPacks.LabelIsrmUcs);
        _packBox.Items.Add(FormulaPacks.LabelSaltCreep);
        _packBox.Items.Add(FormulaPacks.LabelEn12390);
        _packBox.SelectedItem = string.IsNullOrWhiteSpace(seed?.FormulaPack)
            ? FormulaPacks.LabelRo(FormulaPacks.DefaultForClass(seed?.Class))
            : FormulaPacks.LabelRo(seed!.FormulaPack);
        AddCtl(form, 2, "Pachet formule", _packBox);
        _classBox.SelectionChanged += (_, _) =>
        {
            if (_classBox.SelectedItem is string cls)
                _packBox.SelectedItem = FormulaPacks.LabelRo(FormulaPacks.DefaultForClass(cls));
        };

        _aliasBox = AddText(form, 3, "Aliasuri (căutare)",
            seed?.Aliases is { Count: > 0 } ? string.Join(", ", seed.Aliases) : "");
        _l0Box = AddText(form, 4, "L0 [mm]", Num(seed?.L0Mm ?? 80));
        _d0Box = AddText(form, 5, "Ø [mm]", Num(seed?.D0Mm ?? 40));
        _eBox = AddText(form, 6, "E [GPa]", Num(seed?.EGPa ?? 0));
        _nuBox = AddText(form, 7, "ν", Num(seed?.Nu ?? 0.3));
        _rhoBox = AddText(form, 8, "Densitate [kg/m³]", Num(seed?.DensityKgM3 ?? 0));

        var more = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        _strengthBox = new TextBox
        {
            Text = seed?.StrengthNotes ?? "",
            Margin = new Thickness(0, 0, 0, 6),
            MinHeight = 28,
            Padding = new Thickness(6, 4, 6, 4)
        };
        more.Children.Add(new TextBlock { Text = "Rezistență (fy / UCS / f_c)", FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
        more.Children.Add(_strengthBox);
        _standardBox = new TextBox
        {
            Text = seed?.StandardNote ?? "Personalizat — valori indicative, confirmați pe probă.",
            Margin = new Thickness(0, 0, 0, 6),
            MinHeight = 28,
            Padding = new Thickness(6, 4, 6, 4)
        };
        more.Children.Add(new TextBlock { Text = "Standard / notă", FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
        more.Children.Add(_standardBox);
        _notesBox = new TextBox
        {
            Text = seed?.Notes ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 48,
            Padding = new Thickness(6, 4, 6, 4)
        };
        more.Children.Add(new TextBlock { Text = "Note (T, umiditate — fluaj)", FontSize = 11, Margin = new Thickness(0, 0, 0, 2) });
        more.Children.Add(_notesBox);
        AddCtl(form, 9, "Note", more);

        root.Children.Add(form);
        Content = root;
    }

    private void Confirm()
    {
        var name = _nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show(this, "Completați numele epruvetei.", "Epruvetă nouă",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var cls = _classBox.SelectedItem as string ?? SpecimenClasses.Personalizat;
        var packLabel = _packBox.SelectedItem as string ?? "";
        var pack = packLabel switch
        {
            FormulaPacks.LabelIsrmUcs => FormulaPacks.IsrmUcs,
            FormulaPacks.LabelSaltCreep => FormulaPacks.SaltCreep,
            FormulaPacks.LabelEn12390 => FormulaPacks.En12390,
            _ => FormulaPacks.SteelMetal
        };

        Card = new SpecimenCard
        {
            Id = "custom-" + Guid.NewGuid().ToString("N")[..10],
            NameRo = name,
            Class = cls,
            FormulaPack = pack,
            Shape = SpecimenShapes.Cilindru,
            IsCustom = true,
            Aliases = (_aliasBox.Text ?? "")
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            L0Mm = Parse(_l0Box.Text),
            D0Mm = Parse(_d0Box.Text),
            EGPa = Parse(_eBox.Text),
            Nu = Parse(_nuBox.Text),
            DensityKgM3 = Parse(_rhoBox.Text),
            StrengthNotes = _strengthBox.Text.Trim(),
            StandardNote = _standardBox.Text.Trim(),
            Notes = _notesBox.Text.Trim()
        };
        Confirmed = true;
        DialogResult = true;
        Close();
    }

    private static TextBox AddText(Grid form, int row, string label, string value)
    {
        var box = new TextBox
        {
            Text = value ?? "",
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 28,
            Padding = new Thickness(6, 4, 6, 4)
        };
        AddCtl(form, row, label, box);
        return box;
    }

    private static void AddCtl(Grid form, int row, string label, UIElement control)
    {
        var tb = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Top,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 8, 8)
        };
        Grid.SetRow(tb, row);
        Grid.SetColumn(tb, 0);
        form.Children.Add(tb);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);
        form.Children.Add(control);
    }

    private static string Num(double v) =>
        v > 0 ? v.ToString("0.###", CultureInfo.InvariantCulture) : "";

    private static double Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var t = text.Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : 0;
    }
}
