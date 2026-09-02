using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Spider8DAQ.Core.Integrations;

/// <summary>Optional post-recording export: copy CSV to a folder and/or POST JSON metadata.</summary>
public sealed class ThirdPartyExportSettings
{
    public bool Enabled { get; set; }
    public string OutboundFolder { get; set; } = "";
    public string WebhookUrl { get; set; } = "";
    public bool CopyCsv { get; set; } = true;
    public bool PostMetadata { get; set; }
}

public sealed class ThirdPartyExportResult
{
    public bool Copied { get; init; }
    public string? CopiedPath { get; init; }
    public bool Posted { get; init; }
    public string Message { get; init; } = "";
}

public static class ThirdPartyExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static string SettingsPath(string? dataRoot = null) =>
        string.IsNullOrWhiteSpace(dataRoot)
            ? AppPaths.IntegrationsSettings
            : Path.Combine(dataRoot, "data", "integrations.json");

    public static ThirdPartyExportSettings Load(string? dataRoot = null)
    {
        var path = SettingsPath(dataRoot);
        if (!File.Exists(path)) return new ThirdPartyExportSettings();
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ThirdPartyExportSettings>(json, JsonOptions)
                   ?? new ThirdPartyExportSettings();
        }
        catch
        {
            return new ThirdPartyExportSettings();
        }
    }

    public static void Save(ThirdPartyExportSettings settings, string? dataRoot = null)
    {
        var path = SettingsPath(dataRoot);
        AppPaths.EnsureWritable(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }

    /// <summary>Legacy overload — <paramref name="baseDir"/> ignored; settings live under LocalAppData.</summary>
    public static void Save(string baseDir, ThirdPartyExportSettings settings) =>
        Save(settings, dataRoot: null);

    public static async Task<ThirdPartyExportResult> ExportAsync(
        ThirdPartyExportSettings settings,
        string csvPath,
        object metadata,
        CancellationToken ct = default)
    {
        if (!settings.Enabled)
            return new ThirdPartyExportResult { Message = "Integrare 3rd-party dezactivată." };

        var copied = false;
        string? copiedPath = null;
        var posted = false;
        var notes = new List<string>();

        if (settings.CopyCsv && !string.IsNullOrWhiteSpace(settings.OutboundFolder) && File.Exists(csvPath))
        {
            Directory.CreateDirectory(settings.OutboundFolder);
            copiedPath = Path.Combine(settings.OutboundFolder, Path.GetFileName(csvPath));
            await Task.Run(() => File.Copy(csvPath, copiedPath, overwrite: true), ct);
            copied = true;
            notes.Add($"Copiat → {copiedPath}");
        }

        if (settings.PostMetadata && !string.IsNullOrWhiteSpace(settings.WebhookUrl))
        {
            var payload = JsonSerializer.Serialize(metadata, JsonOptions);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(settings.WebhookUrl, content, ct);
            posted = resp.IsSuccessStatusCode;
            notes.Add(posted
                ? $"Webhook OK ({(int)resp.StatusCode})"
                : $"Webhook eșuat ({(int)resp.StatusCode})");
        }

        if (notes.Count == 0)
            notes.Add("Nicio acțiune (folder/webhook neconfigurate).");

        return new ThirdPartyExportResult
        {
            Copied = copied,
            CopiedPath = copiedPath,
            Posted = posted,
            Message = string.Join("; ", notes)
        };
    }
}
