using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Measurement fingerprint: verifiable seal of experiment context + signal payload.
/// Code form: UPET-XXXX-XXXX-XXXX (SHA-256 truncated, algorithm UPET-FP-v1).
/// </summary>
public static class MeasurementFingerprint
{
    public const string Algorithm = "UPET-FP-v1";
    public const string MetaKey = "MeasurementFingerprint";
    public const string PayloadKey = "FingerprintPayloadSha256";
    public const string AlgoKey = "FingerprintAlgo";

    public sealed class Seal
    {
        public string Code { get; init; } = "";
        public string PayloadSha256 { get; init; } = "";
        public string MetaSha256 { get; init; } = "";
        public string AlgorithmId { get; init; } = Algorithm;
    }

    public enum VerifyStatus
    {
        Missing,
        Valid,
        Altered,
        Incomplete
    }

    public sealed class VerifyResult
    {
        public VerifyStatus Status { get; init; }
        public string? StoredCode { get; init; }
        public string? ComputedCode { get; init; }
        public string Message { get; init; } = "";
    }

    public static Seal Compute(ProjectMeta meta, ReadOnlySpan<byte> payload)
    {
        var payloadHash = SHA256.HashData(payload);
        var metaCanon = CanonicalizeMeta(meta);
        var metaHash = SHA256.HashData(Encoding.UTF8.GetBytes(metaCanon));
        var combined = Encoding.UTF8.GetBytes(
            Algorithm + "|" +
            Convert.ToHexString(payloadHash) + "|" +
            Convert.ToHexString(metaHash));
        var full = SHA256.HashData(combined);
        return new Seal
        {
            Code = FormatCode(full),
            PayloadSha256 = Convert.ToHexString(payloadHash),
            MetaSha256 = Convert.ToHexString(metaHash),
            AlgorithmId = Algorithm
        };
    }

    public static Seal ComputeForSession(OfflineSession session, ProjectMeta meta)
        => Compute(meta, UpetReportFile.GetSamplePayload(session));

    public static string FormatCode(ReadOnlySpan<byte> sha256)
    {
        if (sha256.Length < 6)
            throw new ArgumentException("Hash too short.", nameof(sha256));
        var hex = Convert.ToHexString(sha256[..6]);
        return $"UPET-{hex[..4]}-{hex[4..8]}-{hex[8..12]}";
    }

