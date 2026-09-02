using System.Globalization;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.DataViewer;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Sensors;

namespace Spider8DAQ.Core.Advisory;

/// <summary>
/// Finds previous recordings of the same experiment type in the local recordings folder
/// and builds cheap summaries (N, duration, Fmax / ε_max / ovalitate / bombare) for the advisor.
/// No cloud DB — CSV # comments + last 1–3 file scans only.
/// </summary>
public static class ExperimentPeerCatalog
{
    public const int DefaultMaxPeers = 3;
    public const int HeaderMetaMaxLines = 80;

    /// <summary>Read ExperimentType from the first CSV comment lines (does not load samples).</summary>
    public static string? ReadExperimentType(string path)
    {
        var meta = ReadHeaderMeta(path);
        return string.IsNullOrWhiteSpace(meta?.ExperimentType) ? null : meta!.ExperimentType.Trim();
    }

    public static ProjectMeta? ReadHeaderMeta(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return null;

            var meta = new ProjectMeta();
            var any = false;
            var n = 0;
            foreach (var raw in CsvSharedIO.EnumerateLines(path))
            {
                n++;
                if (n > HeaderMetaMaxLines) break;
                var t = raw.TrimStart();
                if (t.Length > 0 && t[0] == '\uFEFF') t = t[1..].TrimStart();
                if (string.IsNullOrWhiteSpace(t)) continue;
                if (!t.StartsWith('#')) break;
                t = t[1..].TrimStart();
                var eq = t.IndexOf('=');
                if (eq <= 0) continue;
                var key = t[..eq].Trim();
                var val = t[(eq + 1)..].Trim();
                switch (key)
                {
                    case "ExperimentType":
                        meta.ExperimentType = val; any = true; break;
                    case "Sample":
                        meta.SampleId = val; any = true; break;
                    case "SampleDiameterMm" when TryParseInv(val, out var d):
                        meta.SampleDiameterMm = d; any = true; break;
                    case "SampleLengthMm" when TryParseInv(val, out var l):
                        meta.SampleLengthMm = l; any = true; break;
                    case "ContourConfig":
                        var cfg = CylinderContourExport.TryParseCsvCommentValue(val);
                        if (cfg is not null)
                        {
                            meta.CylinderContour = cfg;
                            any = true;
                        }
                        break;
                    default:
                        if (Spider8DAQ.Core.Specimens.SpecimenIdentification.TryApplyCsvKey(meta, key, val))
                            any = true;
                        break;
                }
            }

            return any ? meta : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Summarize an already-loaded session (current live/offline) without touching disk.</summary>
    public static ExperimentSummary SummarizeSession(
        OfflineSession session,
        string? experimentType,
        ProjectMeta? meta = null)
    {
        meta ??= session.AttachedMeta;
        var type = FirstNonBlank(experimentType, meta?.ExperimentType);
        var duration = session.Timestamps.Count >= 2
            ? session.Timestamps[^1] - session.Timestamps[0]
            : TimeSpan.Zero;
        var s = new ExperimentSummary
        {
            FilePath = session.SourcePath ?? "",
            FileName = string.IsNullOrWhiteSpace(session.SourcePath)
                ? "(sesiune curentă)"
                : Path.GetFileName(session.SourcePath),
            ModifiedLocal = string.IsNullOrWhiteSpace(session.SourcePath) || !File.Exists(session.SourcePath)
                ? DateTime.Now
                : File.GetLastWriteTime(session.SourcePath),
            ExperimentType = type,
            SampleCount = session.Timestamps.Count,
            Duration = duration.Duration(),
            Fmax = TryForcePeak(session),
            EpsMax = TryStrainPeak(session)
        };
        return FillContourMetrics(s, session, meta, type);
    }

    /// <summary>
    /// Newest same-type CSV peers in <paramref name="folder"/>, excluding
    /// <paramref name="excludePath"/>. Metrics are computed only for the last 1–3 matches.
    /// </summary>
    public static IReadOnlyList<ExperimentSummary> FindPeers(
        string folder,
        string? experimentType,
        string? excludePath,
        int maxPeers = DefaultMaxPeers,
        bool computeMetrics = true)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(experimentType))
            return Array.Empty<ExperimentSummary>();
        if (!Directory.Exists(folder)) return Array.Empty<ExperimentSummary>();

