using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spider8DAQ.Core.Licensing;

/// <summary>Local cache of a server-accepted application key (hash + DPAPI blob — never the raw key in plaintext).</summary>
public sealed class ApplicationKeyRecord
{
    public string KeyHash { get; set; } = "";
    public string KeyLast4 { get; set; } = "";
    public string MachineIdHash { get; set; } = "";
    public DateTime AcceptedUtc { get; set; }
    public DateTime LastOkUtc { get; set; }
    public DateTime? ValidUntilUtc { get; set; }
    public string? Hostname { get; set; }

    /// <summary>DPAPI-protected raw key (CurrentUser). Not used by heartbeat (hash is enough).</summary>
    public string? KeyProtected { get; set; }
}

public static class ApplicationKeyStore
{
    public const string FileName = "app-key.json";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("UPET-AcqLab-app-key-v1");

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

    public static bool HasUnexpiredCache(DateTime? nowUtc = null)
    {
        var rec = Load();
        if (rec is null || string.IsNullOrWhiteSpace(rec.KeyHash))
            return false;
        return !ApplicationKeyValidity.IsExpired(rec.ValidUntilUtc, nowUtc);
    }

    public static void SaveAccepted(
        string keyHash,
        string keyLast4,
        string machineIdHash,
        string? hostname,
        string? rawKey = null,
        DateTime? validUntilUtc = null)
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var now = DateTime.UtcNow;
        var existing = Load();
        var until = validUntilUtc ?? existing?.ValidUntilUtc;
        var rec = new ApplicationKeyRecord
        {
            KeyHash = keyHash,
            KeyLast4 = keyLast4,
            MachineIdHash = machineIdHash,
            Hostname = hostname,
            AcceptedUtc = existing?.AcceptedUtc is { Year: > 2000 } a ? a : now,
            LastOkUtc = now,
            ValidUntilUtc = until,
            KeyProtected = ProtectRawKey(rawKey) ?? existing?.KeyProtected
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

    private static string? ProtectRawKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return null;
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            var bytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(rawKey),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return null;
        }
    }
}
