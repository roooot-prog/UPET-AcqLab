using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Spider8DAQ.Core.Licensing;

/// <summary>
/// Licență personală offline UPET AcqLab.
/// Format: UPET-ACQLAB-XXXX-XXXX-XXXX-XXXX (HMAC).
/// </summary>
public static class UpetLicense
{
    public const string ProductId = "UPET-AcqLab";
    public const string KeyPrefix = "UPET-ACQLAB";

    private static readonly byte[] Secret = Encoding.UTF8.GetBytes(
        "UPET-AcqLab-v1/Universitatea-din-Petrosani/2026-personal-series#K7mQ2nR9");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Crockford-like (no I, L, O, U, 0, 1)
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly DateTime ExpEpoch = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static string AppDataDirectory => AppPaths.Root;

    public static string LicenseFilePath => Path.Combine(AppDataDirectory, "license.dat");

    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        return Regex.Replace(name.Trim().ToUpperInvariant(), @"\s+", " ");
    }

    public static string NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        return key.Trim().ToUpperInvariant().Replace(" ", "").Replace('_', '-');
    }

    public static string GenerateKey(string licenseeName, DateTime? expiresUtc = null)
    {
        var name = NormalizeName(licenseeName);
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Numele titularului este obligatoriu.", nameof(licenseeName));

        var nonce = RandomNumberGenerator.GetBytes(3);
        ushort expDays = 0;
        var expToken = "NONE";
        if (expiresUtc is not null)
        {
            var days = (int)Math.Ceiling((expiresUtc.Value.ToUniversalTime().Date - ExpEpoch).TotalDays);
            days = Math.Clamp(days, 1, 65535);
            expDays = (ushort)days;
            expToken = ExpEpoch.AddDays(expDays).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        var nonceHex = Convert.ToHexString(nonce);
        var mac = Hmac($"v1|{ProductId}|{name}|{expToken}|{nonceHex}");

        // 10 bytes → 16 alphabet chars (80 bits)
        var packed = new byte[10];
        packed[0] = nonce[0];
        packed[1] = nonce[1];
        packed[2] = nonce[2];
        packed[3] = (byte)(expDays >> 8);
        packed[4] = (byte)(expDays & 0xFF);
        Buffer.BlockCopy(mac, 0, packed, 5, 5);

        var body = EncodeAlphabet(packed, 16);
        return $"{KeyPrefix}-{body[..4]}-{body[4..8]}-{body[8..12]}-{body[12..16]}";
    }

    public static bool TryValidateKey(string key, string licenseeName, out string error)
    {
        error = "";
        var name = NormalizeName(licenseeName);
        if (string.IsNullOrEmpty(name))
        {
            error = "Introduceți numele titularului licenței.";
            return false;
        }

        if (!TryParseKeyBody(NormalizeKey(key), out var body, out error))
            return false;

        if (!TryDecodeAlphabet(body, 10, out var packed))
        {
            error = "Cheia de licență are un format invalid.";
            return false;
        }

        var nonce = new[] { packed[0], packed[1], packed[2] };
        var expDays = (ushort)((packed[3] << 8) | packed[4]);
        string expToken;
        if (expDays == 0)
        {
            expToken = "NONE";
        }
        else
        {
            var expDate = ExpEpoch.AddDays(expDays);
            if (DateTime.UtcNow.Date > expDate.Date)
            {
                error = "Licența a expirat (" + expDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ").";
                return false;
            }
            expToken = expDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        var mac = Hmac($"v1|{ProductId}|{name}|{expToken}|{Convert.ToHexString(nonce)}");
        for (var i = 0; i < 5; i++)
        {
            if (packed[5 + i] != mac[i])
            {
                error = "Cheia nu este validă pentru acest nume (serie UPET AcqLab).";
                return false;
            }
        }
        return true;
    }

    public static LicenseRecord? LoadStored()
    {
        try
        {
            if (!File.Exists(LicenseFilePath)) return null;
            return JsonSerializer.Deserialize<LicenseRecord>(File.ReadAllText(LicenseFilePath), JsonOpts);
        }
        catch { return null; }
    }

    public static bool IsActivated(out LicenseRecord? record, out string statusMessage)
    {
        record = LoadStored();
        if (record is null || string.IsNullOrWhiteSpace(record.Key) || string.IsNullOrWhiteSpace(record.Licensee))
        {
            statusMessage = "Nu există licență activată.";
            return false;
        }
        if (!string.Equals(record.Product, ProductId, StringComparison.OrdinalIgnoreCase))
        {
            statusMessage = "Licența nu este pentru UPET AcqLab.";
            return false;
        }
        if (!TryValidateKey(record.Key, record.Licensee, out var err))
        {
            statusMessage = err;
            return false;
        }
        statusMessage = $"Licență activă: {record.Licensee}";
        return true;
    }

    public static void SaveActivation(string licenseeName, string key)
    {
        if (!TryValidateKey(key, licenseeName, out var err))
            throw new InvalidOperationException(err);

        Directory.CreateDirectory(AppDataDirectory);
        var record = new LicenseRecord
        {
            Product = ProductId,
            Licensee = licenseeName.Trim(),
            Key = NormalizeKey(key),
            ActivatedUtc = DateTime.UtcNow,
            MachineName = Environment.MachineName
        };
        File.WriteAllText(LicenseFilePath, JsonSerializer.Serialize(record, JsonOpts));
    }

    private static byte[] Hmac(string material)
    {
        using var h = new HMACSHA256(Secret);
        return h.ComputeHash(Encoding.UTF8.GetBytes(material));
    }

    private static bool TryParseKeyBody(string key, out string body, out string error)
    {
        body = "";
        error = "";
        var parts = key.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // UPET-ACQLAB-XXXX-XXXX-XXXX-XXXX
        if (parts.Length != 6 || parts[0] != "UPET" || parts[1] != "ACQLAB" ||
            parts[2].Length != 4 || parts[3].Length != 4 || parts[4].Length != 4 || parts[5].Length != 4)
        {
            error = "Format așteptat: UPET-ACQLAB-XXXX-XXXX-XXXX-XXXX";
            return false;
        }
        body = parts[2] + parts[3] + parts[4] + parts[5];
        foreach (var c in body)
        {
            if (Alphabet.IndexOf(c) < 0)
            {
                error = "Cheia conține caractere invalide.";
                return false;
            }
        }
        return true;
    }

    private static string EncodeAlphabet(byte[] data, int charCount)
    {
        var bits = 0;
        var value = 0;
        var sb = new StringBuilder(charCount);
        foreach (var b in data)
        {
            value = (value << 8) | b;
            bits += 8;
            while (bits >= 5 && sb.Length < charCount)
            {
                bits -= 5;
                sb.Append(Alphabet[(value >> bits) & 31]);
            }
        }
        if (sb.Length < charCount && bits > 0)
            sb.Append(Alphabet[(value << (5 - bits)) & 31]);
        while (sb.Length < charCount) sb.Append(Alphabet[0]);
        return sb.ToString()[..charCount];
    }

    private static bool TryDecodeAlphabet(string body, int byteCount, out byte[] packed)
    {
        packed = new byte[byteCount];
        var bits = 0;
        var value = 0;
        var idx = 0;
        foreach (var c in body)
        {
            var v = Alphabet.IndexOf(c);
            if (v < 0) return false;
            value = (value << 5) | v;
            bits += 5;
            while (bits >= 8 && idx < packed.Length)
            {
                bits -= 8;
                packed[idx++] = (byte)((value >> bits) & 0xFF);
            }
        }
        return idx >= byteCount;
    }
}

public sealed class LicenseRecord
{
    public string Product { get; set; } = UpetLicense.ProductId;
    public string Licensee { get; set; } = "";
    public string Key { get; set; } = "";
    public DateTime ActivatedUtc { get; set; }
    public string? MachineName { get; set; }
}
