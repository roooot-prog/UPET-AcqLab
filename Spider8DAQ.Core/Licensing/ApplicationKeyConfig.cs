using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spider8DAQ.Core.Licensing;

/// <summary>
/// Single AcqLab config file next to the EXE: <c>license-server.json</c>.
/// Empty <see cref="LicenseServerUrl"/> skips the application-key gate (author PC).
/// </summary>
public sealed class ApplicationKeyConfig
{
    public const string FileName = "license-server.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [JsonPropertyName("LicenseServerUrl")]
    public string LicenseServerUrl { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(LicenseServerUrl);

    public string NormalizedBaseUrl =>
        (LicenseServerUrl ?? "").Trim().TrimEnd('/');

    public static string FilePathFor(string? installRoot = null) =>
        Path.Combine(installRoot ?? AppPaths.InstallRoot, FileName);

    public static ApplicationKeyConfig Load(string? installRoot = null)
    {
        try
        {
            var path = FilePathFor(installRoot);
            if (!File.Exists(path)) return new();
            var cfg = JsonSerializer.Deserialize<ApplicationKeyConfig>(File.ReadAllText(path), JsonOpts);
            if (cfg is null) return new();
            cfg.LicenseServerUrl = (cfg.LicenseServerUrl ?? "").Trim();
            return cfg;
        }
        catch
        {
            return new();
        }
    }
}
