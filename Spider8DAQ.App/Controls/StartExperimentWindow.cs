using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Spider8DAQ.App.ViewModels;
using Spider8DAQ.Core;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Sensors;
using Spider8DAQ.Core.Specimens;

namespace Spider8DAQ.App.Controls;

/// <summary>
/// Dialog «Start experiment» — meta pentru raport.
/// Ordine: Sample/Probă → Tip experiment → panouri adaptive (contur etc.) → rest.
/// Tipul <see cref="ExperimentTypes.CylinderContour"/> afișează expanderul Contur și subliniază Ø.
/// </summary>
public sealed class StartExperimentWindow : Window
{
    public sealed class SensorPickItem
    {
        public SensorDefinition Sensor { get; init; } = null!;
        public string Display =>
            string.IsNullOrWhiteSpace(Sensor.Code)
                ? $"{Sensor.Name} [{Sensor.Category}]"
                : $"{Sensor.Code} — {Sensor.Name} [{Sensor.Category}]";
    }

    private readonly TextBox _projectBox;
    private readonly TextBox _operatorBox;
    private readonly TextBox _sampleBox;
    private readonly TextBox _lengthBox;
    private readonly TextBox _widthBox;
    private readonly TextBox _thicknessBox;
    private readonly TextBox _diameterBox;
    private readonly TextBox _massBox;
    private readonly TextBlock _dimensionsHint;
    private readonly ComboBox _typeBox;
    private readonly ComboBox _presetBox;
    private readonly TextBox _locationBox;
    private readonly TextBox _commentBox;
    private readonly TextBox _rateBox;
    private readonly TextBlock _startHint;
    private TextBlock _photoHint = null!;
    private Image _photoPreview = null!;
    private TextBox _beforeNotesBox = null!;
    private readonly ListBox _sensorList;
    private readonly CheckBox _applySensorsCheck;
    private readonly ObservableCollection<SensorPickItem> _sensorItems = new();
    private readonly IReadOnlyList<SensorDefinition> _allSensors;
    private readonly int _channelCount;
    private readonly IReadOnlyList<CylinderContourConfig.ChannelHint> _channelHints;
    private string _montagePhotoPath = "";
    private DateTime? _beforeCapturedAt;
    private bool _contourAutoMapped;
    private TextBlock? _contourAutoMapHint;

    private readonly Expander _contourExpander;
    private readonly TextBlock _contourLabel;
    private readonly TextBlock _typeAdaptiveHint;
    private readonly TextBlock _dimensionsRowLabel;
    private TextBlock _diameterFieldLabel = null!;
    private Border? _diameterHighlight;
    private ComboBox _contourCountBox = null!;
    private CheckBox _forceEnableCheck = null!;
    private ComboBox _forceChannelBox = null!;
    private ComboBox _strokeChannelBox = null!;
    private TextBlock _contourRadiusHint = null!;
    private StackPanel _sensorMapPanel = null!;
    private readonly List<(ComboBox Channel, TextBox Angle)> _sensorRows = new();
    private readonly bool _simulatorContourDemo;
    private StackPanel? _simAssignPanel;

    private SpecimenLibrary _specimenLibrary = SpecimenLibrary.Load();
    private SpecimenCard? _selectedSpecimen;
    private readonly string _initialSpecimenId;
    private bool _userClearedSpecimen;
    private bool _specimenRestoreAttempted;
    private TextBlock _specimenLabel = null!;
    private Border _specimenHost = null!;
    private TextBox _specimenSearchBox = null!;
    private TextBlock _specimenSearchPlaceholder = null!;
    private StackPanel _specimenCardsPanel = null!;
    private TextBlock _specimenHint = null!;
    private TextBox _eBox = null!;
    private TextBox _nuBox = null!;
    private TextBox _specimenNotesBox = null!;
    private StackPanel _saltNotesRow = null!;

    public bool Confirmed { get; private set; }
    public string ProjectNameValue { get; private set; } = "";
    public string OperatorValue { get; private set; } = "";
    public string SampleIdValue { get; private set; } = "";
    public double SampleLengthMmValue { get; private set; }
    public double SampleWidthMmValue { get; private set; }
    public double SampleThicknessMmValue { get; private set; }
    public double SampleDiameterMmValue { get; private set; }
    public double SampleAreaMm2Value { get; private set; }
    public double SampleMassGValue { get; private set; }
    public string SampleDimensionsSummaryValue { get; private set; } = "";
    public string ExperimentTypeValue { get; private set; } = "";
    public ExperimentPreset? SelectedPreset { get; private set; }
    public string LocationValue { get; private set; } = "";
    public string CommentValue { get; private set; } = "";
    public int SampleRateHzValue { get; private set; } = 50;
    public int EstimatedDurationMinutesValue { get; private set; }
    public DateTime ExperimentStartLocalValue { get; private set; } = DateTime.Now;
    public IReadOnlyList<SensorDefinition> PlannedSensors { get; private set; } = Array.Empty<SensorDefinition>();
    public bool ApplySensorsToChannels { get; private set; }
    public string MontagePhotoPathValue => _montagePhotoPath;
    public string MontageBeforeNotesValue { get; private set; } = "";
    public DateTime? MontageBeforeCapturedAtValue => _beforeCapturedAt;
    public CylinderContourConfig? ContourConfigValue { get; private set; }
    /// <summary>True when Simulator + Contur + 8 senzori — random 8/1/3 mm ramp on Start.</summary>
    public bool ContourSimDemoEnabled { get; private set; }
    public SpecimenCard? SelectedSpecimenCard { get; private set; }
    public double SpecimenYoungGPaValue { get; private set; }
    public double SpecimenPoissonNuValue { get; private set; }
    public string SpecimenNotesValue { get; private set; } = "";

