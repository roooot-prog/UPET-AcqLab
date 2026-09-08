using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spider8DAQ.Core.Licensing;

/// <summary>
/// Single AcqLab config file next to the EXE: <c>license-server.json</c>.
/// Empty <see cref="LicenseServerUrl"/> skips the application-key gate and heartbeat
/// (author PC / source tree). The lab copy that should receive Admin Mesaj must set the URL.
/// </summary>
public sealed class ApplicationKeyConfig
{
    public const string FileName = "license-server.json";
    public const string LocalLoopbackUrl = "http://127.0.0.1:5088";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [JsonPropertyName("LicenseServerUrl")]
    public string LicenseServerUrl { get; set; } = "";

    /// <summary>Extra bases (LAN IP, previous tunnel). Tried after <see cref="LicenseServerUrl"/>.</summary>
    [JsonPropertyName("LicenseServerUrls")]
    public List<string> LicenseServerUrls { get; set; } = new();

    public bool IsConfigured => CandidateUrls.Count > 0;

    public string NormalizedBaseUrl =>
        CandidateUrls.Count > 0 ? CandidateUrls[0] : "";

    /// <summary>Unique server bases: primary URL first, then extras (e.g. LAN http://192.168.x.x:5088).</summary>
    public IReadOnlyList<string> CandidateUrls
    {
        get
        {
            var list = new List<string>();
            AddUnique(list, LicenseServerUrl);
            if (LicenseServerUrls is not null)
            {
                foreach (var u in LicenseServerUrls)
                    AddUnique(list, u);
            }
            return list;
        }
    }

    private static void AddUnique(List<string> list, string? raw)
    {
        var s = (raw ?? "").Trim().TrimEnd('/');
        if (s.Length == 0) return;
        foreach (var existing in list)
        {
            if (string.Equals(existing, s, StringComparison.OrdinalIgnoreCase))
                return;
        }
        list.Add(s);
    }

    /// <summary>Local json first, then extra bases (GitHub catalog / previous tunnel).</summary>
    public static IReadOnlyList<string> MergeUrls(IEnumerable<string?> first, IEnumerable<string?> extra)
    {
        var list = new List<string>();
        if (first is not null)
        {
            foreach (var u in first)
                AddUnique(list, u);
        }
        if (extra is not null)
        {
            foreach (var u in extra)
                AddUnique(list, u);
        }
        return list;
    }

    /// <summary>
    /// Configured URL, or loopback for writing next to <c>publish-v2</c>.
    /// Heartbeat itself still requires <see cref="IsConfigured"/> (source tree stays skipped).
    /// </summary>
    public string ResolveHeartbeatUrl() =>
        IsConfigured ? NormalizedBaseUrl : LocalLoopbackUrl;

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
