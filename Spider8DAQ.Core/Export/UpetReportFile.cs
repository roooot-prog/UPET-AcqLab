using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Proprietary UPET AcqLab report container (.upet).
/// Binary + Deflate + AES — not readable by Excel/CSV viewers; only this app opens it.
/// Note: security is app-bound obfuscation, not DRM against reverse engineering.
/// </summary>
public static class UpetReportFile
{
    public const string Extension = ".upet";
    /// <summary>Legacy extension kept for opening older exports.</summary>
    public const string LegacyExtension = ".upetr";
    /// <summary>Save / Open dedicated .upet dialog (plus legacy + All files).</summary>
    public const string FileFilter =
        "Raport UPET AcqLab (*.upet)|*.upet|" +
        "Raport UPET (vechi *.upetr)|*.upetr|" +
        "Toate fișierele|*.*";
    /// <summary>Analysis Load: show .upet and CSV together by default.</summary>
    public const string AnalysisOpenFilter =
        "Date analiză (*.upet;*.csv)|*.upet;*.csv;*.upetr|" +
        "Raport UPET AcqLab (*.upet)|*.upet|" +
        "Raport UPET (vechi *.upetr)|*.upetr|" +
        "CSV (*.csv)|*.csv|" +
        "Toate fișierele|*.*";
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("UPETRPT1");
    /// <summary>v1 = samples only; v2 = samples + embedded full-duration graphs (PNG) + optional montage photos.</summary>
    private const ushort FormatVersion = 2;
    private const ushort MinReadableVersion = 1;
    public const string RoleMontageBefore = "montage_before";
    public const string RoleMontageAfter = "montage_after";
    // App-private material (not a user password). Changed only with intentional format bump.
    private static readonly byte[] AppMaterial = Encoding.UTF8.GetBytes(
        "UPET-AcqLab/Universitatea-din-Petrosani/Spider8DAQ/report-v1");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // ProjectMeta.ApparentPoissonNu defaults to NaN — without this, every .upet Save throws.
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static bool HasReportExtension(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(LegacyExtension, StringComparison.OrdinalIgnoreCase);

    public sealed class ReportEnvelope
    {
        public ushort FormatVersion { get; set; } = UpetReportFile.FormatVersion;
        public string App { get; set; } = "UPET AcqLab";
        public string SoftwareAuthor { get; set; } = ReportHeaderHelper.SoftwareAuthor;
        public string AuthorLine { get; set; } = ReportHeaderHelper.AuthorLine;
        public string CreatedLocal { get; set; } = "";
        public ProjectMeta? Meta { get; set; }
        public List<string> ChannelNames { get; set; } = new();
        public int SampleCount { get; set; }
        public string? SourcePath { get; set; }
        public string Note { get; set; } =
            "Fișier proprietar UPET AcqLab — deschideți doar în această aplicație (Analiză → Open .upet).";
    }

    public static void Save(
        string path,
        OfflineSession session,
        ProjectMeta? meta = null,
        IReadOnlyList<(string Role, string Name, byte[] PngBytes)>? graphs = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.Timestamps.Count == 0)
            throw new InvalidOperationException("Sesiunea nu are eșantioane.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        var envelope = new ReportEnvelope
        {
            FormatVersion = FormatVersion,
            CreatedLocal = DateTime.Now.ToString("O"),
            Meta = meta ?? new ProjectMeta(),
            ChannelNames = session.ChannelNames.ToList(),
            SampleCount = session.Timestamps.Count,
            SourcePath = session.SourcePath,
            Note = graphs is { Count: > 0 }
                ? "Fișier proprietar UPET AcqLab cu grafice (+ poze montaj dacă există) — doar UPET AcqLab."
                : "Fișier proprietar UPET AcqLab — deschideți doar în această aplicație (Analiză → Open .upet)."
        };

        // Seal measurement fingerprint into meta before writing cleartext envelope.
        var plain = PackSamples(session);
        var seal = MeasurementFingerprint.Compute(envelope.Meta!, plain);
        MeasurementFingerprint.ApplySealToMeta(envelope.Meta!, seal);

        var metaBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, JsonOpts));
        var compressed = Deflate(plain);
        DeriveKeyIv(out var key, out var iv);
        var cipher = AesEncrypt(compressed, key, iv);