    public StartExperimentWindow(
        string projectName,
        string operatorName,
        string sampleId,
        string location,
        string comment,
        int sampleRateHz,
        string? experimentType,
        ExperimentPreset? selectedPreset,
        IReadOnlyList<ExperimentPreset> presets,
        IReadOnlyList<SensorDefinition> sensors,
        string? montagePhotoPath = null,
        string? montageBeforeNotes = null,
        DateTime? montageBeforeCapturedAt = null,
        double sampleLengthMm = 0,
        double sampleWidthMm = 0,
        double sampleThicknessMm = 0,
        double sampleDiameterMm = 0,
        double sampleMassG = 0,
        CylinderContourConfig? initialContour = null,
        int channelCount = 8,
        IReadOnlyList<CylinderContourConfig.ChannelHint>? channelHints = null,
        string? initialSpecimenId = null,
        double specimenYoungGPa = 0,
        double specimenPoissonNu = 0,
        string? specimenNotes = null,
        bool simulatorContourDemo = false)
    {
        _allSensors = sensors;
        _channelCount = Math.Clamp(channelCount <= 0 ? 8 : channelCount, 1, 32);
        _channelHints = channelHints ?? Array.Empty<CylinderContourConfig.ChannelHint>();
        _montagePhotoPath = montagePhotoPath ?? "";
        _beforeCapturedAt = montageBeforeCapturedAt;
        _initialSpecimenId = initialSpecimenId ?? "";
        _simulatorContourDemo = simulatorContourDemo;

        Title = "Start experiment — UPET AcqLab";
        Width = 700;
        Height = 900;
        MinWidth = 560;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xEA));
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(16) };

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = "Configurare experiment",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6))
        });
        header.Children.Add(new TextBlock
        {
            Text = "Datele completează antetul rapoartelor. Dimensiunile / greutatea probei sunt opționale (ca poza montaj).",
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
            Margin = new Thickness(0, 12, 0, 0)
        };
        var ok = new Button
        {
            Content = "Start experiment",
            Padding = new Thickness(16, 8, 16, 8),
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

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var form = new Grid { Margin = new Thickness(0, 0, 4, 0) };
        for (var i = 0; i < 16; i++)
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _projectBox = AddLabeledText(form, 0, "Nume proiect", projectName);
        _operatorBox = AddLabeledText(form, 1, "Operator", operatorName);
        _sampleBox = AddLabeledText(form, 2, "Sample / Probă", sampleId);

        // Tip experiment — imediat sub Sample/Probă; panourile adaptive urmează.
        _typeBox = new ComboBox { Margin = new Thickness(0, 0, 0, 8), MinHeight = 28, IsEditable = true };
        foreach (var t in BuildTypeOptions())
            _typeBox.Items.Add(t);
        if (!string.IsNullOrWhiteSpace(experimentType) && !_typeBox.Items.Contains(experimentType))
            _typeBox.Items.Insert(0, experimentType);
        _typeBox.SelectedItem = !string.IsNullOrWhiteSpace(experimentType)
            ? experimentType
            : (_typeBox.Items.Count > 0 ? _typeBox.Items[0] : null);
        _typeBox.SelectionChanged += (_, _) => OnExperimentTypeChanged();
        _typeBox.LostFocus += (_, _) => OnExperimentTypeChanged();
        _typeBox.DropDownClosed += (_, _) => OnExperimentTypeChanged();
        AddLabeledControl(form, 3, "Tip experiment", _typeBox);

        _typeAdaptiveHint = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73))
        };
        Grid.SetRow(_typeAdaptiveHint, 4);
        Grid.SetColumn(_typeAdaptiveHint, 1);
        form.Children.Add(_typeAdaptiveHint);

        var specimenPanel = BuildSpecimenPanel(specimenYoungGPa, specimenPoissonNu, specimenNotes);
        _specimenLabel = new TextBlock
        {
            Text = "Epruvetă (opțional)",
            VerticalAlignment = VerticalAlignment.Top,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 8, 8)
        };
        Grid.SetRow(_specimenLabel, 5);
        Grid.SetColumn(_specimenLabel, 0);
        form.Children.Add(_specimenLabel);
        Grid.SetRow(specimenPanel, 5);
        Grid.SetColumn(specimenPanel, 1);
        form.Children.Add(specimenPanel);

        // Dimensiuni create înainte de expander (R₀ citește Ø), dar pe grid după Contur.
        _lengthBox = MakeDimBox(sampleLengthMm, "Lungime [mm] — opțional");
        _widthBox = MakeDimBox(sampleWidthMm, "Lățime [mm] — opțional");
        _thicknessBox = MakeDimBox(sampleThicknessMm, "Grosime [mm] — opțional");
        _diameterBox = MakeDimBox(sampleDiameterMm, "Diametru [mm] — necesar pentru contur (R₀=Ø/2)");
        _massBox = MakeDimBox(sampleMassG, "Greutate [g] — opțional");

        _contourExpander = BuildContourExpander(initialContour, sampleDiameterMm);
        _contourLabel = new TextBlock
        {
            Text = "Contur cilindru",
            VerticalAlignment = VerticalAlignment.Top,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 4, 8, 8)
        };
        Grid.SetRow(_contourLabel, 6);
        Grid.SetColumn(_contourLabel, 0);
        form.Children.Add(_contourLabel);
        Grid.SetRow(_contourExpander, 6);
        Grid.SetColumn(_contourExpander, 1);
        form.Children.Add(_contourExpander);

        var dimPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var dimRow = new WrapPanel();
        dimRow.Children.Add(LabeledDim("Lungime [mm]", _lengthBox));
        dimRow.Children.Add(LabeledDim("Lățime [mm]", _widthBox));
        dimRow.Children.Add(LabeledDim("Grosime [mm]", _thicknessBox));
        dimRow.Children.Add(BuildDiameterDimField());
        dimRow.Children.Add(LabeledDim("Greutate [g]", _massBox));
        dimPanel.Children.Add(dimRow);
        _dimensionsHint = new TextBlock
        {
            Text = "Opțional — lăsați gol dacă nu măsurați. Prismă: Lungime × Lățime × Grosime; cilindru: Diametru (A=πØ²/4). Greutate în g.",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        dimPanel.Children.Add(_dimensionsHint);
        void OnDimChanged(object s, TextChangedEventArgs e)
        {
            RefreshDimensionsHint();
            RefreshContourRadiusHint();
            RefreshDiameterEmphasis();
        }
        _lengthBox.TextChanged += OnDimChanged;
        _widthBox.TextChanged += OnDimChanged;
        _thicknessBox.TextChanged += OnDimChanged;
        _diameterBox.TextChanged += OnDimChanged;
        _massBox.TextChanged += OnDimChanged;
        _dimensionsRowLabel = AddLabeledControlReturningLabel(
            form, 7, "Dimensiuni / greutate\n(opțional)", dimPanel);

        _presetBox = new ComboBox
        {
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 28,
            DisplayMemberPath = "Name"
        };
        foreach (var p in presets)
            _presetBox.Items.Add(p);
        if (selectedPreset is not null)
            _presetBox.SelectedItem = presets.FirstOrDefault(p => p.Id == selectedPreset.Id) ?? selectedPreset;
        else if (_presetBox.Items.Count > 0)
            _presetBox.SelectedIndex = 0;
        AddLabeledControl(form, 8, "Preset vizualizare", _presetBox);

        _locationBox = AddLabeledText(form, 9, "Locație / banc",
            string.IsNullOrWhiteSpace(location)
                ? "Lab UPET — presa / stand"
                : location);

        _commentBox = new TextBox
        {
            Text = comment ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 56,
            Margin = new Thickness(0, 0, 0, 8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        AddLabeledControl(form, 10, "Comentariu / obiectiv", _commentBox);

        _rateBox = AddLabeledText(form, 11, "Rată [Hz]",
            sampleRateHz > 0 ? sampleRateHz.ToString(CultureInfo.InvariantCulture) : "50");

        _startHint = new TextBlock
        {
            Text = $"Data/ora start (la confirmare): {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 11
        };
        Grid.SetRow(_startHint, 12);
        Grid.SetColumn(_startHint, 1);
        form.Children.Add(_startHint);

        var photoPanel = BuildPhotoPanel(montageBeforeNotes);
        AddLabeledControl(form, 13, "Poză/schiță montaj\n(înainte, opțional)", photoPanel);
        if (!string.IsNullOrWhiteSpace(_montagePhotoPath))
            ShowPhotoPreview(_montagePhotoPath);

        _sensorList = new ListBox
        {
            Height = 120,
            Margin = new Thickness(0, 0, 0, 6),
            SelectionMode = SelectionMode.Extended,
            DisplayMemberPath = nameof(SensorPickItem.Display)
        };
        _sensorList.ItemsSource = _sensorItems;
        AddLabeledControl(form, 14, "Senzori planificați\n(Ctrl+click)", _sensorList);

        _applySensorsCheck = new CheckBox
        {
            Content = "Aplică senzorii selectați pe canalele Enabled (în ordine CH)",
            IsChecked = true,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(_applySensorsCheck, 15);
        Grid.SetColumn(_applySensorsCheck, 1);
        form.Children.Add(_applySensorsCheck);

        OnExperimentTypeChanged();

        scroll.Content = form;
        root.Children.Add(scroll);
        Content = root;

        RefreshSensorList();
        RestoreSpecimenSelection(fillDimensions: false);
        UpdateSpecimenSearchPlaceholder();
    }

    private UIElement BuildSpecimenPanel(double youngGPa, double poissonNu, string? notes)
    {
        var host = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xA8, 0xB0, 0xD0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(0xEE, 0xF0, 0xFA)),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };
        _specimenHost = host;

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "Opțional — lasă gol sau caută: sare, granit, S355. Cardul completează L0, Ø, E, ν și pachetul. Nu blochează Start.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var newBtn = new Button
        {
            Content = "Epruvetă nouă",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(8, 0, 0, 0),
            FontWeight = FontWeights.SemiBold
        };
        newBtn.Click += (_, _) => CreateCustomSpecimen();
        DockPanel.SetDock(newBtn, Dock.Right);
        var clearBtn = new Button
        {
            Content = "Fără epruvetă",
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(8, 0, 0, 0)
        };
        clearBtn.Click += (_, _) => SelectSpecimen(null, fillDimensions: false);
        DockPanel.SetDock(clearBtn, Dock.Right);
        searchRow.Children.Add(newBtn);
        searchRow.Children.Add(clearBtn);
        var searchHost = new Grid();
        _specimenSearchBox = new TextBox
        {
            MinHeight = 28,
            Padding = new Thickness(6, 4, 6, 4),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            ToolTip = "lasă gol sau caută: sare, granit, S355"
        };
        _specimenSearchPlaceholder = new TextBlock
        {
            Text = "lasă gol sau caută: sare, granit, S355",
            IsHitTestVisible = false,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8A, 0x93, 0x9E)),
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var searchBg = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xCE, 0xD6)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Child = searchHost
        };
        searchHost.Children.Add(_specimenSearchBox);
        searchHost.Children.Add(_specimenSearchPlaceholder);
        _specimenSearchBox.TextChanged += (_, _) =>
        {
            UpdateSpecimenSearchPlaceholder();
            RebuildSpecimenCards();
        };
        searchRow.Children.Add(searchBg);
        body.Children.Add(searchRow);

        var scroll = new ScrollViewer
        {
            MaxHeight = 168,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 6)
        };
        _specimenCardsPanel = new StackPanel();
        scroll.Content = _specimenCardsPanel;
        body.Children.Add(scroll);

        var evRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        _eBox = MakeDimBox(youngGPa, "Modul Young E [GPa] — indicativ, confirmați");
        _nuBox = MakeDimBox(poissonNu, "Coeficient Poisson ν — indicativ, confirmați");
        evRow.Children.Add(LabeledDim("E [GPa]", _eBox));
        evRow.Children.Add(LabeledDim("ν", _nuBox));
        body.Children.Add(evRow);

        _saltNotesRow = new StackPanel { Margin = new Thickness(0, 0, 0, 4), Visibility = Visibility.Collapsed };
        _saltNotesRow.Children.Add(new TextBlock
        {
            Text = "Note fluaj (T, umiditate) — opțional; fit Norton nu e inclus",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 2)
        });
        _specimenNotesBox = new TextBox
        {
            Text = notes ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 40,
            Padding = new Thickness(6, 4, 6, 4)
        };
        _saltNotesRow.Children.Add(_specimenNotesBox);
        body.Children.Add(_saltNotesRow);

        _specimenHint = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Text = SpecimenCatalog.IndicativeDisclaimerRo
        };
        body.Children.Add(_specimenHint);

        host.Child = body;
        RebuildSpecimenCards();
        return host;
    }

    private void RefreshSpecimenPanel()
    {
        if (_specimenHost is null || _specimenLabel is null) return;
        var show = ExperimentTypes.IsCompression(CurrentExperimentType());
        var vis = show ? Visibility.Visible : Visibility.Collapsed;
        _specimenHost.Visibility = vis;
        _specimenLabel.Visibility = vis;
        if (!show)
        {
            // Keep in-memory selection (including explicit none) when hiding the panel.
            return;
        }

        if (!_specimenRestoreAttempted && !_userClearedSpecimen)
            RestoreSpecimenSelection(fillDimensions: false);
        else
        {
            RebuildSpecimenCards();
            UpdateSpecimenHint();
        }
    }

    private void RestoreSpecimenSelection(bool fillDimensions)
    {
        if (_userClearedSpecimen)
        {
            RebuildSpecimenCards();
            UpdateSpecimenHint();
            return;
        }
        if (!ExperimentTypes.IsCompression(CurrentExperimentType())) return;
        if (_specimenRestoreAttempted) return;
        _specimenRestoreAttempted = true;

        // Empty LastSpecimenId is a valid "none" — do not fall back to the first catalog card.
        var id = !string.IsNullOrWhiteSpace(_initialSpecimenId)
            ? _initialSpecimenId
            : _specimenLibrary.LastSpecimenId;
        var card = _specimenLibrary.FindById(id);
        if (card is not null)
            SelectSpecimen(card, fillDimensions);
        else
        {
            RebuildSpecimenCards();
            UpdateSpecimenHint();
        }
    }

    private void UpdateSpecimenSearchPlaceholder()
    {
        if (_specimenSearchPlaceholder is null) return;
        _specimenSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(_specimenSearchBox?.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RebuildSpecimenCards()
    {
        if (_specimenCardsPanel is null) return;
        _specimenCardsPanel.Children.Clear();
        if (_selectedSpecimen is null)
        {
            _specimenCardsPanel.Children.Add(new TextBlock
            {
                Text = "Nimic selectat (opțional) — Start funcționează și fără epruvetă.",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x84, 0x4A)),
                Margin = new Thickness(4, 0, 4, 6)
            });
        }
        var q = _specimenSearchBox?.Text ?? "";
        var hits = _specimenLibrary.Search(q);
        var n = 0;
        foreach (var card in hits)
        {
            if (n++ >= 40) break;
            _specimenCardsPanel.Children.Add(BuildSpecimenCardUi(card));
        }

        if (hits.Count == 0)
        {
            _specimenCardsPanel.Children.Add(new TextBlock
            {
                Text = "Nicio epruvetă nu se potrivește. Încercați sare, granit, S355 sau Epruvetă nouă.",
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
                Margin = new Thickness(4)
            });
        }
    }

    private UIElement BuildSpecimenCardUi(SpecimenCard card)
    {
        var selected = _selectedSpecimen is not null
                       && string.Equals(_selectedSpecimen.Id, card.Id, StringComparison.OrdinalIgnoreCase);
        var accent = card.Class switch
        {
            SpecimenClasses.Metal => Color.FromRgb(0x00, 0x72, 0xC6),
            SpecimenClasses.Roca => Color.FromRgb(0x6D, 0x4C, 0x41),
            SpecimenClasses.Sare => Color.FromRgb(0xC4, 0xA3, 0x35),
            SpecimenClasses.Beton => Color.FromRgb(0x5D, 0x6D, 0x7E),
            _ => Color.FromRgb(0x1E, 0x84, 0x4A)
        };
        var border = new Border
        {
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = new SolidColorBrush(selected ? accent : Color.FromRgb(0xC5, 0xCE, 0xD6)),
            Background = Brushes.White,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = System.Windows.Input.Cursors.Hand,
            Tag = card
        };
        var col = new StackPanel();
        var title = new DockPanel();
        var cls = new TextBlock
        {
            Text = card.Class + (card.IsCustom ? " · personalizat" : ""),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(accent),
            Margin = new Thickness(8, 0, 0, 0)
        };
        DockPanel.SetDock(cls, Dock.Right);
        title.Children.Add(cls);
        title.Children.Add(new TextBlock
        {
            Text = card.NameRo,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        col.Children.Add(title);
        col.Children.Add(new TextBlock
        {
            Text = card.BuildCardSubtitle(),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 2, 0, 0)
        });
        border.Child = col;
        border.MouseLeftButtonUp += (_, _) => SelectSpecimen(card, fillDimensions: true);
        return border;
    }

    private void SelectSpecimen(SpecimenCard? card, bool fillDimensions)
    {
        if (card is null)
        {
            _userClearedSpecimen = true;
            _selectedSpecimen = null;
            _specimenLibrary.RememberLast("");
            UpdateSpecimenHint();
            RebuildSpecimenCards();
            RefreshDimensionsHint();
            RefreshContourRadiusHint();
            RefreshDiameterEmphasis();
            return;
        }

        _userClearedSpecimen = false;
        _selectedSpecimen = card;
        if (fillDimensions)
        {
            if (card.L0Mm > 0 && _lengthBox is not null)
                _lengthBox.Text = card.L0Mm.ToString("0.###", CultureInfo.InvariantCulture);
            if (card.D0Mm > 0 && _diameterBox is not null)
                _diameterBox.Text = card.D0Mm.ToString("0.###", CultureInfo.InvariantCulture);
            _specimenLibrary.RememberLast(card.Id);
        }
        // Never overwrite L0/Ø/E/ν the operator already typed unless they clicked a card
        // (fillDimensions) or the field is still empty.
        if (_eBox is not null && (fillDimensions || ParseOptional(_eBox.Text) <= 0) && card.EGPa > 0)
            _eBox.Text = card.EGPa.ToString("0.###", CultureInfo.InvariantCulture);
        if (_nuBox is not null && (fillDimensions || ParseOptional(_nuBox.Text) <= 0) && card.Nu > 0)
            _nuBox.Text = card.Nu.ToString("0.###", CultureInfo.InvariantCulture);
        if (_specimenNotesBox is not null && string.IsNullOrWhiteSpace(_specimenNotesBox.Text) &&
            !string.IsNullOrWhiteSpace(card.Notes))
            _specimenNotesBox.Text = card.Notes;

        UpdateSpecimenHint();
        RebuildSpecimenCards();
        RefreshDimensionsHint();
        RefreshContourRadiusHint();
        RefreshDiameterEmphasis();
    }

    private void UpdateSpecimenHint()
    {
        if (_specimenHint is null) return;
        if (_saltNotesRow is not null)
        {
            _saltNotesRow.Visibility =
                _selectedSpecimen?.FormulaPack == FormulaPacks.SaltCreep
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        if (_selectedSpecimen is null)
        {
            _specimenHint.Text =
                "Nicio epruvetă selectată (opțional) — Start funcționează și fără. " +
                "lasă gol sau caută: sare, granit, S355.";
            return;
        }

        var pack = FormulaPacks.ReportMeaningRo(_selectedSpecimen.FormulaPack);
        _specimenHint.Text =
            _selectedSpecimen.BuildSummaryLine() + " — " + pack + " " +
            SpecimenCatalog.IndicativeDisclaimerRo;
    }

    private void CreateCustomSpecimen()
    {
        var dlg = new NewSpecimenWindow(_selectedSpecimen)
        {
            Owner = this
        };
        if (dlg.ShowDialog() != true || !dlg.Confirmed || dlg.Card is null)
            return;
        _specimenLibrary.AddOrUpdateCustom(dlg.Card);
        _specimenLibrary.RememberLast(dlg.Card.Id);
        if (_specimenSearchBox is not null)
            _specimenSearchBox.Text = dlg.Card.NameRo;
        SelectSpecimen(dlg.Card, fillDimensions: true);
    }

    private StackPanel BuildPhotoPanel(string? montageBeforeNotes)
    {
        var photoPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        var photoBtns = new WrapPanel();
        var camBtn = new Button
        {
            Content = "Cameră — montaj înainte…",
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 4),
            FontWeight = FontWeights.SemiBold
        };
        camBtn.Click += (_, _) => OpenCameraCapture();
        var clearPhoto = new Button
        {
            Content = "Șterge foto",
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 0, 4)
        };
        clearPhoto.Click += (_, _) =>
        {
            _montagePhotoPath = "";
            _beforeCapturedAt = null;
            _photoPreview.Source = null;
            _photoHint.Text = "Opțional — fără poză puteți porni experimentul.";
        };
        photoBtns.Children.Add(camBtn);
        photoBtns.Children.Add(clearPhoto);
        photoPanel.Children.Add(photoBtns);
        _photoHint = new TextBlock
        {
            Text = BuildBeforePhotoHint(),
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 2, 0, 4),
            TextWrapping = TextWrapping.Wrap
        };
        photoPanel.Children.Add(_photoHint);
        _photoPreview = new Image
        {
            MaxHeight = 100,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        photoPanel.Children.Add(_photoPreview);
        photoPanel.Children.Add(new TextBlock
        {
            Text = "Observații montaj înainte (opțional)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 4),
            FontSize = 11
        });
        _beforeNotesBox = new TextBox
        {
            Text = montageBeforeNotes ?? "",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 48,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(6, 4, 6, 4)
        };
        photoPanel.Children.Add(_beforeNotesBox);
        return photoPanel;
    }

    private Expander BuildContourExpander(CylinderContourConfig? initial, double diameterMm)
    {
        var cfg = CloneConfig(initial, diameterMm, _channelCount, _simulatorContourDemo);

        var body = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        body.Children.Add(new TextBlock
        {
            Text = "Mapare senzori circumferențiali → canale Spider8 + unghiuri. R₀ = Ø/2 din dimensiuni. Valori în mm (scara canalului). La tip Contur: auto-map pe canale Rec/[mm] + unghiuri egale.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73)),
            Margin = new Thickness(0, 0, 0, 6)
        });

        _contourAutoMapHint = new TextBlock
        {
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6)),
            Margin = new Thickness(0, 0, 0, 6),
            Visibility = Visibility.Collapsed
        };
        body.Children.Add(_contourAutoMapHint);

        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        top.Children.Add(new TextBlock
        {
            Text = "Senzori",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 6, 0)
        });
        _contourCountBox = new ComboBox { Width = 64, MinHeight = 26, Margin = new Thickness(0, 0, 12, 4) };
        _contourCountBox.Items.Add(4);
        _contourCountBox.Items.Add(8);
        _contourCountBox.SelectedItem = cfg.SensorCount == 8 ? 8 : 4;
        top.Children.Add(_contourCountBox);

        _forceEnableCheck = new CheckBox
        {
            Content = "Canal forță",
            IsChecked = cfg.ForceChannelIndex is >= 0,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 4)
        };
        top.Children.Add(_forceEnableCheck);
        _forceChannelBox = MakeChannelCombo(cfg.ForceChannelIndex ?? 0);
        _forceChannelBox.IsEnabled = _forceEnableCheck.IsChecked == true;
        _forceChannelBox.Margin = new Thickness(0, 0, 12, 4);
        top.Children.Add(_forceChannelBox);
        _forceEnableCheck.Checked += (_, _) => _forceChannelBox.IsEnabled = true;
        _forceEnableCheck.Unchecked += (_, _) => _forceChannelBox.IsEnabled = false;

        top.Children.Add(new TextBlock
        {
            Text = "Cursă presa",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 6, 4)
        });
        _strokeChannelBox = MakeChannelCombo(
            cfg.StrokeChannelIndex >= 0
                ? cfg.StrokeChannelIndex
                : Math.Min(cfg.SensorCount, _channelCount - 1));
        _strokeChannelBox.Margin = new Thickness(0, 0, 0, 4);
        top.Children.Add(_strokeChannelBox);
        body.Children.Add(top);

        _contourRadiusHint = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6)),
            Margin = new Thickness(0, 0, 0, 6),
            TextWrapping = TextWrapping.Wrap
        };
        body.Children.Add(_contourRadiusHint);

        _sensorMapPanel = new StackPanel();
        body.Children.Add(_sensorMapPanel);
        body.Children.Add(BuildSimAssignmentPanel());

        _contourCountBox.SelectionChanged += (_, _) =>
        {
            if (ExperimentTypes.IsCylinderContour(CurrentExperimentType()))
                ApplyContourAutoMap(force: true);
            else
                RebuildSensorMapRows(ReadPartialConfig());
            RefreshSimAssignmentPanel();
        };
        RebuildSensorMapRows(cfg);
        RefreshContourRadiusHint();
        RefreshSimAssignmentPanel();

        return new Expander
        {
            Header = "Configurare contur (4/8 senzori, forță, cursă, unghiuri) — auto-map la tip Contur",
            IsExpanded = true,
            Margin = new Thickness(0, 0, 0, 8),
            Content = body
        };
    }

    private static CylinderContourConfig CloneConfig(
        CylinderContourConfig? initial,
        double diameterMm,
        int channelCount,
        bool simulatorContourDemo)
    {
        var fallbackN = simulatorContourDemo && initial is null ? 8 : 4;
        var cfg = initial is null
            ? CylinderContourConfig.CreateDefault(fallbackN, diameterMm, channelCount)
            : new CylinderContourConfig
            {
                SensorCount = initial.SensorCount == 8 ? 8 : 4,
                SensorChannelIndices = initial.SensorChannelIndices.ToList(),
                SensorAnglesDeg = initial.SensorAnglesDeg.ToList(),
                ForceChannelIndex = initial.ForceChannelIndex,
                StrokeChannelIndex = initial.StrokeChannelIndex,
                InitialRadiusMm = initial.InitialRadiusMm
            };
        cfg.EnsureShape(cfg.SensorCount);
        if (channelCount > 0 && cfg.HasOutOfRangeChannels(channelCount))
            cfg.MapToAvailableChannels(channelCount);
        if (cfg.InitialRadiusMm <= 0 && diameterMm > 0)
            cfg.InitialRadiusMm = diameterMm / 2.0;
        return cfg;
    }

    private ComboBox MakeChannelCombo(int selected)
    {
        var box = new ComboBox { Width = 72, MinHeight = 26 };
        for (var i = 0; i < _channelCount; i++)
            box.Items.Add($"CH{i}");
        box.SelectedIndex = Math.Clamp(selected, 0, _channelCount - 1);
        return box;
    }

    private void RebuildSensorMapRows(CylinderContourConfig seed)
    {
        var n = _contourCountBox.SelectedItem is int c && c == 8 ? 8 : 4;
        seed.EnsureShape(n);
        var angles = seed.EffectiveAnglesDeg();
        _sensorMapPanel.Children.Clear();
        _sensorRows.Clear();

        _sensorMapPanel.Children.Add(new TextBlock
        {
            Text = "S# → canal · unghi [°]",
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        });

        for (var i = 0; i < n; i++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            row.Children.Add(new TextBlock
            {
                Text = $"S{i + 1}",
                Width = 28,
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold
            });
            var ch = MakeChannelCombo(i < seed.SensorChannelIndices.Count
                ? seed.SensorChannelIndices[i]
                : i);
            ch.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(ch);
            row.Children.Add(new TextBlock
            {
                Text = "∠",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
            var ang = new TextBox
            {
                Width = 56,
                Text = (i < angles.Count ? angles[i] : CylinderContourConfig.DefaultAngleDeg(n, i))
                    .ToString("0.###", CultureInfo.InvariantCulture),
                Padding = new Thickness(4, 2, 4, 2),
                ToolTip = "Unghi [°] față de axa +X (vedere de sus)"
            };
            row.Children.Add(ang);
            _sensorMapPanel.Children.Add(row);
            _sensorRows.Add((ch, ang));
        }
    }

    private UIElement BuildSimAssignmentPanel()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
        _simAssignPanel = panel;

        panel.Children.Add(new TextBlock
        {
            Text = "Simulator — ținte u [mm] (același ca graficul Contur)",
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        });
        panel.Children.Add(new TextBlock
        {
            Text =
                "La fiecare Start: 4 senzori aleși aleatoriu → 8 mm, 1 din rest → 1 mm, 3 → 3 mm. " +
                "Rampă 0 → țintă în exact 10 s, apoi stop. Repartizarea (read-only) apare după Start. " +
                "L0/Ø rămân cele din dimensiuni — nu se schimbă.",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6)),
            Margin = new Thickness(0, 0, 0, 2)
        });
        return panel;
    }

    private void RefreshSimAssignmentPanel()
    {
        if (_simAssignPanel is null) return;
        var show = _simulatorContourDemo
                   && ExperimentTypes.IsCylinderContour(CurrentExperimentType())
                   && _contourCountBox?.SelectedItem is int n && n == 8;
        _simAssignPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsSimDemoPanelActive() =>
        _simulatorContourDemo
        && ExperimentTypes.IsCylinderContour(CurrentExperimentType())
        && _contourCountBox?.SelectedItem is int n && n == 8
        && _simAssignPanel is { Visibility: Visibility.Visible };

    private CylinderContourConfig ReadPartialConfig()
    {
        var n = _contourCountBox.SelectedItem is int c && c == 8 ? 8 : 4;
        var cfg = new CylinderContourConfig { SensorCount = n };
        foreach (var (ch, ang) in _sensorRows)
        {
            cfg.SensorChannelIndices.Add(Math.Max(0, ch.SelectedIndex));
            var t = (ang.Text ?? "").Trim().Replace(',', '.');
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                a = double.NaN;
            cfg.SensorAnglesDeg.Add(a);
        }
        cfg.EnsureShape(n);
        return cfg;
    }

    private UIElement BuildDiameterDimField()
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 4) };
        _diameterFieldLabel = new TextBlock
        {
            Text = "Diametru [mm]",
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 6, 0)
        };
        sp.Children.Add(_diameterFieldLabel);
        _diameterHighlight = new Border
        {
            Child = _diameterBox,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(3)
        };
        sp.Children.Add(_diameterHighlight);
        return sp;
    }

    private void OnExperimentTypeChanged()
    {
        RefreshSensorList();
        RefreshTypeAdaptivePanels();
        RefreshSpecimenPanel();
        RefreshDimensionsHint();
        RefreshContourRadiusHint();
        RefreshDiameterEmphasis();
        RefreshSimAssignmentPanel();
        if (ExperimentTypes.IsCylinderContour(CurrentExperimentType()))
            ApplyContourAutoMap(force: !_contourAutoMapped);
        else
            _contourAutoMapped = false;
    }

    /// <summary>
    /// When Contur type is selected: map S1…Sn → Rec-enabled (or first N mm) channels
    /// and even angles 0/45/… — less manual editing in Start Exp.
    /// </summary>
    private void ApplyContourAutoMap(bool force)
    {
        if (!ExperimentTypes.IsCylinderContour(CurrentExperimentType()))
            return;
        if (_contourAutoMapped && !force)
            return;

        var n = _contourCountBox?.SelectedItem is int c && c == 8 ? 8 : 4;
        var d = ParseOptional(_diameterBox.Text);
        var mapped = CylinderContourConfig.AutoMapFromChannelHints(
            n, d, _channelHints, _channelCount);

        if (_contourCountBox is not null && !Equals(_contourCountBox.SelectedItem, mapped.SensorCount))
            _contourCountBox.SelectedItem = mapped.SensorCount;

        if (_forceEnableCheck is not null)
        {
            var hasForce = mapped.ForceChannelIndex is >= 0;
            _forceEnableCheck.IsChecked = hasForce;
            if (_forceChannelBox is not null)
            {
                _forceChannelBox.IsEnabled = hasForce;
                if (hasForce && mapped.ForceChannelIndex is int fi)
                    _forceChannelBox.SelectedIndex = Math.Clamp(fi, 0, _channelCount - 1);
            }
        }
        if (_strokeChannelBox is not null && mapped.StrokeChannelIndex >= 0)
            _strokeChannelBox.SelectedIndex = Math.Clamp(mapped.StrokeChannelIndex, 0, _channelCount - 1);

        RebuildSensorMapRows(mapped);
        if (_contourAutoMapHint is not null)
        {
            _contourAutoMapHint.Text = mapped.ToAutoMapSummaryRo();
            _contourAutoMapHint.Visibility = Visibility.Visible;
        }
        _contourAutoMapped = true;
        RefreshContourRadiusHint();
        RefreshSimAssignmentPanel();
    }

    private void RefreshTypeAdaptivePanels()
    {
        var type = CurrentExperimentType();
        var isContour = ExperimentTypes.IsCylinderContour(type);
        var vis = isContour ? Visibility.Visible : Visibility.Collapsed;
        _contourExpander.Visibility = vis;
        _contourLabel.Visibility = vis;
        if (isContour)
            _contourExpander.IsExpanded = true;

        if (isContour)
        {
            _typeAdaptiveHint.Visibility = Visibility.Visible;
            _typeAdaptiveHint.Text =
                "Tip contur: completați Ø (diametru) — necesar pentru R₀ — și maparea senzorilor circumferențiali mai jos."
                + (_simulatorContourDemo
                    ? " Simulator: la Start, 4×8 mm + 1×1 mm + 3×3 mm aleatoriu, rampă 10 s."
                    : "");
            _typeAdaptiveHint.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6));
            _typeAdaptiveHint.FontWeight = FontWeights.SemiBold;
            if (_dimensionsRowLabel is not null)
                _dimensionsRowLabel.Text = "Dimensiuni / greutate\n(Ø necesar contur)";
        }
        else if (type.StartsWith("Mixt", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(type))
        {
            _typeAdaptiveHint.Visibility = Visibility.Visible;
            _typeAdaptiveHint.Text =
                "Mixt / multi-senzor: toate categoriile din librărie; dimensiunile rămân opționale.";
            _typeAdaptiveHint.Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73));
            _typeAdaptiveHint.FontWeight = FontWeights.Normal;
            if (_dimensionsRowLabel is not null)
                _dimensionsRowLabel.Text = "Dimensiuni / greutate\n(opțional)";
        }
        else if (SensorCategories.All.Any(c => string.Equals(c, type, StringComparison.OrdinalIgnoreCase)))
        {
            _typeAdaptiveHint.Visibility = Visibility.Visible;
            _typeAdaptiveHint.Text =
                $"Categorie «{type}»: lista de senzori planificați e filtrată pe această categorie. Conturul cilindru este ascuns.";
            _typeAdaptiveHint.Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73));
            _typeAdaptiveHint.FontWeight = FontWeights.Normal;
            if (_dimensionsRowLabel is not null)
                _dimensionsRowLabel.Text = "Dimensiuni / greutate\n(opțional)";
        }
        else
        {
            _typeAdaptiveHint.Visibility = Visibility.Collapsed;
            _typeAdaptiveHint.Text = "";
            if (_dimensionsRowLabel is not null)
                _dimensionsRowLabel.Text = "Dimensiuni / greutate\n(opțional)";
        }

        RefreshSpecimenPanel();
        RefreshSimAssignmentPanel();
    }

    private void RefreshDiameterEmphasis()
    {
        if (_diameterFieldLabel is null || _diameterHighlight is null) return;
        var isContour = ExperimentTypes.IsCylinderContour(CurrentExperimentType());
        var hasDiameter = ParseOptional(_diameterBox.Text) > 0;
        if (isContour)
        {
            _diameterFieldLabel.Text = hasDiameter ? "Diametru Ø [mm]" : "Diametru Ø [mm] ★ necesar";
            _diameterFieldLabel.Foreground = hasDiameter
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6))
                : new SolidColorBrush(Color.FromRgb(0xB0, 0x3A, 0x2E));
            _diameterFieldLabel.FontWeight = FontWeights.Bold;
            _diameterHighlight.BorderThickness = new Thickness(1.5);
            _diameterHighlight.BorderBrush = hasDiameter
                ? new SolidColorBrush(Color.FromRgb(0x00, 0x72, 0xC6))
                : new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
            _diameterHighlight.Padding = new Thickness(2);
            _diameterBox.ToolTip = "Obligatoriu pentru contur cilindru — R₀ = Ø/2";
        }
        else
        {
            _diameterFieldLabel.Text = "Diametru [mm]";
            _diameterFieldLabel.Foreground = Brushes.Black;
            _diameterFieldLabel.FontWeight = FontWeights.SemiBold;
            _diameterHighlight.BorderThickness = new Thickness(0);
            _diameterHighlight.BorderBrush = null;
            _diameterHighlight.Padding = new Thickness(0);
            _diameterBox.ToolTip = "Diametru [mm] — opțional (cilindru)";
        }
    }

    private string CurrentExperimentType() =>
        (_typeBox.SelectedItem as string ?? _typeBox.Text ?? "").Trim();

    private void RefreshContourRadiusHint()
    {
        if (_contourRadiusHint is null) return;
        var d = ParseOptional(_diameterBox.Text);
        if (d > 0)
        {
            var r0 = d / 2.0;
            _contourRadiusHint.Text =
                $"R₀ = Ø/2 = {r0.ToString("0.###", CultureInfo.InvariantCulture)} mm (din diametru).";
        }
        else
        {
            _contourRadiusHint.Text =
                "R₀ nedeterminat — completați Diametru Ø [mm] (necesar pentru graficul contur).";
        }
    }

    private void RefreshDimensionsHint()
    {
        var L = ParseOptional(_lengthBox.Text);
        var W = ParseOptional(_widthBox.Text);
        var T = ParseOptional(_thicknessBox.Text);
        var D = ParseOptional(_diameterBox.Text);
        var m = ParseOptional(_massBox.Text);
        var summary = SampleDimensions.BuildSummary(L, W, T, D, massG: m);
        var isContour = ExperimentTypes.IsCylinderContour(CurrentExperimentType());
        if (isContour && D <= 0)
        {
            _dimensionsHint.Text =
                "★ Pentru «Compresiune cilindru – contur» diametrul Ø este necesar (R₀=Ø/2). " +
                "Lungime / lățime / grosime / greutate rămân opționale.";
            _dimensionsHint.Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0x3A, 0x2E));
            return;
        }

        _dimensionsHint.Foreground = new SolidColorBrush(Color.FromRgb(0x5A, 0x65, 0x73));
        if (!string.IsNullOrWhiteSpace(summary))
        {
            _dimensionsHint.Text = "Rezumat: " + summary;
            return;
        }

        _dimensionsHint.Text = isContour
            ? "Completați Diametru Ø [mm] pentru contur. Prismă: L×l×g opțional; greutate în g."
            : "Opțional — lăsați gol dacă nu măsurați. Prismă: Lungime × Lățime × Grosime; cilindru: Diametru (A=πØ²/4). Greutate în g.";
    }

    private static TextBox MakeDimBox(double value, string tip)
    {
        return new TextBox
        {
            Text = value > 0 ? value.ToString("0.###", CultureInfo.InvariantCulture) : "",
            Width = 68,
            Margin = new Thickness(0, 0, 8, 4),
            MinHeight = 28,
            Padding = new Thickness(6, 4, 6, 4),
            ToolTip = tip
        };
    }

    private static UIElement LabeledDim(string label, TextBox box)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 4) };
        sp.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 6, 0)
        });
        sp.Children.Add(box);
        return sp;
    }

    private static double ParseOptional(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var t = text.Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0
            ? v
            : 0;
    }

    private string BuildBeforePhotoHint()
    {
        if (string.IsNullOrWhiteSpace(_montagePhotoPath))
            return "Opțional — fără poză puteți porni experimentul.";
        var name = Path.GetFileName(_montagePhotoPath);
        return _beforeCapturedAt is { } t
            ? $"Înainte: {name} · {t:yyyy-MM-dd HH:mm:ss}"
            : $"Înainte: {name}";
    }

    private void OpenCameraCapture()
    {
        var stamp = DateTime.Now;
        var baseName = ExperimentFileNaming.BuildBaseName(
            _sampleBox.Text,
            stamp,
            ExperimentFileNaming.RoleBefore);
        var recordings = AppPaths.EnsureWritable(AppPaths.Recordings);
        var cam = new CameraCaptureWindow(
            string.IsNullOrWhiteSpace(_montagePhotoPath) ? null : _montagePhotoPath,
            title: "Montaj înainte — cameră",
            heading: "Poză / schiță montaj (înainte, opțional)",
            hint: "Documentați montajul înainte de măsurare dacă doriți. Fișier: " + baseName + ".jpg (lângă CSV).",
            saveDirectory: recordings,
            preferredBaseName: baseName)
        {
            Owner = this
        };
        if (cam.ShowDialog() == true && !string.IsNullOrWhiteSpace(cam.CapturedPath))
        {
            _montagePhotoPath = cam.CapturedPath!;
            _beforeCapturedAt = stamp;
            _photoHint.Text = BuildBeforePhotoHint();
            ShowPhotoPreview(_montagePhotoPath);
        }
    }

    private void ShowPhotoPreview(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _photoPreview.Source = null;
                return;
            }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            _photoPreview.Source = bmp;
        }
        catch
        {
            _photoPreview.Source = null;
        }
    }

    private static IEnumerable<string> BuildTypeOptions()
    {
        yield return "Mixt / multi-senzor";
        yield return ExperimentTypes.CylinderContour;
        foreach (var c in SensorCategories.All)
            yield return c;
    }

    private void RefreshSensorList()
    {
        var type = _typeBox.SelectedItem as string ?? _typeBox.Text ?? "";
        _sensorItems.Clear();
        IEnumerable<SensorDefinition> q = _allSensors;
        if (!string.IsNullOrWhiteSpace(type) &&
            !type.StartsWith("Mixt", StringComparison.OrdinalIgnoreCase) &&
            !ExperimentTypes.IsCylinderContour(type) &&
            SensorCategories.All.Any(c => string.Equals(c, type, StringComparison.OrdinalIgnoreCase)))
        {
            q = _allSensors.Where(s =>
                string.Equals(s.Category, type, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var s in q.OrderBy(s => s.Category).ThenBy(s => s.Code).ThenBy(s => s.Name).Take(200))
            _sensorItems.Add(new SensorPickItem { Sensor = s });
    }

    private static TextBox AddLabeledText(Grid form, int row, string label, string value)
    {
        var box = new TextBox
        {
            Text = value ?? "",
            Margin = new Thickness(0, 0, 0, 8),
            MinHeight = 28,
            Padding = new Thickness(6, 4, 6, 4)
        };
        AddLabeledControl(form, row, label, box);
        return box;
    }

    private static TextBlock AddLabeledControlReturningLabel(Grid form, int row, string label, UIElement control)
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
        return tb;
    }

    private static void AddLabeledControl(Grid form, int row, string label, UIElement control)
    {
        AddLabeledControlReturningLabel(form, row, label, control);
    }

    private CylinderContourConfig? ReadContourConfigOrNull()
    {
        var type = (_typeBox.SelectedItem as string ?? _typeBox.Text ?? "").Trim();
        if (!ExperimentTypes.IsCylinderContour(type))
            return null;

        var n = _contourCountBox.SelectedItem is int c && c == 8 ? 8 : 4;
        var cfg = new CylinderContourConfig
        {
            SensorCount = n,
            StrokeChannelIndex = Math.Max(0, _strokeChannelBox.SelectedIndex),
            ForceChannelIndex = _forceEnableCheck.IsChecked == true
                ? Math.Max(0, _forceChannelBox.SelectedIndex)
                : null,
            InitialRadiusMm = ParseOptional(_diameterBox.Text) / 2.0
        };

        var defaults = CylinderContourConfig.DefaultAnglesDeg(n);
        for (var i = 0; i < n; i++)
        {
            if (i < _sensorRows.Count)
            {
                var (ch, ang) = _sensorRows[i];
                cfg.SensorChannelIndices.Add(Math.Max(0, ch.SelectedIndex));
                var t = (ang.Text ?? "").Trim().Replace(',', '.');
                if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ||
                    double.IsNaN(a) || double.IsInfinity(a))
                    a = defaults[i];
                cfg.SensorAnglesDeg.Add(a);
            }
            else
            {
                cfg.SensorChannelIndices.Add(i);
                cfg.SensorAnglesDeg.Add(defaults[i]);
            }
        }

        return cfg;
    }

    private void Confirm()
    {
        ProjectNameValue = _projectBox.Text.Trim();
        OperatorValue = _operatorBox.Text.Trim();
        SampleIdValue = _sampleBox.Text.Trim();
        SampleLengthMmValue = ParseOptional(_lengthBox.Text);
        SampleWidthMmValue = ParseOptional(_widthBox.Text);
        SampleThicknessMmValue = ParseOptional(_thicknessBox.Text);
        SampleDiameterMmValue = ParseOptional(_diameterBox.Text);
        SampleMassGValue = ParseOptional(_massBox.Text);
        SampleAreaMm2Value = SampleDimensions.ComputeAreaMm2(
            SampleLengthMmValue, SampleWidthMmValue, SampleThicknessMmValue, SampleDiameterMmValue);
        SampleDimensionsSummaryValue = SampleDimensions.BuildSummary(
            SampleLengthMmValue, SampleWidthMmValue, SampleThicknessMmValue, SampleDiameterMmValue,
            SampleAreaMm2Value, SampleMassGValue);
        ExperimentTypeValue = (_typeBox.SelectedItem as string ?? _typeBox.Text ?? "").Trim();
        ContourConfigValue = ReadContourConfigOrNull();
        ContourSimDemoEnabled = IsSimDemoPanelActive();
        if (ExperimentTypes.IsCompression(ExperimentTypeValue) && _selectedSpecimen is not null)
        {
            SelectedSpecimenCard = _selectedSpecimen;
            SpecimenYoungGPaValue = ParseOptional(_eBox?.Text);
            SpecimenPoissonNuValue = ParseOptional(_nuBox?.Text);
            SpecimenNotesValue = _specimenNotesBox?.Text.Trim() ?? "";
            _specimenLibrary.RememberLast(_selectedSpecimen.Id);
        }
        else
        {
            SelectedSpecimenCard = null;
            SpecimenYoungGPaValue = 0;
            SpecimenPoissonNuValue = 0;
            SpecimenNotesValue = "";
            if (ExperimentTypes.IsCompression(ExperimentTypeValue))
                _specimenLibrary.RememberLast("");
        }
        SelectedPreset = _presetBox.SelectedItem as ExperimentPreset;
        LocationValue = _locationBox.Text.Trim();
        CommentValue = _commentBox.Text.Trim();
        if (!int.TryParse(_rateBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hz) || hz < 1)
            hz = 50;
        SampleRateHzValue = Math.Clamp(hz, 1, 9600);
        EstimatedDurationMinutesValue = 0;
        ExperimentStartLocalValue = DateTime.Now;
        PlannedSensors = _sensorList.SelectedItems.Cast<SensorPickItem>().Select(i => i.Sensor).ToList();
        ApplySensorsToChannels = _applySensorsCheck.IsChecked == true;
        MontageBeforeNotesValue = _beforeNotesBox.Text.Trim();
        Confirmed = true;
        DialogResult = true;
        Close();
    }
}
