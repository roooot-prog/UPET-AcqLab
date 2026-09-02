using System.IO;
using System.Text.Json;
using Spider8DAQ.Core.Licensing;
using Spider8DAQ.Core.Physics;

namespace Spider8DAQ.App.Controls;

public sealed class LiveGaugeEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int ChannelIndex { get; set; } = -1;
    public string DisplayMode { get; set; } = "Native";
    /// <summary>Default right of Grafic live (rack column).</summary>
    public double Left { get; set; } = 1276;
    public double Top { get; set; } = 4;
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 300;
    public bool AutoScale { get; set; } = true;
    public double GaugeMin { get; set; }
    public double GaugeMax { get; set; } = 100;
    /// <summary>Auto | N | kN — preferred primary force unit on the dial.</summary>
    public string ForceUnit { get; set; } = "Auto";
}

public sealed class LiveGaugeCalibrationProfile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double PistonAreaCm2 { get; set; } = PressureToMass.DefaultPistonAreaCm2;
    public string DisplayMode { get; set; } = "PressureToKg";
}

public sealed class LiveGaugeSettings
{
    public bool Enabled { get; set; }
    public int ChannelIndex { get; set; } = -1;
    public double PistonAreaCm2 { get; set; } = PressureToMass.DefaultPistonAreaCm2;
    /// <summary>Native = unitate senzor; PressureToKg = bar→kg cu aria pistonului.</summary>
    public string DisplayMode { get; set; } = "Native";
    public List<LiveGaugeEntry> Gauges { get; set; } = new();
    public List<LiveGaugeCalibrationProfile> Profiles { get; set; } = new();
    public string? ActiveProfileId { get; set; }
    public bool HydraulicPressChecklistShown { get; set; }
}

/// <summary>Persists cadrane live under %LocalAppData%\UPETAcqLab\live-gauge.json.</summary>
public static class LiveGaugeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string SettingsFilePath =>
        Path.Combine(UpetLicense.AppDataDirectory, "live-gauge.json");

    private static string LegacyPath =>
        Path.Combine(UpetLicense.AppDataDirectory, "pressure-gauge.json");

    public static LiveGaugeSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (!File.Exists(path) && File.Exists(LegacyPath))
            {
                var legacy = File.ReadAllText(LegacyPath);
                var old = JsonSerializer.Deserialize<PressureGaugeSettingsLegacy>(legacy, JsonOptions);
                if (old is not null)
                {
                    var migrated = Normalize(new LiveGaugeSettings
                    {
                        Enabled = old.Enabled,
                        ChannelIndex = old.ChannelIndex,
                        PistonAreaCm2 = MigratePistonArea(old.PistonAreaCm2),
                        DisplayMode = "PressureToKg",
                        Gauges = new List<LiveGaugeEntry>
                        {
                            new()
                            {
                                ChannelIndex = old.ChannelIndex,
                                DisplayMode = "PressureToKg",
                                Left = 1276,
                                Top = 4
                            }
                        }
                    });
                    Save(migrated);
                    return migrated;
                }
            }
            if (!File.Exists(path))
            {
                var fresh = Normalize(new LiveGaugeSettings());
                Save(fresh);
                return fresh;
            }
            var json = File.ReadAllText(path);
            var doc = JsonSerializer.Deserialize<LiveGaugeSettings>(json, JsonOptions) ?? new LiveGaugeSettings();
            if (doc.Gauges.Count == 0 && (doc.Enabled || doc.ChannelIndex >= -1))
            {
                doc.Gauges.Add(new LiveGaugeEntry
                {
                    ChannelIndex = doc.ChannelIndex,
                    DisplayMode = doc.DisplayMode ?? "Native",
                    Left = 1276,
                    Top = 4
                });
            }
            var normalized = Normalize(doc);
            if (NeedsPersistAfterNormalize(doc, normalized))
                Save(normalized);
            return normalized;
        }
        catch
        {
            return Normalize(new LiveGaugeSettings());
        }
    }

    public static void Save(LiveGaugeSettings settings)
    {
        try
        {
            Directory.CreateDirectory(UpetLicense.AppDataDirectory);
            var normalized = Normalize(settings);
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(normalized, JsonOptions));
        }
        catch
        {
            /* non-fatal */
        }
    }

    public static double MigratePistonArea(double areaCm2) =>
        PressureToMass.NormalizePistonAreaCm2(areaCm2);

    private static LiveGaugeSettings Normalize(LiveGaugeSettings settings)
    {
        settings.PistonAreaCm2 = MigratePistonArea(settings.PistonAreaCm2);
        EnsureDefaultProfiles(settings);
        return settings;
    }

    private static void EnsureDefaultProfiles(LiveGaugeSettings settings)
    {
        settings.Profiles ??= new List<LiveGaugeCalibrationProfile>();
        if (settings.Profiles.All(p => p.Id != PressureToMass.HydraulicPressProfileId))
        {
            settings.Profiles.Insert(0, new LiveGaugeCalibrationProfile
            {
                Id = PressureToMass.HydraulicPressProfileId,
                Name = PressureToMass.HydraulicPressProfileName,
                PistonAreaCm2 = PressureToMass.DefaultPistonAreaCm2,
                DisplayMode = "PressureToKg"
            });
        }
    }

    private static bool NeedsPersistAfterNormalize(LiveGaugeSettings before, LiveGaugeSettings after)
    {
        if (Math.Abs(before.PistonAreaCm2 - after.PistonAreaCm2) > 0.001)
            return true;
        if (before.Profiles is null || before.Profiles.Count == 0)
            return true;
        return before.Profiles.All(p => p.Id != PressureToMass.HydraulicPressProfileId);
    }

    private sealed class PressureGaugeSettingsLegacy
    {
        public bool Enabled { get; set; }
        public double PistonAreaCm2 { get; set; } = PressureToMass.LegacyPistonAreaCm2;
        public int ChannelIndex { get; set; } = -1;
    }
}
