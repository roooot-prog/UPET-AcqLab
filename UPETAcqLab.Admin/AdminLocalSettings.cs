using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UPETAcqLab.Admin;

internal sealed class AdminLocalSettings
{
    public string LicenseServerUrl { get; set; } = "http://127.0.0.1:5088";
    public string AdminPassword { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UPETAcqLab.Admin",
        "settings.json");

    public static AdminLocalSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return JsonSerializer.Deserialize<AdminLocalSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new();
        }
        catch
        {
            return new();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts));
    }
}
