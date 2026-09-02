using System.Windows;
using System.Windows.Input;
using Spider8DAQ.App.Controls;
using Spider8DAQ.Core.Physics;

namespace Spider8DAQ.App.ViewModels;

public partial class MainViewModel
{
    private bool _hydraulicPressChecklistShown;

    private void LoadHydraulicPressFlags(LiveGaugeSettings settings)
    {
        _hydraulicPressChecklistShown = settings.HydraulicPressChecklistShown;
    }

    private void SaveHydraulicPressFlags(LiveGaugeSettings settings)
    {
        settings.HydraulicPressChecklistShown = _hydraulicPressChecklistShown;
    }

    /// <summary>Y(t) + cadran presiune→kg, aria piston UPET, senzor P15 recomandat.</summary>
    private void ApplyHydraulicPressSetup(bool showChecklist)
    {
        PlotMode = DisplayLayouts.Yt;
        FollowLiveZoom = true;
        MultiPanelMode = false;

        PistonAreaCm2 = PressureToMass.DefaultPistonAreaCm2;
        ApplyHydraulicPressCalibrationProfile();

        if (LiveGauges.Count == 0)
            AddLiveGauge();
        foreach (var g in LiveGauges)
            g.DisplayMode = GaugeModePressureToKg;
        OnPropertyChanged(nameof(LiveGaugeDisplayMode));
        OnPropertyChanged(nameof(ShowPressureToKgOption));
        ShowLiveGauge = true;

        var p15 = _sensorLibrary.FindByCode("P15RVA/1/200B");
        if (p15 is not null)
        {
            SelectedSensorRow = SensorRows.FirstOrDefault(r => r.Code == p15.Code)
                                ?? SensorRows.FirstOrDefault(r => r.Name == p15.Name);
            SelectedSensorName = p15.Name;
        }

        var idx = ResolveHwIndexFromUiSelector(SelectedChannelForSensor);
        if (idx is int i && i < Channels.Count)
        {
            var ch = Channels[i];
            ch.Enabled = true;
            ch.ShowOnPlot = true;
            SelectedChannelRow = ch;
            if (LiveGauges.Count > 0)
                LiveGauges[0].ChannelIndex = i;
        }
        else
        {
            var first = Channels.FirstOrDefault(c => c.Enabled) ?? Channels.FirstOrDefault();
            if (first is not null)
            {
                first.Enabled = true;
                first.ShowOnPlot = true;
                SelectedChannelRow = first;
                if (LiveGauges.Count > 0)
                    LiveGauges[0].ChannelIndex = first.Index;
            }
        }

        foreach (var g in LiveGauges)
            ResetGaugeScale(g);

        RebuildPlotSeries();
        AutoscaleLivePlots();
        SaveLiveGaugeSettings();

        var settings = LiveGaugeStore.Load();
        settings.ActiveProfileId = PressureToMass.HydraulicPressProfileId;
        LiveGaugeStore.Save(settings);

        HelpPanelText =
            "Presă hidraulică UPET: Y(t) în bar, cadran în kg (A=201,06 cm², scară 0–50 t). " +
            "Zero la aer → Apply P15 → Start. 1 t ≈ 4,88 bar · 3 t ≈ 14,63 bar.";

        if (showChecklist && !_hydraulicPressChecklistShown)
        {
            _hydraulicPressChecklistShown = true;
            SaveLiveGaugeSettings();
            MessageBox.Show(
                "Pași recomandați — presă hidraulică UPET:\n\n" +
                "1. Conectați Spider8; cablați P15RVA/1/200B pe DcVoltage (20 bar/V).\n" +
                "2. Zero la aer (F9) — ~0 bar la repaus.\n" +
                "3. Selectați P15 în bibliotecă → Apply → CH.\n" +
                "4. Verificați aria piston: 201,06 cm² (Cadran / Vizualizare).\n" +
                "5. Start (F5) — grafic bar, cadran kg.\n\n" +
                "Referință: 1 t ≈ 4,88 bar · 3 t ≈ 14,63 bar (~205 kg/bar). Cadran: scară 0–50 t.",
                "Presă hidraulică",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void ApplyHydraulicPressCalibrationProfile()
    {
        var settings = LiveGaugeStore.Load();
        var profile = settings.Profiles.FirstOrDefault(p => p.Id == PressureToMass.HydraulicPressProfileId);
        if (profile is null) return;
        PistonAreaCm2 = profile.PistonAreaCm2;
        if (!string.IsNullOrWhiteSpace(profile.DisplayMode))
        {
            foreach (var g in LiveGauges)
                g.DisplayMode = profile.DisplayMode;
        }
    }

    private void OnHydraulicPressPresetSelected()
    {
        ApplyHydraulicPressSetup(showChecklist: true);
        Status = "Experiment: Presă hidraulică — Y(t) bar + cadran kg (A=201,06 cm²).";
    }

    internal void WireHydraulicPressCommands()
    {
        ApplyHydraulicPressCommand = new RelayCommand(() =>
        {
            var preset = ExperimentPresets.FirstOrDefault(p => p.Id == "hydraulic-press");
            if (preset is not null)
                SelectedExperiment = preset;
            else
                OnHydraulicPressPresetSelected();
        });
    }

    public ICommand ApplyHydraulicPressCommand { get; private set; } = null!;
}