        var assets = (graphs ?? Array.Empty<(string, string, byte[])>())
            .Where(g => g.PngBytes is { Length: > 0 })
            .Select(g => (g.Role, g.Name, g.PngBytes))
            .ToList();
        foreach (var photo in CollectMontageAssets(meta ?? envelope.Meta))
            assets.Add(photo);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);
        bw.Write(Magic);
        bw.Write(FormatVersion);
        bw.Write((ushort)(assets.Count > 0 ? 3 : 1)); // bit0 encrypted, bit1 has graphs
        bw.Write(metaBytes.Length);
        bw.Write(metaBytes);
        bw.Write(cipher.Length);
        bw.Write(cipher);
        bw.Write(Crc32(plain));
        bw.Write(assets.Count);
        foreach (var (role, name, bytes) in assets)
        {
            WriteUtf8(bw, role ?? "graph");
            WriteUtf8(bw, name ?? "plot");
            bw.Write(bytes.Length);
            bw.Write(bytes);
        }
    }

    /// <summary>Embed before/after montage photos as binary assets (self-contained .upet).</summary>
    public static List<(string Role, string Name, byte[] Bytes)> CollectMontageAssets(ProjectMeta? meta)
    {
        var list = new List<(string Role, string Name, byte[] Bytes)>();
        TryAddMontage(list, meta?.MontagePhotoPath, RoleMontageBefore, "before");
        TryAddMontage(list, meta?.MontagePhotoAfterPath, RoleMontageAfter, "after");
        return list;
    }

    private static void TryAddMontage(
        List<(string Role, string Name, byte[] Bytes)> list,
        string? path,
        string role,
        string nameHint)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0) return;
            var ext = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";
            list.Add((role, nameHint + ext.ToLowerInvariant(), bytes));
        }
        catch
        {
            /* skip unreadable photo */
        }
    }

    public static OfflineSession Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var br = new BinaryReader(fs);
        var magic = br.ReadBytes(8);
        if (magic.Length != 8 || !magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("Nu este un raport UPET AcqLab (.upet).");

        var ver = br.ReadUInt16();
        if (ver < MinReadableVersion || ver > FormatVersion)
            throw new InvalidDataException($"Versiune .upet nesuportată ({ver}). Actualizați UPET AcqLab.");

        _ = br.ReadUInt16(); // flags
        var metaLen = br.ReadInt32();
        if (metaLen < 0 || metaLen > 16_000_000)
            throw new InvalidDataException("Antet .upet corupt.");
        var metaBytes = br.ReadBytes(metaLen);
        if (metaBytes.Length != metaLen)
            throw new InvalidDataException("Antet .upet incomplet.");

        var cipherLen = br.ReadInt32();
        if (cipherLen < 1 || cipherLen > 512_000_000)
            throw new InvalidDataException("Payload .upet corupt.");
        var cipher = br.ReadBytes(cipherLen);
        if (cipher.Length != cipherLen)
            throw new InvalidDataException("Payload .upet incomplet.");

        var expectedCrc = br.ReadUInt32();
        DeriveKeyIv(out var key, out var iv);
        var compressed = AesDecrypt(cipher, key, iv);
        var plain = Inflate(compressed);
        if (Crc32(plain) != expectedCrc)
            throw new InvalidDataException("Integritate .upet eșuată (CRC). Fișier deteriorat sau străin.");

        var session = UnpackSamples(plain);
        session.SourcePath = path;
        ReportEnvelope? env = null;
        try
        {
            env = JsonSerializer.Deserialize<ReportEnvelope>(metaBytes, JsonOpts);
            if (env?.ChannelNames is { Count: > 0 } names && names.Count == session.ChannelNames.Count)
                session.ChannelNames = names;
            if (env?.Meta is not null)
                session.AttachedMeta = env.Meta;
        }
        catch
        {
            /* channel names from binary still valid */
        }

        // Optional trailing assets (v2+): graphs + montage photos.
        if (fs.Position < fs.Length)
        {
            try
            {
                var assetCount = br.ReadInt32();
                if (assetCount > 0 && assetCount <= 512)
                {
                    var extractDir = Path.Combine(
                        Path.GetTempPath(),
                        "UPETAcqLab_upet_graphs",
                        Path.GetFileNameWithoutExtension(path) + "_" + Guid.NewGuid().ToString("N")[..8]);
                    Directory.CreateDirectory(extractDir);
                    for (var i = 0; i < assetCount; i++)
                    {
                        var role = ReadUtf8(br);
                        var name = ReadUtf8(br);
                        var len = br.ReadInt32();
                        if (len < 1 || len > 80_000_000) break;
                        var bytes = br.ReadBytes(len);
                        if (bytes.Length != len) break;
                        var ext = Path.GetExtension(name);
                        if (string.IsNullOrWhiteSpace(ext))
                            ext = role.StartsWith("montage", StringComparison.OrdinalIgnoreCase) ? ".jpg" : ".png";
                        var safe = SanitizeFileToken(Path.GetFileNameWithoutExtension(name));
                        var file = Path.Combine(extractDir, $"{i:00}_{SanitizeFileToken(role)}_{safe}{ext}");
                        File.WriteAllBytes(file, bytes);
                        session.AttachedGraphs.Add((role, name, file));

                        session.AttachedMeta ??= new ProjectMeta();
                        if (string.Equals(role, RoleMontageBefore, StringComparison.OrdinalIgnoreCase))
                            session.AttachedMeta.MontagePhotoPath = file;
                        else if (string.Equals(role, RoleMontageAfter, StringComparison.OrdinalIgnoreCase))
                            session.AttachedMeta.MontagePhotoAfterPath = file;
                    }
                }
            }
            catch
            {
                /* older trailing garbage — ignore assets */
            }
        }

        session.CursorB = Math.Max(0, session.Timestamps.Count - 1);
        session.RecomputeStats();
        return session;
    }

    private static void WriteUtf8(BinaryWriter bw, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s ?? "");
        bw.Write(bytes.Length);
        bw.Write(bytes);
    }

    private static string ReadUtf8(BinaryReader br)
    {
        var len = br.ReadInt32();
        if (len < 0 || len > 4096) throw new InvalidDataException("String .upet invalid.");
        return Encoding.UTF8.GetString(br.ReadBytes(len));
    }

    private static string SanitizeFileToken(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var chars = (s ?? "x").Select(ch => bad.Contains(ch) ? '_' : ch).ToArray();
        var t = new string(chars).Trim();
        if (t.Length > 40) t = t[..40];
        return string.IsNullOrWhiteSpace(t) ? "graph" : t;
    }

    public static bool LooksLikeUpetReport(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (fs.Length < 16) return false;
            Span<byte> m = stackalloc byte[8];
            if (fs.Read(m) != 8) return false;
            return m.SequenceEqual(Magic);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Binary sample payload used by .upet and measurement fingerprints.</summary>
    public static byte[] GetSamplePayload(OfflineSession session) => PackSamples(session);

    private static byte[] PackSamples(OfflineSession session)
    {
        var n = session.Timestamps.Count;
        var ch = session.ChannelNames.Count;
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
        {
            bw.Write(n);
            bw.Write(ch);
            for (var i = 0; i < ch; i++)
            {
                var name = i < session.ChannelNames.Count ? session.ChannelNames[i] : $"CH{i}";
                var bytes = Encoding.UTF8.GetBytes(name);
                bw.Write(bytes.Length);
                bw.Write(bytes);
            }
            for (var i = 0; i < n; i++)
                bw.Write(session.Timestamps[i].ToUniversalTime().Ticks);
            for (var i = 0; i < n; i++)
                bw.Write(i < session.Sequences.Count ? session.Sequences[i] : i);
            for (var c = 0; c < ch; c++)
            {
                var col = c < session.Columns.Count ? session.Columns[c] : Array.Empty<double>();
                for (var i = 0; i < n; i++)
                    bw.Write(i < col.Length ? col[i] : double.NaN);
            }
        }
        return ms.ToArray();
    }

    private static OfflineSession UnpackSamples(byte[] plain)
    {
        using var ms = new MemoryStream(plain, writable: false);
        using var br = new BinaryReader(ms, Encoding.UTF8);
        var n = br.ReadInt32();
        var ch = br.ReadInt32();
        if (n < 0 || ch < 0 || n > 50_000_000 || ch > 512)
            throw new InvalidDataException("Dimensiuni .upet invalide.");

        var names = new List<string>(ch);
        for (var i = 0; i < ch; i++)
        {
            var len = br.ReadInt32();
            if (len < 0 || len > 4096) throw new InvalidDataException("Nume canal invalid.");
            names.Add(Encoding.UTF8.GetString(br.ReadBytes(len)));
        }

        var stamps = new List<DateTime>(n);
        for (var i = 0; i < n; i++)
            stamps.Add(new DateTime(br.ReadInt64(), DateTimeKind.Utc).ToLocalTime());

        var seq = new List<long>(n);
        for (var i = 0; i < n; i++)
            seq.Add(br.ReadInt64());

        var cols = new List<double[]>(ch);
        for (var c = 0; c < ch; c++)
        {
            var col = new double[n];
            for (var i = 0; i < n; i++)
                col[i] = br.ReadDouble();
            cols.Add(col);
        }

        return new OfflineSession
        {
            ChannelNames = names,
            Timestamps = stamps,
            Sequences = seq,
            Columns = cols
        };
    }

    private static void DeriveKeyIv(out byte[] key, out byte[] iv)
    {
        var hash = SHA256.HashData(AppMaterial);
        key = hash;
        iv = SHA256.HashData(hash.AsSpan(0, 16).ToArray())[..16];
    }

    private static byte[] AesEncrypt(byte[] plain, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var enc = aes.CreateEncryptor();
        return enc.TransformFinalBlock(plain, 0, plain.Length);
    }

    private static byte[] AesDecrypt(byte[] cipher, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(cipher, 0, cipher.Length);
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var zs = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            zs.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static byte[] Inflate(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zs = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zs.CopyTo(output);
        return output.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