        maxPeers = Math.Clamp(maxPeers, 1, 5);
        var exclude = NormalizePath(excludePath);
        var matches = new List<(string Path, DateTime Modified, string Type)>();

        foreach (var path in Directory.GetFiles(folder, "*.csv"))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith("_stats.csv", StringComparison.OrdinalIgnoreCase)) continue;
            if (PathsEqual(NormalizePath(path), exclude)) continue;

            var type = ReadExperimentType(path);
            if (!ExperimentTypes.AreSameKind(experimentType, type)) continue;

            DateTime modified;
            try { modified = File.GetLastWriteTime(path); }
            catch { continue; }
            matches.Add((path, modified, type!.Trim()));
        }

        var list = new List<ExperimentSummary>();
        foreach (var m in matches.OrderByDescending(x => x.Modified).Take(maxPeers))
        {
            try
            {
                list.Add(SummarizeFile(m.Path, m.Type, computeMetrics));
            }
            catch
            {
                list.Add(new ExperimentSummary
                {
                    FilePath = m.Path,
                    FileName = Path.GetFileName(m.Path),
                    ModifiedLocal = m.Modified,
                    ExperimentType = m.Type,
                    SampleCount = 0
                });
            }
        }

        return list;
    }

    public static ExperimentSummary SummarizeFile(string path, string? experimentType, bool computeMetrics)
    {
        RecordingEntry? probe = null;
        try { probe = RecordingIndex.Probe(path); } catch { /* keep going */ }

        var header = ReadHeaderMeta(path);
        var type = FirstNonBlank(experimentType, header?.ExperimentType);
        var s = new ExperimentSummary
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            ModifiedLocal = probe?.ModifiedLocal ?? File.GetLastWriteTime(path),
            ExperimentType = type,
            SampleCount = probe?.SampleCount ?? 0,
            Duration = probe?.Duration ?? TimeSpan.Zero
        };

        if (!computeMetrics) return s;

        try
        {
            var session = OfflineSession.FromCsv(path);
            s = SummarizeSession(session, type, header ?? session.AttachedMeta) with
            {
                FilePath = path,
                FileName = Path.GetFileName(path),
                ModifiedLocal = s.ModifiedLocal,
                SampleCount = s.SampleCount > 0 ? s.SampleCount : session.Timestamps.Count,
                Duration = s.Duration > TimeSpan.Zero ? s.Duration : session.Timestamps.Count >= 2
                    ? (session.Timestamps[^1] - session.Timestamps[0]).Duration()
                    : TimeSpan.Zero
            };
        }
        catch
        {
            /* N/duration from probe still useful */
        }

        return s;
    }

    /// <summary>Romanian one-liner comparing current vs the newest same-type peer (never mixes families).</summary>
    public static AdviceItem? Compare(
        ExperimentSummary current,
        IReadOnlyList<ExperimentSummary> peers,
        bool scanComplete,
        AdvisorMemory? memory = null,
        bool userAsked = false)
    {
        var type = current.ExperimentType;
        if (string.IsNullOrWhiteSpace(type)) return null;

        var compatible = peers
            .Where(p => ExperimentTypes.AreSameKind(type, p.ExperimentType))
            .ToList();

        if (compatible.Count == 0)
        {
            if (!scanComplete) return null;
            if (!userAsked && (memory?.TimesShown("peer-none") ?? 0) > 0) return null;
            if (memory?.IsRejected("peer-none") == true) return null;
            var family = ExperimentTypes.IsCylinderContour(type)
                ? "Contur"
                : ExperimentTypes.IsStrainGauges(type)
                    ? "Tensometrie"
                    : type;
            return new AdviceItem
            {
                Id = "peer-none",
                Title = "Fără test anterior de același tip",
                Suggestion = $"Nu există altă înregistrare «{family}» în folderul recordings — acesta e primul (sau singurul) de acest tip.",
                Severity = AdviceSeverity.Info,
                Steps = ["Folder recordings — comparația apare după următorul test de același tip."],
                Score = 0.72,
                ActionId = "recordings",
                ActionLabel = "Recordings"
            };
        }

        if (memory?.IsRejected("peer-compare") == true) return null;

        var peer = compatible[0];
        var date = peer.ModifiedLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var bits = new List<string>();
        var extra = new List<string>();

        if (ExperimentTypes.IsCylinderContour(type))
        {
            AddDelta(bits, "ovalitate", current.OvalityMm, peer.OvalityMm, "mm");
            AddDelta(bits, "bombare", current.BarrelingIndex, peer.BarrelingIndex, "");
            AddDelta(bits, "u_med", current.UMeanMm, peer.UMeanMm, "mm");
            AddDelta(bits, "u_max", current.UMaxMm, peer.UMaxMm, "mm");
            AddDelta(bits, "Fmax", current.Fmax, peer.Fmax, "");
            extra.Add("Doar teste «Compresiune cilindru – contur» — nu se compară cu tensometrie.");
        }
        else if (ExperimentTypes.IsStrainGauges(type))
        {
            AddDelta(bits, "ε_max", current.EpsMax, peer.EpsMax, "µm/m");
            extra.Add("Doar teste «Mărci tensometrice» — nu se compară cu contur/bombare.");
        }
        else
        {
            AddDelta(bits, "Fmax", current.Fmax, peer.Fmax, "");
            AddDelta(bits, "ε_max", current.EpsMax, peer.EpsMax, "µm/m");
        }

        if (current.SampleCount > 0 && peer.SampleCount > 0)
            extra.Add($"N={current.SampleCount:N0} vs {peer.SampleCount:N0}");
        if (current.Duration > TimeSpan.Zero && peer.Duration > TimeSpan.Zero)
            extra.Add($"durată {FormatDur(current.Duration)} vs {FormatDur(peer.Duration)}");

        string suggestion;
        if (bits.Count == 0)
        {
            suggestion = ExperimentTypes.IsStrainGauges(type)
                ? $"Tensometrie: N={current.SampleCount:N0} față de {date} (N={peer.SampleCount:N0})."
                : ExperimentTypes.IsCylinderContour(type)
                    ? $"Față de ultimul test Contur ({date}): N={current.SampleCount:N0} vs {peer.SampleCount:N0}."
                    : $"Față de {date}: N={current.SampleCount:N0} vs {peer.SampleCount:N0}.";
        }
        else if (ExperimentTypes.IsStrainGauges(type) && bits.Count == 1 && bits[0].Contains("similar", StringComparison.Ordinal))
            suggestion = $"Tensometrie: {bits[0]} cu {date}.";
        else if (ExperimentTypes.IsCylinderContour(type))
            suggestion = $"Față de ultimul test Contur ({date}): {string.Join("; ", bits)}.";
        else if (ExperimentTypes.IsStrainGauges(type))
            suggestion = $"Tensometrie față de {date}: {string.Join("; ", bits)}.";
        else
            suggestion = $"Față de {date}: {string.Join("; ", bits)}.";

        var title = ExperimentTypes.IsCylinderContour(type)
            ? "Comparație cu testul Contur anterior"
            : ExperimentTypes.IsStrainGauges(type)
                ? "Comparație tensometrie"
                : "Comparație cu testul anterior";

        var more = compatible.Count > 1 ? $" · {compatible.Count} teste de același tip" : "";
        return new AdviceItem
        {
            Id = "peer-compare",
            Title = title,
            Suggestion = suggestion + more,
            Severity = AdviceSeverity.Info,
            Steps = extra.Count == 0 ? new[] { Path.GetFileName(peer.FilePath) } : extra.ToArray(),
            Score = 1.05 + (memory?.Boost("peer-compare") ?? 0),
            ActionId = "recordings",
            ActionLabel = "Recordings"
        };
    }

    internal static void AddDelta(List<string> bits, string label, double? current, double? peer, string unit)
    {
        if (current is not { } c || peer is not { } p) return;
        if (!double.IsFinite(c) || !double.IsFinite(p)) return;
        var inv = CultureInfo.InvariantCulture;
        var curS = FormatNum(c, inv);
        var peerS = FormatNum(p, inv);
        var scale = Math.Max(Math.Abs(p), 1e-9);
        var rel = (c - p) / scale;
        var u = string.IsNullOrEmpty(unit) ? "" : " " + unit;
        string word;
        if (Math.Abs(rel) < 0.10) word = "similar";
        else if (rel > 0) word = "mai mare";
        else word = "mai mic";
        bits.Add($"{label} {curS} vs {peerS}{u} ({word})");
    }

    private static ExperimentSummary FillContourMetrics(
        ExperimentSummary s, OfflineSession session, ProjectMeta? meta, string type)
    {
        if (!ExperimentTypes.IsCylinderContour(type)) return s;
        if (meta is null) return s;
        try
        {
            if (!CylinderContourExport.ShouldAttempt(meta, session)) return s;
            var result = CylinderContourExport.TryCompute(session, meta);
            if (result is not { IsValid: true }) return s;
            return s with
            {
                OvalityMm = FiniteOrNull(result.OvalityMm),
                BarrelingIndex = FiniteOrNull(result.BarrelingIndex),
                UMeanMm = FiniteOrNull(result.UMeanMm),
                UMaxMm = FiniteOrNull(result.UMaxMm),
                Fmax = s.Fmax ?? (result.ForceAtIndex is double f && double.IsFinite(f) ? Math.Abs(f) : null)
            };
        }
        catch
        {
            return s;
        }
    }

    private static double? TryForcePeak(OfflineSession session)
    {
        double? best = null;
        for (var i = 0; i < session.Stats.Count; i++)
        {
            var name = i < session.ChannelNames.Count ? session.ChannelNames[i] : session.Stats[i].Name;
            var unit = UnitFromName(name);
            if (!LooksForce(name, unit)) continue;
            var peak = PeakAbs(session.Stats[i]);
            if (peak is null) continue;
            if (best is null || peak > best) best = peak;
        }
        return best;
    }

    private static double? TryStrainPeak(OfflineSession session)
    {
        double? best = null;
        for (var i = 0; i < session.Stats.Count; i++)
        {
            var name = i < session.ChannelNames.Count ? session.ChannelNames[i] : session.Stats[i].Name;
            var unit = UnitFromName(name);
            if (!StrainScale.IsStrainUnit(unit) && !LooksStrainName(name)) continue;
            var peak = PeakAbs(session.Stats[i]);
            if (peak is null) continue;
            if (best is null || peak > best) best = peak;
        }
        return best;
    }

    private static double? PeakAbs(ChannelStats st)
    {
        if (st.Count <= 0) return null;
        var a = Math.Abs(st.Max);
        var b = Math.Abs(st.Min);
        var p = Math.Max(a, b);
        return double.IsFinite(p) ? p : null;
    }

    private static bool LooksForce(string name, string unit)
    {
        if (unit.Equals("N", StringComparison.OrdinalIgnoreCase)
            || unit.Equals("kN", StringComparison.OrdinalIgnoreCase)
            || unit.Equals("lbf", StringComparison.OrdinalIgnoreCase))
            return true;
        return name.Contains("forț", StringComparison.OrdinalIgnoreCase)
               || name.Contains("fort", StringComparison.OrdinalIgnoreCase)
               || name.Contains("force", StringComparison.OrdinalIgnoreCase)
               || name.Contains("U2B", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksStrainName(string name) =>
        name.Contains("µm/m", StringComparison.OrdinalIgnoreCase)
        || name.Contains("um/m", StringComparison.OrdinalIgnoreCase)
        || name.Contains("SG", StringComparison.OrdinalIgnoreCase)
        || name.Contains("tensom", StringComparison.OrdinalIgnoreCase)
        || name.Contains("strain", StringComparison.OrdinalIgnoreCase);

    private static string UnitFromName(string name)
    {
        var a = name.IndexOf('[');
        var b = name.IndexOf(']');
        return a >= 0 && b > a ? name[(a + 1)..b] : "";
    }

    private static double? FiniteOrNull(double v) => double.IsFinite(v) ? v : null;

    private static string FormatNum(double v, CultureInfo inv) =>
        Math.Abs(v) >= 100 ? v.ToString("0.#", inv) : v.ToString("0.###", inv);

    private static string FormatDur(TimeSpan t) =>
        t.TotalMinutes >= 1 ? $"{t.TotalMinutes:0.#} min" : $"{t.TotalSeconds:0.#} s";

    private static string FirstNonBlank(params string?[] parts)
    {
        foreach (var p in parts)
            if (!string.IsNullOrWhiteSpace(p)) return p.Trim();
        return "";
    }

    private static bool TryParseInv(string val, out double d) =>
        double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    private static bool PathsEqual(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
