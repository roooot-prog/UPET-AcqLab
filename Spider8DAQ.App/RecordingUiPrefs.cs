using System.IO;
using System.Text.Json;
using Spider8DAQ.Core;

namespace Spider8DAQ.App;

/// <summary>Persisted Rec / experiment soft-prompt prefs.</summary>
public static class RecordingUiPrefs
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static bool? _skipNoExperimentWarning;

    private static string PrefsPath =>
        Path.Combine(AppPaths.Data, "recording-prefs.json");

    public static bool SkipNoExperimentWarning
    {
        get
        {
            _skipNoExperimentWarning ??= Load().SkipNoExperimentWarning;
            return _skipNoExperimentWarning.Value;
        }
        set
        {
            _skipNoExperimentWarning = value;
            try
            {
                AppPaths.EnsureWritable(AppPaths.Data);
                var dto = Load();
                dto.SkipNoExperimentWarning = value;
                File.WriteAllText(PrefsPath, JsonSerializer.Serialize(dto, Json));
            }
            catch
            {
                /* ignore prefs write failures */
            }
        }
    }

    private static Dto Load()
    {
        try
        {
            if (File.Exists(PrefsPath))
            {
                var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(PrefsPath), Json);
                if (dto is not null) return dto;
            }
        }
        catch
        {
            /* defaults */
        }

        return new Dto();
    }

    private sealed class Dto
    {
        public bool SkipNoExperimentWarning { get; set; }
    }
}
