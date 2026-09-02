using System.IO;
using System.Text.Json;
using Spider8DAQ.Core.Licensing;

namespace Spider8DAQ.App.Controls;

public sealed class LiveChannelPlotEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int ChannelIndex { get; set; }
    public double Left { get; set; } = 620;
    public double Top { get; set; } = 480;
    public double Width { get; set; } = 420;
    public double Height { get; set; } = 280;
    public bool FollowLive { get; set; } = true;
}

public sealed class LiveChannelPlotSettings
{
    public List<LiveChannelPlotEntry> Panels { get; set; } = new();
}

/// <summary>Persists separate Y(t) panels under %LocalAppData%\UPETAcqLab\live-channel-plots.json.</summary>
public static class LiveChannelPlotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string SettingsFilePath =>
        Path.Combine(UpetLicense.AppDataDirectory, "live-channel-plots.json");

    public static LiveChannelPlotSettings Load()
    {
        try
        {
            var path = SettingsFilePath;
            if (!File.Exists(path))
                return new LiveChannelPlotSettings();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LiveChannelPlotSettings>(json, JsonOptions)
                   ?? new LiveChannelPlotSettings();
        }
        catch
        {
            return new LiveChannelPlotSettings();
        }
    }

    public static void Save(LiveChannelPlotSettings settings)
    {
        try
        {
            Directory.CreateDirectory(UpetLicense.AppDataDirectory);
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            /* non-fatal */
        }
    }
}
