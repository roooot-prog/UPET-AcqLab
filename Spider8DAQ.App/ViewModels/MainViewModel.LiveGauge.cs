using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Spider8DAQ.App.Controls;
using Spider8DAQ.Core.Acquisition;
using Spider8DAQ.Core.Physics;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    public const string GaugeModeNative = "Native";
    public const string GaugeModePressureToKg = "PressureToKg";
    public const string GaugeModeBarToMpa = "BarToMpa";
    public const string GaugeModeForceToKn = "ForceToKn";
    public const string GaugeModeStrainToPercent = "StrainToPercent";

    private bool _showLiveGauge;
    private double _pistonAreaCm2 = PressureToMass.DefaultPistonAreaCm2;
    private LiveGaugeItemViewModel? _selectedLiveGauge;

    /// <summary>Afișează cadranele gradate live pe spațiul de lucru.</summary>
    public bool ShowLiveGauge
    {
        get => _showLiveGauge;
        set
        {
            _showLiveGauge = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowPressureKgGauge));
            SaveLiveGaugeSettings();
            if (value && LiveGauges.Count == 0)
                AddLiveGauge();
            if (value)
                HelpPanelText = "Cadrane live: trageți de titlu pentru mutare; alegeți canalul CH0–CH7 pe fiecare cadran. Y(t) rămâne activ.";
        }
    }

    /// <summary>Compatibilitate binding-uri vechi presiune→kg.</summary>
    public bool ShowPressureKgGauge
    {
        get => ShowLiveGauge;
        set => ShowLiveGauge = value;
    }

    public ObservableCollection<LiveGaugeItemViewModel> LiveGauges { get; } = new();

    public LiveGaugeItemViewModel? SelectedLiveGauge
    {
        get => _selectedLiveGauge;
        set { _selectedLiveGauge = value; OnPropertyChanged(); }
    }

    public ObservableCollection<LiveGaugeChannelOption> LiveGaugeChannelOptions { get; } = new()
    {
        new LiveGaugeChannelOption("Auto (canal liber)", -1),
        new LiveGaugeChannelOption("CH0", 0),
        new LiveGaugeChannelOption("CH1", 1),
        new LiveGaugeChannelOption("CH2", 2),
        new LiveGaugeChannelOption("CH3", 3),
        new LiveGaugeChannelOption("CH4", 4),
        new LiveGaugeChannelOption("CH5", 5),
        new LiveGaugeChannelOption("CH6", 6),
        new LiveGaugeChannelOption("CH7", 7),
    };

    public double PistonAreaCm2
    {
        get => _pistonAreaCm2;
        set
        {
            _pistonAreaCm2 = PressureToMass.NormalizePistonAreaCm2(Math.Max(0, value));
            OnPropertyChanged();
            SaveLiveGaugeSettings();
            foreach (var g in LiveGauges)
                RecomputeGaugeItem(g, g.SourcePhysical, ResolveGaugeChannel(g));
        }
    }

    public string LiveGaugeFormulaHint =>
        "Presiune→kg: m[kg] = P[bar]×10⁵×A[cm²]×10⁻⁴ / 9,80665. UPET: A=201,06 cm² → ~205 kg/bar; cadran scară 0–50 t (50 000 kg).";

    public ObservableCollection<string> LiveGaugeDisplayModes { get; } =
        new([GaugeModeNative, GaugeModePressureToKg, GaugeModeBarToMpa, GaugeModeForceToKn, GaugeModeStrainToPercent]);

    /// <summary>Modes allowed for a channel (unit / category) — used by cadran ComboBox.</summary>
    public static IReadOnlyList<LiveGaugeDisplayModeOption> GetDisplayModesForChannel(ChannelRow? ch)
    {
        var list = new List<LiveGaugeDisplayModeOption>
        {
            new(GaugeModeNative, "Nativ (unitate senzor)")
        };
        if (ch is null)
            return list;

        var unit = ch.Unit ?? "";
        var cat = ch.SensorCategory ?? "";
        var name = (ch.SensorName ?? "") + " " + ch.Name;

        var isPressure = unit.Contains("bar", StringComparison.OrdinalIgnoreCase)
                         || cat.Contains("Presiune", StringComparison.OrdinalIgnoreCase)
                         || name.Contains("P15", StringComparison.OrdinalIgnoreCase);
        var isForce = unit.Equals("N", StringComparison.OrdinalIgnoreCase)
                      || unit.Equals("kN", StringComparison.OrdinalIgnoreCase)
                      || cat.Contains("Forță", StringComparison.OrdinalIgnoreCase)
                      || cat.Contains("Forta", StringComparison.OrdinalIgnoreCase)
                      || name.Contains("U2B", StringComparison.OrdinalIgnoreCase);
        var isStrain = unit.Contains("µm/m", StringComparison.OrdinalIgnoreCase)
                       || unit.Contains("um/m", StringComparison.OrdinalIgnoreCase)
                       || unit.Contains("µε", StringComparison.OrdinalIgnoreCase)
                       || cat.Contains("tensometr", StringComparison.OrdinalIgnoreCase)
                       || name.Contains("Timbru", StringComparison.OrdinalIgnoreCase)
                       || name.Contains("pasiv", StringComparison.OrdinalIgnoreCase);

        if (isPressure)
        {
            list.Add(new(GaugeModePressureToKg, "Presiune → kg"));
            list.Add(new(GaugeModeBarToMpa, "bar → MPa"));
        }
        if (isForce && unit.Equals("N", StringComparison.OrdinalIgnoreCase))
            list.Add(new(GaugeModeForceToKn, "N → kN"));
        if (isStrain)
            list.Add(new(GaugeModeStrainToPercent, "µm/m → %"));

        return list;
    }

    public void RefreshGaugeAvailableModes(LiveGaugeItemViewModel gauge)
    {
        var ch = ResolveGaugeChannel(gauge);
        var modes = GetDisplayModesForChannel(ch);
        gauge.SetAvailableDisplayModes(modes);
        if (modes.All(m => m.Id != gauge.DisplayMode))
            gauge.DisplayMode = GaugeModeNative;
    }

    // Legacy aliases — primul cadran, pentru compatibilitate
    public int LiveGaugeChannelIndex
    {
        get => LiveGauges.FirstOrDefault()?.ChannelIndex ?? -1;
        set
        {
            var g = LiveGauges.FirstOrDefault();
            if (g is null) { AddLiveGauge(); g = LiveGauges[0]; }
            g.ChannelIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LiveGaugeChannelPicker));
            SaveLiveGaugeSettings();
        }
    }

    public int LiveGaugeChannelPicker
    {
        get => LiveGaugeChannelIndex + 1;
        set => LiveGaugeChannelIndex = value - 1;
    }

    public string LiveGaugeDisplayMode
    {
        get => LiveGauges.FirstOrDefault()?.DisplayMode ?? GaugeModeNative;
        set
        {
            foreach (var g in LiveGauges)
                g.DisplayMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowPressureToKgOption));
            SaveLiveGaugeSettings();
            foreach (var g in LiveGauges)
                ResetGaugeScale(g);
        }
    }

    public bool ShowPressureToKgOption =>
        LiveGauges.Any(g => g.DisplayMode == GaugeModePressureToKg
                            || ResolveGaugeChannel(g)?.Unit.Contains("bar", StringComparison.OrdinalIgnoreCase) == true);

    public double LiveGaugeValue => LiveGauges.FirstOrDefault()?.Value ?? double.NaN;
    public string LiveGaugeDisplay => LiveGauges.FirstOrDefault()?.Display ?? "—";
    public string LiveGaugeUnit => LiveGauges.FirstOrDefault()?.Unit ?? "";
    public double LiveGaugeGaugeMax => LiveGauges.FirstOrDefault()?.GaugeMax ?? 100;
    public double LiveGaugeGaugeMin => LiveGauges.FirstOrDefault()?.GaugeMin ?? 0;
    public string LiveGaugeChannelLabel => LiveGauges.FirstOrDefault()?.ChannelLabel ?? "—";

    public double PressureKgValue => LiveGaugeValue;
    public string PressureKgDisplay => LiveGaugeDisplay;
    public double PressureKgGaugeMax => LiveGaugeGaugeMax;
    public string PressureKgChannelLabel => LiveGaugeChannelLabel;
    public string PressureKgFormulaHint => LiveGaugeFormulaHint;
    public int PressureKgChannelPicker
    {
        get => LiveGaugeChannelPicker;
        set => LiveGaugeChannelPicker = value;
    }

    internal void WireLiveGaugeCommands()
    {
        AddLiveGaugeCommand = new RelayCommand(AddLiveGauge);
        RemoveSelectedLiveGaugeCommand = new RelayCommand(RemoveSelectedLiveGauge, () => SelectedLiveGauge is not null);
    }

    public ICommand AddLiveGaugeCommand { get; private set; } = null!;
    public ICommand RemoveSelectedLiveGaugeCommand { get; private set; } = null!;

    private void AddLiveGauge()
    {
        var offset = LiveGauges.Count * 28;
        // Prefer a distinct enabled channel so 3 cadrane ≠ same Auto→CH first.
        var used = LiveGauges.Select(g => g.ChannelIndex).Where(i => i >= 0).ToHashSet();
        foreach (var g in LiveGauges.Where(x => x.ChannelIndex < 0))
        {
            var claimed = ResolveGaugeChannel(g);
            if (claimed is not null) used.Add(claimed.Index);
        }
        var next = Channels.FirstOrDefault(c => c.Enabled && !used.Contains(c.Index))
                   ?? Channels.FirstOrDefault(c => !used.Contains(c.Index));
        var entry = new LiveGaugeEntry
        {
            Left = 1276 + offset,
            Top = 4 + offset,
            ChannelIndex = next?.Index ?? -1,
            DisplayMode = GaugeModeNative
        };
        var vm = CreateGaugeViewModel(entry);
        LiveGauges.Add(vm);
        SelectedLiveGauge = vm;
        ShowLiveGauge = true;
        SaveLiveGaugeSettings();
        OnPropertyChanged(nameof(LiveGaugeChannelIndex));
        OnPropertyChanged(nameof(LiveGaugeChannelPicker));
    }

    private void RemoveSelectedLiveGauge()
    {
        if (SelectedLiveGauge is not null)
            RemoveLiveGauge(SelectedLiveGauge);
    }

    private void RemoveLiveGauge(LiveGaugeItemViewModel? gauge)
    {
        if (gauge is null || !LiveGauges.Contains(gauge)) return;
        LiveGauges.Remove(gauge);
        if (SelectedLiveGauge == gauge)
            SelectedLiveGauge = LiveGauges.FirstOrDefault();
        if (LiveGauges.Count == 0)
            ShowLiveGauge = false;
        SaveLiveGaugeSettings();
    }

    private LiveGaugeItemViewModel CreateGaugeViewModel(LiveGaugeEntry entry)
    {
        var vm = new LiveGaugeItemViewModel(entry.Id)
        {
            ChannelIndex = entry.ChannelIndex,
            DisplayMode = entry.DisplayMode ?? GaugeModeNative,
            Left = entry.Left,
            Top = entry.Top,
            Width = entry.Width,
            Height = entry.Height,
            AutoScale = entry.AutoScale,
            GaugeMin = entry.GaugeMin,
            GaugeMax = entry.GaugeMax > entry.GaugeMin ? entry.GaugeMax : entry.GaugeMin + 100,
            ForceUnit = string.IsNullOrWhiteSpace(entry.ForceUnit)
                ? LiveGaugeItemViewModel.ForceUnitAuto
                : entry.ForceUnit,
        };
        vm.RemoveCommand = new RelayCommand(() => RemoveLiveGauge(vm));
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(LiveGaugeItemViewModel.ChannelIndex)
                or nameof(LiveGaugeItemViewModel.DisplayMode)
                or nameof(LiveGaugeItemViewModel.ForceUnit))
            {
                if (e.PropertyName == nameof(LiveGaugeItemViewModel.ChannelIndex))
                    RefreshGaugeAvailableModes(vm);
                if (vm.AutoScale) ResetGaugeScale(vm);
                else RecomputeGaugeItem(vm, vm.SourcePhysical, ResolveGaugeChannel(vm));
                SaveLiveGaugeSettings();
            }
            else if (e.PropertyName is nameof(LiveGaugeItemViewModel.AutoScale))
            {
                if (vm.AutoScale) ResetGaugeScale(vm);
                SaveLiveGaugeSettings();
            }
            else if (e.PropertyName is nameof(LiveGaugeItemViewModel.GaugeMin)
                     or nameof(LiveGaugeItemViewModel.GaugeMax))
            {
                SaveLiveGaugeSettings();
            }
        };
        RefreshGaugeAvailableModes(vm);
        if (vm.AutoScale) ResetGaugeScale(vm);
        return vm;
    }

    private void LoadLiveGaugeSettings()
    {
        var s = LiveGaugeStore.Load();
        LoadHydraulicPressFlags(s);
        _pistonAreaCm2 = LiveGaugeStore.MigratePistonArea(s.PistonAreaCm2);
        _showLiveGauge = s.Enabled;
        LiveGauges.Clear();
        foreach (var entry in s.Gauges)
            LiveGauges.Add(CreateGaugeViewModel(entry));
        OnPropertyChanged(nameof(PistonAreaCm2));
        OnPropertyChanged(nameof(ShowLiveGauge));
        OnPropertyChanged(nameof(ShowPressureKgGauge));
        OnPropertyChanged(nameof(LiveGaugeDisplayMode));
        OnPropertyChanged(nameof(LiveGaugeChannelPicker));
    }

    private void SaveLiveGaugeSettings()
    {
        var settings = new LiveGaugeSettings
        {
            Enabled = ShowLiveGauge,
            PistonAreaCm2 = PistonAreaCm2,
            ChannelIndex = LiveGauges.FirstOrDefault()?.ChannelIndex ?? -1,
            DisplayMode = LiveGauges.FirstOrDefault()?.DisplayMode ?? GaugeModeNative,
            Gauges = LiveGauges.Select(g => new LiveGaugeEntry
            {
                Id = g.Id,
                ChannelIndex = g.ChannelIndex,
                DisplayMode = g.DisplayMode,
                Left = g.Left,
                Top = g.Top,
                Width = g.Width,
                Height = g.Height,
                AutoScale = g.AutoScale,
                GaugeMin = g.GaugeMin,
                GaugeMax = g.GaugeMax,
                ForceUnit = g.ForceUnit
            }).ToList()
        };
        SaveHydraulicPressFlags(settings);
        LiveGaugeStore.Save(settings);
    }

    public void OnLiveGaugeLayoutChanged(LiveGaugeItemViewModel gauge) => SaveLiveGaugeSettings();

    /// <summary>Called after the dial settings dialog applies changes (scale / unit / channel).</summary>
    public void NotifyLiveGaugeSettingsApplied(LiveGaugeItemViewModel gauge)
    {
        SelectedLiveGauge = gauge;
        if (gauge.AutoScale)
            ResetGaugeScale(gauge);
        else
            RecomputeGaugeItem(gauge, gauge.SourcePhysical, ResolveGaugeChannel(gauge));
        SaveLiveGaugeSettings();
        HelpPanelText = "Cadran: scară / canal / unitate actualizate. Click pe cadran redeschide setările.";
    }

    /// <summary>Reset dial min/max from sensor Capacity (dialog preset).</summary>
    public void ResetLiveGaugeScaleFromSensor(LiveGaugeItemViewModel gauge)
    {
        gauge.AutoScale = true;
        ResetGaugeScale(gauge);
    }

    private void ResetAllLiveGaugeScales()
    {
        foreach (var g in LiveGauges)
            ResetGaugeScale(g);
    }

    private void RefreshLiveGaugeChannelLabel()
    {
        foreach (var g in LiveGauges)
            RefreshGaugeChannelLabel(g);
    }

    private void RefreshGaugeChannelLabel(LiveGaugeItemViewModel gauge)
    {
        if (gauge.ChannelIndex < 0)
        {
            var auto = ResolveGaugeChannel(gauge);
            gauge.ChannelLabel = auto is null
                ? "Auto (canal liber)"
                : $"Auto → {auto.Name} [{auto.Unit}]";
            return;
        }
        var ch = Channels.ElementAtOrDefault(gauge.ChannelIndex);
        gauge.ChannelLabel = ch is null
            ? $"CH{gauge.ChannelIndex}"
            : $"{ch.Name} [{ch.Unit}]";
    }

    private ChannelRow? ResolveGaugeChannel(LiveGaugeItemViewModel gauge)
    {
        if (gauge.ChannelIndex >= 0 && gauge.ChannelIndex < Channels.Count)
        {
            var selected = Channels[gauge.ChannelIndex];
            if (selected.Enabled) return selected;
        }

        // Auto: assign distinct enabled channels across multiple Auto dials (order in LiveGauges).
        var taken = new HashSet<int>();
        foreach (var other in LiveGauges)
        {
            if (ReferenceEquals(other, gauge)) break;
            if (other.ChannelIndex >= 0 && other.ChannelIndex < Channels.Count
                && Channels[other.ChannelIndex].Enabled)
            {
                taken.Add(other.ChannelIndex);
                continue;
            }
            var earlier = Channels.FirstOrDefault(c => c.Enabled && !taken.Contains(c.Index));
            if (earlier is not null) taken.Add(earlier.Index);
        }

        return Channels.FirstOrDefault(c => c.Enabled && !taken.Contains(c.Index))
               ?? Channels.FirstOrDefault(c => c.Enabled);
    }

    private int ResolveGaugeChannelIndex(LiveGaugeItemViewModel gauge)
    {
        var ch = ResolveGaugeChannel(gauge);
        return ch?.Index ?? 0;
    }

    private static (double Min, double Max) EstimateGaugeRange(ChannelRow ch, bool pressureToKg, double pistonAreaCm2)
    {
        if (pressureToKg && ch.Unit.Contains("bar", StringComparison.OrdinalIgnoreCase))
        {
            // Fixed UPET dial 0–50 t — independent of P15 sensor capacity (~200 bar → ~41 t).
            _ = pistonAreaCm2;
            return (0, PressureToMass.HydraulicPressGaugeMaxKg);
        }
        if (ch.Capacity > 0)
        {
            var cap = Math.Abs(ch.Capacity);
            var unit = ch.Unit ?? "";
            // Relative pressure: unipolar 0…Cap so fill/needle track from empty.
            // Force / other Capacity sensors (U2B etc.): bipolar ±Cap·1.05 from sensor Capacity
            // (U2B 2kN → ±2100 N; U2B 5kN → ±5250 N) — never a shared wrong scale.
            var relativePressure = unit.Contains("bar", StringComparison.OrdinalIgnoreCase)
                                   && !ch.Name.Contains("abs", StringComparison.OrdinalIgnoreCase);
            if (relativePressure)
                return (0, cap * 1.05);
            return (-cap * 1.05, cap * 1.05);
        }
        return (0, 100);
    }

    private void ResetGaugeScale(LiveGaugeItemViewModel gauge)
    {
        var ch = ResolveGaugeChannel(gauge);
        if (ch is null)
        {
            if (gauge.AutoScale)
            {
                gauge.GaugeMin = 0;
                gauge.GaugeMax = 100;
            }
            RefreshGaugeAvailableModes(gauge);
            return;
        }
        if (gauge.AutoScale)
        {
            var pressureKg = gauge.DisplayMode == GaugeModePressureToKg;
            var (min, max) = EstimateGaugeRange(ch, pressureKg, PistonAreaCm2);
            if (gauge.DisplayMode == GaugeModeBarToMpa && ch.Unit.Contains("bar", StringComparison.OrdinalIgnoreCase))
            {
                min /= 10.0;
                max /= 10.0;
            }
            else if (gauge.DisplayMode == GaugeModeForceToKn && ch.Unit.Equals("N", StringComparison.OrdinalIgnoreCase))
            {
                min /= 1000.0;
                max /= 1000.0;
            }
            else if (gauge.DisplayMode == GaugeModeStrainToPercent)
            {
                min /= 10_000.0;
                max /= 10_000.0;
            }
            else
                (min, max) = ConvertForceRangeToPreferred(min, max, ch.Unit, gauge.ForceUnit);
            gauge.GaugeMin = min;
            gauge.GaugeMax = max;
        }
        RefreshGaugeChannelLabel(gauge);
        RefreshGaugeAvailableModes(gauge);
        gauge.ShowPressureToKgOption = gauge.DisplayMode == GaugeModePressureToKg
                                       || ch.Unit.Contains("bar", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateLiveGauge(ProcessedSample sample)
    {
        if (!ShowLiveGauge || LiveGauges.Count == 0) return;

        foreach (var gauge in LiveGauges)
        {
            var chIdx = ResolveGaugeChannelIndex(gauge);
            if (chIdx < 0 || chIdx >= sample.Physical.Length)
            {
                RecomputeGaugeItem(gauge, double.NaN, null);
                continue;
            }

            var ch = Channels.ElementAtOrDefault(chIdx);
            var physical = ch is { Enabled: true } ? sample.Physical[chIdx] : double.NaN;
            gauge.SourcePhysical = physical;
            RecomputeGaugeItem(gauge, physical, ch);

            if (ch is not null)
            {
                gauge.ChannelLabel = gauge.ChannelIndex >= 0
                    ? $"{ch.Name} [{ch.Unit}]"
                    : $"Auto → {ch.Name} [{ch.Unit}]";
            }
        }
    }

    private void RecomputeGaugeItem(LiveGaugeItemViewModel gauge, double physical, ChannelRow? ch)
    {
        ch ??= ResolveGaugeChannel(gauge);

        if (!ShowLiveGauge || ch is null)
        {
            gauge.Value = double.NaN;
            gauge.Display = "—";
            gauge.Unit = "";
            gauge.SourceBarDisplay = "";
            gauge.Subtitle = "";
            gauge.ShowPressureDual = false;
            gauge.SecondaryDisplay = "";
            gauge.ShowForceDual = false;
            return;
        }

        var useKg = gauge.DisplayMode == GaugeModePressureToKg
                    && ch.Unit.Contains("bar", StringComparison.OrdinalIgnoreCase)
                    && PistonAreaCm2 > 0;
        var useMpa = gauge.DisplayMode == GaugeModeBarToMpa
                     && ch.Unit.Contains("bar", StringComparison.OrdinalIgnoreCase);
        var useKn = gauge.DisplayMode == GaugeModeForceToKn
                    && (ch.Unit.Equals("N", StringComparison.OrdinalIgnoreCase)
                        || ch.Unit.Equals("kN", StringComparison.OrdinalIgnoreCase));
        var useStrainPct = gauge.DisplayMode == GaugeModeStrainToPercent
                           && (ch.Unit.Contains("µm/m", StringComparison.OrdinalIgnoreCase)
                               || ch.Unit.Contains("um/m", StringComparison.OrdinalIgnoreCase));

        double value;
        string unit;
        if (useKg)
        {
            value = PressureToMass.BarCm2ToKg(physical, PistonAreaCm2);
            unit = "kg";
            // Secondary bar line keeps decimals (e.g. "0.184 bar").
            gauge.SourceBarDisplay = double.IsNaN(physical) ? "— bar" : $"{FormatGaugeValue(physical)} bar";
            gauge.Subtitle =
                $"A={PistonAreaCm2:0.##} cm² · scară 0–{PressureToMass.HydraulicPressGaugeMaxTons:0} t";
            gauge.ShowPressureDual = true;
            gauge.SecondaryDisplay = "";
            gauge.ShowForceDual = false;
        }
        else if (useMpa)
        {
            value = physical / 10.0; // 1 bar = 0.1 MPa
            unit = "MPa";
            gauge.SourceBarDisplay = double.IsNaN(physical) ? "— bar" : $"{FormatGaugeValue(physical)} bar";
            gauge.Subtitle = "1 bar = 0,1 MPa";
            gauge.ShowPressureDual = true;
            gauge.SecondaryDisplay = "";
            gauge.ShowForceDual = false;
        }
        else if (useKn)
        {
            if (ch.Unit.Equals("kN", StringComparison.OrdinalIgnoreCase))
            {
                value = physical;
                unit = "kN";
            }
            else
            {
                value = physical / 1000.0;
                unit = "kN";
            }
            gauge.SourceBarDisplay = "";
            gauge.Subtitle = ch.Capacity > 0 && !double.IsNaN(physical)
                ? $"{100.0 * physical / ch.Capacity:0.##} %FS"
                : "N → kN";
            gauge.ShowPressureDual = false;
            if (!double.IsNaN(value))
            {
                var n = value * 1000.0;
                gauge.SecondaryDisplay = string.Format(CultureInfo.InvariantCulture, "{0} N", FormatGaugeValue(n));
                gauge.ShowForceDual = true;
            }
            else
            {
                gauge.SecondaryDisplay = "";
                gauge.ShowForceDual = false;
            }
        }
        else if (useStrainPct)
        {
            value = physical / 10_000.0; // µm/m → %
            unit = "%";
            gauge.SourceBarDisplay = double.IsNaN(physical) ? "— µm/m" : $"{FormatGaugeValue(physical)} µm/m";
            gauge.Subtitle = "1% = 10 000 µm/m";
            gauge.ShowPressureDual = true;
            gauge.SecondaryDisplay = "";
            gauge.ShowForceDual = false;
        }
        else
        {
            value = physical;
            unit = string.IsNullOrWhiteSpace(ch.Unit) ? "" : ch.Unit;
            (value, unit) = ApplyPreferredForceUnit(value, unit, gauge.ForceUnit);
            gauge.SourceBarDisplay = "";
            // Near-zero on large FS (e.g. U2B 5 kN): show %FS so Y-zoom 29–33 N doesn't look like a fault.
            if (ch.Capacity > 0 && !double.IsNaN(physical))
            {
                var pct = 100.0 * physical / ch.Capacity;
                gauge.Subtitle = Math.Abs(pct) < 2.0
                    ? $"{pct:0.###} %FS — aproape zero (Zero CH pe liber)"
                    : $"{pct:0.##} %FS";
            }
            else
                gauge.Subtitle = "";
            gauge.ShowPressureDual = false;
            var unitTrim = unit.Trim();
            var isForceN = unitTrim.Equals("N", StringComparison.OrdinalIgnoreCase);
            var isForceKn = unitTrim.Equals("kN", StringComparison.OrdinalIgnoreCase);
            if (!double.IsNaN(value) && isForceN)
            {
                var kn = value / 1000.0;
                var kg = value / 9.80665;
                gauge.SecondaryDisplay = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} kN · {1} kg", FormatGaugeValue(kn), FormatGaugeValue(kg));
                gauge.ShowForceDual = true;
            }
            else if (!double.IsNaN(value) && isForceKn)
            {
                var n = value * 1000.0;
                var kg = n / 9.80665;
                gauge.SecondaryDisplay = string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} N · {1} kg", FormatGaugeValue(n), FormatGaugeValue(kg));
                gauge.ShowForceDual = true;
            }
            else
            {
                gauge.SecondaryDisplay = "";
                gauge.ShowForceDual = false;
            }
        }

        gauge.Value = value;
        gauge.Unit = unit;
        gauge.Display = double.IsNaN(value) ? "—" : FormatGaugeValue(value, unit);

        if (gauge.AutoScale && (gauge.GaugeMax <= gauge.GaugeMin + 1e-9 || (ch.Capacity > 0 && !useKg && !useMpa && !useStrainPct)))
            ResetGaugeScale(gauge);

        if (gauge.AutoScale && !double.IsNaN(value) && !useKg && ch.Capacity <= 0)
        {
            var span = gauge.GaugeMax - gauge.GaugeMin;
            if (span < 1e-9) span = Math.Max(1, Math.Abs(value));
            if (value > gauge.GaugeMax - span * 0.05)
                gauge.GaugeMax = value + span * 0.15;
            if (value < gauge.GaugeMin + span * 0.05 && gauge.GaugeMin < 0)
                gauge.GaugeMin = value - span * 0.15;
        }
    }

    private static string FormatGaugeValue(double v, string? unit = null)
    {
        if (!string.IsNullOrWhiteSpace(unit)
            && unit.Trim().Equals("kg", StringComparison.OrdinalIgnoreCase))
            return Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

        var a = Math.Abs(v);
        if (a >= 1000) return v.ToString("0.##", CultureInfo.InvariantCulture);
        if (a >= 10) return v.ToString("0.###", CultureInfo.InvariantCulture);
        if (a >= 1) return v.ToString("0.####", CultureInfo.InvariantCulture);
        return v.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    /// <summary>Convert physical force value/unit to the dial's preferred primary unit.</summary>
    private static (double Value, string Unit) ApplyPreferredForceUnit(double value, string unit, string? preferred)
    {
        var pref = string.IsNullOrWhiteSpace(preferred) ||
                   preferred.Equals(LiveGaugeItemViewModel.ForceUnitAuto, StringComparison.OrdinalIgnoreCase)
            ? null
            : preferred.Trim();
        if (pref is null || double.IsNaN(value)) return (value, unit ?? "");

        var src = (unit ?? "").Trim();
        var isN = src.Equals("N", StringComparison.OrdinalIgnoreCase);
        var isKn = src.Equals("kN", StringComparison.OrdinalIgnoreCase);
        if (!isN && !isKn) return (value, unit ?? "");

        if (pref.Equals(LiveGaugeItemViewModel.ForceUnitKn, StringComparison.OrdinalIgnoreCase))
            return isKn ? (value, "kN") : (value / 1000.0, "kN");
        if (pref.Equals(LiveGaugeItemViewModel.ForceUnitN, StringComparison.OrdinalIgnoreCase))
            return isN ? (value, "N") : (value * 1000.0, "N");
        return (value, unit ?? "");
    }

    private static (double Min, double Max) ConvertForceRangeToPreferred(
        double min, double max, string? channelUnit, string? preferred)
    {
        var pref = string.IsNullOrWhiteSpace(preferred) ||
                   preferred.Equals(LiveGaugeItemViewModel.ForceUnitAuto, StringComparison.OrdinalIgnoreCase)
            ? null
            : preferred.Trim();
        if (pref is null) return (min, max);

        var src = (channelUnit ?? "").Trim();
        var isN = src.Equals("N", StringComparison.OrdinalIgnoreCase);
        var isKn = src.Equals("kN", StringComparison.OrdinalIgnoreCase);
        if (!isN && !isKn) return (min, max);

        if (pref.Equals(LiveGaugeItemViewModel.ForceUnitKn, StringComparison.OrdinalIgnoreCase) && isN)
            return (min / 1000.0, max / 1000.0);
        if (pref.Equals(LiveGaugeItemViewModel.ForceUnitN, StringComparison.OrdinalIgnoreCase) && isKn)
            return (min * 1000.0, max * 1000.0);
        return (min, max);
    }
}
