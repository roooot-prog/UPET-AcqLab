using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace Spider8DAQ.Core.Licensing;

/// <summary>
/// Application-key hashing for the hosted license API (not the offline HMAC personal licence).
/// </summary>
public static class ApplicationKeyCrypto
{
    public const string KeyPrefix = "UPET-APP";
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        return key.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal).Replace('_', '-');
    }

    public static string HashUtf8(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string HashKey(string? key) => HashUtf8(NormalizeKey(key));

    public static string KeyLast4(string? key)
    {
        var n = NormalizeKey(key).Replace("-", "", StringComparison.Ordinal);
        if (n.Length < 4) return n;
        return n[^4..];
    }

    public static string MaskKey(string? key)
    {
        var n = NormalizeKey(key);
        var last4 = KeyLast4(n);
        if (string.IsNullOrEmpty(last4)) return "••••";
        return $"{KeyPrefix}-••••-••••-••••-{last4}";
    }

    public static bool LooksLikeApplicationKey(string? key)
    {
        var n = NormalizeKey(key);
        var parts = n.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6) return false;
        if (parts[0] != "UPET" || parts[1] != "APP") return false;
        for (var i = 2; i < 6; i++)
        {
            if (parts[i].Length != 4) return false;
            foreach (var c in parts[i])
            {
                if (Alphabet.IndexOf(c) < 0) return false;
            }
        }
        return true;
    }

    /// <summary>Stable hashed machine id — never send the raw identifier to the server.</summary>
    public static string HashedMachineId()
    {
        var guid = TryReadMachineGuid() ?? "";
        var material = string.Join('|',
            "UPET-AcqLab-machine-v1",
            Environment.MachineName,
            Environment.UserName,
            guid);
        return HashUtf8(material);
    }

    private static string? TryReadMachineGuid()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return null;
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var v = key?.GetValue("MachineGuid") as string;
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }
        catch
        {
            return null;
        }
    }
}