    public static bool IsFingerprintComment(string line)
    {
        var t = line.TrimStart();
        if (!t.StartsWith('#')) return false;
        t = t[1..].TrimStart();
        return t.StartsWith(MetaKey + "=", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith(PayloadKey + "=", StringComparison.OrdinalIgnoreCase)
               || t.StartsWith(AlgoKey + "=", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryReadCodeFromCsv(string path, out string code)
    {
        code = "";
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        foreach (var line in File.ReadLines(path))
        {
            if (TryParseKeyedComment(line, MetaKey, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                code = value.Trim();
                return true;
            }
        }
        return false;
    }

    /// <summary>Append fingerprint footer to a finished CSV (hash = file bytes before append).</summary>
    public static Seal SealCsvFile(string path, ProjectMeta meta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("CSV lipsă pentru amprentă.", path);

        // Drop previous seal lines if re-sealing.
        StripFingerprintComments(path);
        var payload = File.ReadAllBytes(path);
        // Slim meta must match what VerifyCsvFile rebuilds from CSV # comments.
        var slim = new ProjectMeta
        {
            Operator = meta.Operator,
            SampleId = meta.SampleId,
            Backend = meta.Backend,
            SampleRateHz = meta.SampleRateHz,
            Comment = meta.Comment
        };
        var seal = Compute(slim, payload);
        using (var sw = new StreamWriter(path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            sw.Write("# ");
            sw.Write(MetaKey);
            sw.Write('=');
            sw.WriteLine(seal.Code);
            sw.Write("# ");
            sw.Write(PayloadKey);
            sw.Write('=');
            sw.WriteLine(seal.PayloadSha256);
            sw.Write("# ");
            sw.Write(AlgoKey);
            sw.Write('=');
            sw.WriteLine(seal.AlgorithmId);
        }

        ApplySealToMeta(meta, seal);
        return seal;
    }

    public static VerifyResult VerifyCsvFile(string path, ProjectMeta? metaForContext = null)
    {
        _ = metaForContext; // reserved for future strong meta re-check
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new VerifyResult { Status = VerifyStatus.Missing, Message = "Fișier CSV lipsă." };

        var allBytes = File.ReadAllBytes(path);
        var key = "# " + MetaKey + "=";
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var bytePos = IndexOfBytes(allBytes, keyBytes);
        if (bytePos < 0)
            return new VerifyResult { Status = VerifyStatus.Missing, Message = "Fără amprentă în CSV." };

        var text = Encoding.UTF8.GetString(allBytes);
        var textPos = text.LastIndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (textPos < 0)
            return new VerifyResult { Status = VerifyStatus.Missing, Message = "Fără amprentă în CSV." };

        var after = textPos + key.Length;
        var eol = text.IndexOfAny(['\r', '\n'], after);
        var stored = (eol < 0 ? text[after..] : text[after..eol]).Trim();
        if (string.IsNullOrWhiteSpace(stored))
            return new VerifyResult { Status = VerifyStatus.Incomplete, Message = "Amprentă goală." };

        string? storedPayloadHex = null;
        foreach (var line in File.ReadLines(path))
        {
            if (TryParseKeyedComment(line, PayloadKey, out var ph))
                storedPayloadHex = ph.Trim();
        }

        var payload = allBytes.AsSpan(0, bytePos);
        var payloadHex = Convert.ToHexString(SHA256.HashData(payload));
        if (!string.IsNullOrWhiteSpace(storedPayloadHex) &&
            !string.Equals(payloadHex, storedPayloadHex, StringComparison.OrdinalIgnoreCase))
        {
            return new VerifyResult
            {
                Status = VerifyStatus.Altered,
                StoredCode = stored,
                Message = "Amprentă alterată — conținutul CSV nu mai coincide cu sigiliul."
            };
        }

        // Recompute full code with slim meta from CSV comments (self-contained).
        var slim = new ProjectMeta();
        TryFillMetaFromCsvComments(path, slim);
        var computed = Compute(slim, payload);
        if (!string.Equals(computed.Code, stored, StringComparison.OrdinalIgnoreCase))
        {
            // Payload hash OK but code mismatch → sealed with richer meta; still Valid if payload OK.
            if (!string.IsNullOrWhiteSpace(storedPayloadHex) &&
                string.Equals(payloadHex, storedPayloadHex, StringComparison.OrdinalIgnoreCase))
            {
                return new VerifyResult
                {
                    Status = VerifyStatus.Valid,
                    StoredCode = stored,
                    ComputedCode = computed.Code,
                    Message = "Amprentă validă — CSV nemodificat (sigiliu payload)."
                };
            }

            return new VerifyResult
            {
                Status = VerifyStatus.Altered,
                StoredCode = stored,
                ComputedCode = computed.Code,
                Message = "Amprentă alterată — CSV sau metadate nu mai coincid."
            };
        }

        return new VerifyResult
        {
            Status = VerifyStatus.Valid,
            StoredCode = stored,
            ComputedCode = computed.Code,
            Message = "Amprentă validă — CSV nemodificat de la sigilare."
        };
    }

    public static VerifyResult VerifySession(OfflineSession session, ProjectMeta meta)
    {
        var stored = meta.MeasurementFingerprint?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(stored))
            return new VerifyResult { Status = VerifyStatus.Missing, Message = "Raport fără amprentă." };

        var computed = ComputeForSession(session, meta);
        if (string.Equals(computed.Code, stored, StringComparison.OrdinalIgnoreCase))
        {
            return new VerifyResult
            {
                Status = VerifyStatus.Valid,
                StoredCode = stored,
                ComputedCode = computed.Code,
                Message = "Amprentă validă — raport .upet intact."
            };
        }

        return new VerifyResult
        {
            Status = VerifyStatus.Altered,
            StoredCode = stored,
            ComputedCode = computed.Code,
            Message = "Amprentă alterată — semnal sau context modificat."
        };
    }

    public static void ApplySealToMeta(ProjectMeta meta, Seal seal)
    {
        meta.MeasurementFingerprint = seal.Code;
        meta.FingerprintPayloadSha256 = seal.PayloadSha256;
        meta.FingerprintAlgo = seal.AlgorithmId;
    }

    public const string LabelNeverificata = "Neverificată";

    public static string StatusLabelRo(VerifyStatus status) => status switch
    {
        VerifyStatus.Valid => "Validă",
        VerifyStatus.Altered => "Alterată",
        VerifyStatus.Incomplete => "Incompletă",
        _ => "Lipsă"
    };

    /// <summary>
    /// Report-friendly status: verify session/CSV when possible; never leave "—".
    /// Fingerprint present but unverifiable → <see cref="LabelNeverificata"/>.
    /// </summary>
    /// <remarks>
    /// CSV seals use slim meta + raw file bytes; .upet seals use rich meta + packed samples.
    /// Prefer <see cref="VerifyCsvFile"/> when a CSV path exists — calling
    /// <see cref="VerifySession"/> against a CSV-sealed code with <c>CurrentProjectMeta()</c>
    /// always yields a false <see cref="VerifyStatus.Altered"/>.
    /// UI phrases like "Amprentă generată" are not trusted. Negative UI badges
    /// ("Alterată" / "Incompletă") are also not trusted — they often come from an earlier
    /// false-positive VerifySession and must be re-checked against the sealed CSV.
    /// </remarks>
    public static string ResolveReportStatusLabel(
        OfflineSession? session,
        ProjectMeta meta,
        string? explicitLabel = null,
        string? csvPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitLabel))
        {
            var t = explicitLabel.Trim();
            if (t is not ("—" or "-" or "–"))
            {
                var normalized = NormalizeKnownLabel(t);
                // Only short-circuit on non-negative known words. Never trust Alterată /
                // Incompletă from UI — re-verify (CSV preferred) to avoid sticky false positives.
                if (IsTrustedPositiveStatusLabel(normalized))
                    return normalized;
            }
        }

        var hasCode = !string.IsNullOrWhiteSpace(meta.MeasurementFingerprint);
        try
        {
            var path = ResolveExistingCsvPath(csvPath, session?.SourcePath);

            // CSV seal ≠ session/.upet seal (payload format + slim vs rich meta).
            if (path is not null)
            {
                var vr = VerifyCsvFile(path, meta);
                if (vr.Status != VerifyStatus.Missing)
                    return StatusLabelRo(vr.Status);
                // Code only in project meta, not in this CSV → try session below.
            }

            if (session is not null && hasCode)
                return StatusLabelRo(VerifySession(session, meta).Status);

            if (path is not null && hasCode)
                return LabelNeverificata;
        }
        catch
        {
            if (hasCode) return LabelNeverificata;
        }

        if (!hasCode)
            return StatusLabelRo(VerifyStatus.Missing);

        return LabelNeverificata;
    }

    /// <summary>
    /// Prefer an explicit CSV path; otherwise use <see cref="OfflineSession.SourcePath"/>,
    /// stripping ExportRegion suffixes like <c>file.csv#[0,10)</c>.
    /// </summary>
    public static string? ResolveExistingCsvPath(string? preferredPath, string? sessionSourcePath = null)
    {
        foreach (var raw in new[] { preferredPath, sessionSourcePath })
        {
            var candidate = StripSourcePathFragment(raw);
            if (candidate is null) continue;
            if (!candidate.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static string? StripSourcePathFragment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var p = path.Trim();
        var hash = p.IndexOf('#');
        if (hash >= 0) p = p[..hash];
        return string.IsNullOrWhiteSpace(p) ? null : p;
    }

    /// <summary>
    /// Positive/neutral report words only — not UI phrases ("Amprentă generată") and not
    /// sticky negatives ("Alterată") that must be re-verified against the sealed CSV.
    /// </summary>
    private static bool IsTrustedPositiveStatusLabel(string label)
        => label is "Validă" or LabelNeverificata or "Lipsă";

    public static string StatusColorHex(string? labelRo)
    {
        var key = NormalizeKnownLabel(labelRo ?? "");
        return key switch
        {
            "Validă" => "#2E7D32",
            "Alterată" => "#C62828",
            "Incompletă" => "#E65100",
            LabelNeverificata => "#607D8B",
            _ => "#607D8B" // Lipsă
        };
    }

    public static (double R, double G, double B) StatusColorRgb(string? labelRo)
    {
        var hex = StatusColorHex(labelRo).TrimStart('#');
        if (hex.Length != 6) return (0.38, 0.49, 0.55);
        return (
            Convert.ToInt32(hex[..2], 16) / 255.0,
            Convert.ToInt32(hex[2..4], 16) / 255.0,
            Convert.ToInt32(hex[4..6], 16) / 255.0);
    }

    /// <summary>ASCII marker for Helvetica PDF when color alone is insufficient.</summary>
    public static string StatusAsciiMarker(string? labelRo) => NormalizeKnownLabel(labelRo ?? "") switch
    {
        "Validă" => "[OK]",
        "Alterată" => "[ALTERAT]",
        "Incompletă" => "[INCOMPLET]",
        LabelNeverificata => "[NEVERIF]",
        _ => "[LIPSA]"
    };

    /// <summary>PDF cell text: marker + Romanian word (diacritics folded by caller via PdfSafe).</summary>
    public static string FormatStatusForPdf(string? labelRo)
    {
        var label = NormalizeKnownLabel(labelRo ?? StatusLabelRo(VerifyStatus.Missing));
        return StatusAsciiMarker(label) + " " + label;
    }

    private static string NormalizeKnownLabel(string raw)
    {
        var t = raw.Trim();
        if (t.Length == 0) return StatusLabelRo(VerifyStatus.Missing);
        // Accept UI fragments like "Validă · UPET-…"
        var head = t.Split('·', '|')[0].Trim();
        if (head.StartsWith("Verificare", StringComparison.OrdinalIgnoreCase))
            return LabelNeverificata;

        static bool Eq(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        if (Eq(head, "Validă") || Eq(head, "Valida") || Eq(head, "OK") || Eq(head, "[OK]"))
            return "Validă";
        if (Eq(head, "Alterată") || Eq(head, "Alterata") || Eq(head, "[ALTERAT]"))
            return "Alterată";
        if (Eq(head, "Incompletă") || Eq(head, "Incompleta") || Eq(head, "[INCOMPLET]"))
            return "Incompletă";
        if (Eq(head, LabelNeverificata) || Eq(head, "Neverificata") || Eq(head, "[NEVERIF]"))
            return LabelNeverificata;
        if (Eq(head, "Lipsă") || Eq(head, "Lipsa") || Eq(head, "[LIPSA]") || Eq(head, "Missing"))
            return "Lipsă";

        // Marker-prefixed PDF text already normalized
        if (head.StartsWith("[OK]", StringComparison.OrdinalIgnoreCase)) return "Validă";
        if (head.StartsWith("[ALTERAT]", StringComparison.OrdinalIgnoreCase)) return "Alterată";
        if (head.StartsWith("[INCOMPLET]", StringComparison.OrdinalIgnoreCase)) return "Incompletă";
        if (head.StartsWith("[NEVERIF]", StringComparison.OrdinalIgnoreCase)) return LabelNeverificata;
        if (head.StartsWith("[LIPSA]", StringComparison.OrdinalIgnoreCase)) return "Lipsă";

        return head;
    }

    /// <summary>Canonical meta text (excludes fingerprint fields themselves).</summary>
    public static string CanonicalizeMeta(ProjectMeta meta)
    {
        var sb = new StringBuilder(512);
        void Line(string k, string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return;
            sb.Append(k).Append('=').Append(Normalize(v)).Append('\n');
        }

        Line("Operator", meta.Operator);
        Line("SampleId", meta.SampleId);
        Line("SampleLengthMm", meta.SampleLengthMm > 0
            ? meta.SampleLengthMm.ToString(CultureInfo.InvariantCulture) : null);
        Line("SampleWidthMm", meta.SampleWidthMm > 0
            ? meta.SampleWidthMm.ToString(CultureInfo.InvariantCulture) : null);
        Line("SampleThicknessMm", meta.SampleThicknessMm > 0
            ? meta.SampleThicknessMm.ToString(CultureInfo.InvariantCulture) : null);
        Line("SampleDiameterMm", meta.SampleDiameterMm > 0
            ? meta.SampleDiameterMm.ToString(CultureInfo.InvariantCulture) : null);
        Line("SampleAreaMm2", meta.SampleAreaMm2 > 0
            ? meta.SampleAreaMm2.ToString(CultureInfo.InvariantCulture) : null);
        Line("SampleMassG", meta.SampleMassG > 0
            ? meta.SampleMassG.ToString(CultureInfo.InvariantCulture) : null);
        Line("SampleDimensionsSummary", meta.SampleDimensionsSummary);
        if (!double.IsNaN(meta.ApparentPoissonNu) && !double.IsInfinity(meta.ApparentPoissonNu))
            Line("ApparentPoissonNu", meta.ApparentPoissonNu.ToString(CultureInfo.InvariantCulture));
        Line("ApparentPoissonSummary", meta.ApparentPoissonSummary);
        Line("ElasticRecoverySummary", meta.ElasticRecoverySummary);
        Line("ProjectName", meta.ProjectName);
        Line("Backend", meta.Backend);
        Line("SampleRateHz", meta.SampleRateHz > 0
            ? meta.SampleRateHz.ToString(CultureInfo.InvariantCulture) : null);
        Line("ExperimentName", meta.ExperimentName);
        Line("ExperimentType", meta.ExperimentType);
        Line("SpecimenId", meta.SpecimenId);
        Line("SpecimenNameRo", meta.SpecimenNameRo);
        Line("SpecimenClass", meta.SpecimenClass);
        Line("SpecimenFormulaPack", meta.SpecimenFormulaPack);
        Line("SpecimenSummary", meta.SpecimenSummary);
        Line("SpecimenYoungGPa", meta.SpecimenYoungGPa > 0
            ? meta.SpecimenYoungGPa.ToString(CultureInfo.InvariantCulture) : null);
        Line("SpecimenPoissonNu", meta.SpecimenPoissonNu > 0
            ? meta.SpecimenPoissonNu.ToString(CultureInfo.InvariantCulture) : null);
        Line("PlannedSensors", meta.PlannedSensors);
        Line("SensorSummary", meta.SensorSummary);
        Line("Comment", meta.Comment);
        Line("Location", meta.Location);
        Line("ExperimentStartLocal", meta.ExperimentStartLocal);
        Line("ExperimentEndLocal", meta.ExperimentEndLocal);
        Line("MontageBeforeNotes", meta.MontageBeforeNotes);
        Line("MontageAfterNotes", meta.MontageAfterNotes);
        Line("MontageBeforeSha", FileSha256Hex(meta.MontagePhotoPath));
        Line("MontageAfterSha", FileSha256Hex(meta.MontagePhotoAfterPath));
        Line("ActiveChannelCount", meta.ActiveChannelCount > 0
            ? meta.ActiveChannelCount.ToString(CultureInfo.InvariantCulture) : null);

        if (meta.Channels is { Count: > 0 })
        {
            foreach (var ch in meta.Channels.OrderBy(c => c.Index).ThenBy(c => c.Name, StringComparer.Ordinal))
            {
                sb.Append("CH|")
                    .Append(ch.Index.ToString(CultureInfo.InvariantCulture)).Append('|')
                    .Append(Normalize(ch.Name)).Append('|')
                    .Append(Normalize(ch.Unit)).Append('|')
                    .Append(ch.Enabled ? '1' : '0').Append('|')
                    .Append(ch.RecordEnabled ? '1' : '0').Append('|')
                    .Append(Normalize(ch.SensorId)).Append('|')
                    .Append(Normalize(ch.SensorName)).Append('|')
                    .Append(ch.Scale.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
                    .Append(ch.Offset.ToString("G17", CultureInfo.InvariantCulture)).Append('|')
                    .Append(ch.TareValue.ToString("G17", CultureInfo.InvariantCulture))
                    .Append('\n');
            }
        }

        return sb.ToString();
    }

    private static void StripFingerprintComments(string path)
    {
        var lines = File.ReadAllLines(path);
        var kept = lines.Where(l => !IsFingerprintComment(l)).ToArray();
        if (kept.Length == lines.Length) return;
        File.WriteAllLines(path, kept, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void TryFillMetaFromCsvComments(string path, ProjectMeta meta)
    {
        foreach (var line in File.ReadLines(path))
        {
            var t = line.TrimStart();
            if (!t.StartsWith('#')) continue;
            t = t[1..].TrimStart();
            if (IsFingerprintComment("# " + t)) continue;
            var eq = t.IndexOf('=');
            if (eq <= 0) continue;
            var key = t[..eq].Trim();
            var val = t[(eq + 1)..].Trim();
            switch (key)
            {
                case "Operator": meta.Operator = val; break;
                case "Sample": meta.SampleId = val; break;
                case "Backend": meta.Backend = val; break;
                case "Comment": meta.Comment = val; break;
                case "ExperimentType": meta.ExperimentType = val; break;
                case "SampleDiameterMm" when double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var d):
                    meta.SampleDiameterMm = d;
                    break;
                case "RateHzSet" when int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hz):
                    meta.SampleRateHz = hz;
                    break;
                    case "ContourConfig":
                        var cfg = CylinderContourExport.TryParseCsvCommentValue(val);
                        if (cfg is not null)
                            meta.CylinderContour = cfg;
                        break;
                    default:
                        Spider8DAQ.Core.Specimens.SpecimenIdentification.TryApplyCsvKey(meta, key, val);
                        break;
            }
        }
    }

    private static bool TryParseKeyedComment(string line, string key, out string value)
    {
        value = "";
        var t = line.TrimStart();
        if (!t.StartsWith('#')) return false;
        t = t[1..].TrimStart();
        if (!t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return false;
        value = t[(key.Length + 1)..].Trim();
        return true;
    }

    private static string? FileSha256Hex(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs));
        }
        catch
        {
            return null;
        }
    }

    private static string Normalize(string? s)
        => (s ?? "").Trim().Replace('\r', ' ').Replace('\n', ' ');

    private static int IndexOfBytes(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var ok = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { ok = false; break; }
            }
            if (ok) return i;
        }
        return -1;
    }
}
