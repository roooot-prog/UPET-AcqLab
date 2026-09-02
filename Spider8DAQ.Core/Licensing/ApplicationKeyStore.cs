using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spider8DAQ.Core.Licensing;

/// <summary>Local cache of a server-accepted application key (hash only — not the raw key).</summary>
public sealed class ApplicationKeyRecord
{
    public string KeyHash { get; set; } = "";
    public string KeyLast4 { get; set; } = "";
    public string MachineIdHash { get; set; } = "";
    public DateTime AcceptedUtc { get; set; }
    public DateTime LastOkUtc { get; set; }
    public string? Hostname { get; set; }
}

public static class ApplicationKeyStore
{
    public const string FileName = "app-key.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Test override; production uses %LocalAppData%\UPETAcqLab\.</summary>
    public static string? OverrideDirectory { get; set; }

    public static string FilePath =>
        Path.Combine(OverrideDirectory ?? AppPaths.Root, FileName);

    public static ApplicationKeyRecord? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<ApplicationKeyRecord>(File.ReadAllText(FilePath), JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public static bool HasAcceptedCache()
    {
        var rec = Load();
        return rec is not null && !string.IsNullOrWhiteSpace(rec.KeyHash);
    }

    public static void SaveAccepted(string keyHash, string keyLast4, string machineIdHash, string? hostname)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var now = DateTime.UtcNow;
        var existing = Load();
        var rec = new ApplicationKeyRecord
        {
            KeyHash = keyHash,
            KeyLast4 = keyLast4,
            MachineIdHash = machineIdHash,
            Hostname = hostname,
            AcceptedUtc = existing?.AcceptedUtc is { Year: > 2000 } a ? a : now,
            LastOkUtc = now
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(rec, JsonOpts));
    }

    public static void Clear()
    {
        try
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
        catch
        {
            // ignore
        }
    }
}
