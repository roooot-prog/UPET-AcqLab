using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Spider8DAQ.Core.Projects;

/// <summary>Canonical experiment-type labels used in Start Exp / reports.</summary>
public static class ExperimentTypes
{
    /// <summary>Cylinder compression with circumferential contour at Fmax.</summary>
    public const string CylinderContour = "Compresiune cilindru – contur";

    /// <summary>Same Start Exp label as <c>SensorCategories.Strain</c> (mărci tensometrice).</summary>
    public const string StrainGauges = "Mărci tensometrice";

    /// <summary>
    /// True for cylinder/uniaxial compression experiment types (specimen library + formula packs).
    /// False for tensometrie, mixt, CWT-as-type, force-only, and other non-compression labels.
    /// </summary>
    public static bool IsCompression(string? experimentType)
    {
        if (string.IsNullOrWhiteSpace(experimentType)) return false;
        if (IsStrainGauges(experimentType)) return false;
        if (IsCylinderContour(experimentType)) return true;
        var t = NormalizeTypeKey(experimentType);
        if (t.Contains("tensometr", StringComparison.OrdinalIgnoreCase)) return false;
        if (t.Contains("cwt", StringComparison.OrdinalIgnoreCase)
            && !t.Contains("compres", StringComparison.OrdinalIgnoreCase))
            return false;
        return t.Contains("compresiune", StringComparison.OrdinalIgnoreCase)
               || t.Contains("compresie", StringComparison.OrdinalIgnoreCase)
               || t.Contains("compression", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCylinderContour(string? experimentType)
    {
        if (string.IsNullOrWhiteSpace(experimentType)) return false;
        var t = NormalizeTypeKey(experimentType);
        if (string.Equals(t, NormalizeTypeKey(CylinderContour), StringComparison.OrdinalIgnoreCase))
            return true;
        // Tolerant: hyphen/en-dash variants, partial labels from older builds / typed text
        return t.Contains("cilindru", StringComparison.OrdinalIgnoreCase)
               && t.Contains("contur", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True for «Mărci tensometrice» / typed «tensometrie» variants.</summary>
    public static bool IsStrainGauges(string? experimentType)
    {
        if (string.IsNullOrWhiteSpace(experimentType)) return false;
        var t = NormalizeTypeKey(experimentType);
        if (string.Equals(t, NormalizeTypeKey(StrainGauges), StringComparison.OrdinalIgnoreCase))
            return true;
        return t.Contains("tensometr", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Type is set and is not cylinder contour — leftover ContourConfig must not appear in reports.
    /// </summary>
    public static bool IsExplicitNonContour(string? experimentType)
        => !string.IsNullOrWhiteSpace(experimentType) && !IsCylinderContour(experimentType);

    /// <summary>
    /// Same experiment family for peer comparison (contur↔contur, tensometrie↔tensometrie).
    /// Never treats cylinder contour as comparable to strain gauges.
    /// </summary>
    public static bool AreSameKind(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        if (IsCylinderContour(a)) return IsCylinderContour(b);
        if (IsStrainGauges(a)) return IsStrainGauges(b);
        return string.Equals(NormalizeTypeKey(a), NormalizeTypeKey(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Normalize dashes/spaces so UI labels match persisted CSV / .upet meta.</summary>
    public static string NormalizeTypeKey(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var t = s.Trim();
        t = t.Replace('\u2013', '-')  // en-dash
             .Replace('\u2014', '-')  // em-dash
             .Replace('\u2212', '-')  // minus
             .Replace('\u00A0', ' ');
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        return t;
    }
}

/// <summary>
/// Operator mapping for circumferential contour (persisted in <see cref="ProjectMeta"/>).
/// Channel indices are 0-based (UI label CH0 = index 0). Against a CSV session they refer to
/// packed Rec columns unless rebound by name (e.g. header «CH1 [mm]»).
/// </summary>
public sealed class CylinderContourConfig
{
    private static readonly Regex HardwareChToken =
        new(@"^(?:D\d+\.)?CH(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Hint row for Start Exp auto-map (Rec / unit aware).</summary>
    public sealed class ChannelHint
    {
        public int Index { get; init; }
        public string Unit { get; init; } = "";
        public string Name { get; init; } = "";
        public bool Enabled { get; init; } = true;
        public bool RecordEnabled { get; init; }
    }

    /// <summary>4 or 8 circumferential sensors.</summary>
    public int SensorCount { get; set; } = 4;

    /// <summary>CH indices for S1…Sn (length = SensorCount), 0-based.</summary>
    public List<int> SensorChannelIndices { get; set; } = new();

    /// <summary>Angles [deg] for S1…Sn; empty → evenly spaced from 0°.</summary>
    public List<double> SensorAnglesDeg { get; set; } = new();

    /// <summary>Optional force channel (null / negative = none).</summary>
    public int? ForceChannelIndex { get; set; }

    /// <summary>Press stroke / axial compression channel [mm].</summary>
    public int StrokeChannelIndex { get; set; } = -1;

    /// <summary>Initial radius R₀ [mm]; 0 → derive from <see cref="ProjectMeta.SampleDiameterMm"/>/2.</summary>
    public double InitialRadiusMm { get; set; }

    /// <summary>Optional tube inner diameter [mm]; 0 = solid specimen (half-section hatch to axis).</summary>
    public double InnerDiameterMm { get; set; }

    [JsonIgnore]
    public bool IsConfigured =>
        (SensorCount == 4 || SensorCount == 8)
        && SensorChannelIndices.Count >= SensorCount
        && StrokeChannelIndex >= 0;

    public static double[] DefaultAnglesDeg(int sensorCount)
    {
        var n = sensorCount == 8 ? 8 : 4;
        var step = 360.0 / n;
        var a = new double[n];
        for (var i = 0; i < n; i++)
            a[i] = i * step;
        return a;
    }

    public static double DefaultAngleDeg(int sensorCount, int index)
    {
        var n = sensorCount == 8 ? 8 : 4;
        return (index % n) * (360.0 / n);
    }

    /// <summary>
    /// Default S1…Sn → CH0…CH(n-1), cursă → CHn (0-based). When
    /// <paramref name="availableChannelCount"/> is set, clamps onto 0..N-1.
    /// </summary>
    public static CylinderContourConfig CreateDefault(
        int sensorCount = 4,
        double diameterMm = 0,
        int availableChannelCount = 0)
    {
        var n = sensorCount == 8 ? 8 : 4;
        var cfg = new CylinderContourConfig
        {
            SensorCount = n,
            SensorChannelIndices = Enumerable.Range(0, n).ToList(),
            SensorAnglesDeg = DefaultAnglesDeg(n).ToList(),
            ForceChannelIndex = null,
            StrokeChannelIndex = n < 8 ? n : 0,
            InitialRadiusMm = diameterMm > 0 ? diameterMm / 2.0 : 0
        };
        if (availableChannelCount > 0)
            cfg.MapToAvailableChannels(availableChannelCount);
        // When CH0…Sn-1 + cursă leave room, default forță to next free channel (demo Contur).
        if (availableChannelCount >= n + 2 && cfg.ForceChannelIndex is null)
            cfg.ForceChannelIndex = n + 1;
        return cfg;
    }

    /// <summary>
    /// Auto-map S1…Sn (+ cursă / forță) when Contur type is selected in Start Exp.
    /// Prefers Rec-enabled mm channels; else first N mm; else sequential Rec / all channels.
    /// Angles: evenly spaced 0°, 90°… (4) or 0°, 45°… (8).
    /// </summary>
    public static CylinderContourConfig AutoMapFromChannelHints(
        int sensorCount,
        double diameterMm,
        IReadOnlyList<ChannelHint>? hints,
        int fallbackChannelCount = 0)
    {
        var n = sensorCount == 8 ? 8 : 4;
        var list = hints?.Where(h => h.Index >= 0).OrderBy(h => h.Index).ToList()
                   ?? new List<ChannelHint>();

        if (list.Count == 0 && fallbackChannelCount > 0)
        {
            for (var i = 0; i < fallbackChannelCount; i++)
                list.Add(new ChannelHint { Index = i, Enabled = true, RecordEnabled = true, Unit = "mm" });
        }

        if (list.Count == 0)
            return CreateDefault(n, diameterMm, Math.Max(n + 1, fallbackChannelCount));

        // Prefer Rec-enabled (and Enabled); fall back to Enabled; then all.
        var pool = list.Where(h => h.Enabled && h.RecordEnabled).ToList();
        if (pool.Count < n)
            pool = list.Where(h => h.Enabled).ToList();
        if (pool.Count < n)
            pool = list.ToList();

        // Radial sensors: prefer [mm] displacement channels.
        var mmPool = pool.Where(IsMmUnit).ToList();
        var sensorHints = (mmPool.Count >= n ? mmPool : pool).Take(n).ToList();
        while (sensorHints.Count < n)
        {
            var next = pool.FirstOrDefault(h => sensorHints.All(s => s.Index != h.Index))
                       ?? list.FirstOrDefault(h => sensorHints.All(s => s.Index != h.Index));
            if (next is null) break;
            sensorHints.Add(next);
        }

        // If still short, pad with sequential indices.
        var used = new HashSet<int>(sensorHints.Select(h => h.Index));
        var maxIdx = Math.Max(list.Max(h => h.Index), sensorHints.Count > 0 ? sensorHints.Max(h => h.Index) : 0);
        while (sensorHints.Count < n)
        {
            var pad = 0;
            while (used.Contains(pad) && pad <= maxIdx + n) pad++;
            used.Add(pad);
            sensorHints.Add(new ChannelHint { Index = pad, Unit = "mm", Enabled = true, RecordEnabled = true });
        }

        var remaining = pool.Concat(list)
            .Where(h => !used.Contains(h.Index))
            .GroupBy(h => h.Index)
            .Select(g => g.First())
            .OrderBy(h => h.Index)
            .ToList();

        // Cursă: prefer unused mm channel; else next free index.
        ChannelHint? strokeHint = remaining.FirstOrDefault(IsMmUnit)
                                  ?? remaining.FirstOrDefault();
        int strokeIdx;
        if (strokeHint is not null)
        {
            strokeIdx = strokeHint.Index;
            used.Add(strokeIdx);
            remaining.RemoveAll(h => h.Index == strokeIdx);
        }
        else
        {
            strokeIdx = used.DefaultIfEmpty(-1).Max() + 1;
            used.Add(strokeIdx);
        }

        // Forță: optional — prefer N / kN / force-like unit among remaining.
        int? forceIdx = null;
        var forceHint = remaining.FirstOrDefault(IsForceUnit) ?? remaining.FirstOrDefault();
        if (forceHint is not null)
            forceIdx = forceHint.Index;

        var cfg = new CylinderContourConfig
        {
            SensorCount = n,
            SensorChannelIndices = sensorHints.Select(h => h.Index).ToList(),
            SensorAnglesDeg = DefaultAnglesDeg(n).ToList(),
            StrokeChannelIndex = strokeIdx,
            ForceChannelIndex = forceIdx,
            InitialRadiusMm = diameterMm > 0 ? diameterMm / 2.0 : 0
        };
        cfg.EnsureShape(n);
        return cfg;
    }

    /// <summary>Romanian one-liner describing an auto-map result.</summary>
    public string ToAutoMapSummaryRo()
    {
        EnsureShape(SensorCount);
        var sensors = string.Join(", ",
            Enumerable.Range(0, SensorCount).Select(i =>
                $"S{i + 1}→CH{SensorChannelIndices[i]}@{SensorAnglesDeg[i].ToString("0.#", CultureInfo.InvariantCulture)}°"));
        var force = ForceChannelIndex is int f && f >= 0 ? $"F=CH{f}" : "F=—";
        return
            $"Mapare automată: {sensors} · cursă=CH{StrokeChannelIndex} · {force} " +
            "(Rec/[mm] preferat · unghiuri egale).";
    }

    private static bool IsMmUnit(ChannelHint h)
    {
        var u = (h.Unit ?? "").Trim();
        if (u.Equals("mm", StringComparison.OrdinalIgnoreCase)) return true;
        if (u.Contains("mm", StringComparison.OrdinalIgnoreCase)
            && !u.Contains("mm2", StringComparison.OrdinalIgnoreCase)
            && !u.Contains("mm²", StringComparison.OrdinalIgnoreCase))
            return true;
        var name = h.Name ?? "";
        return name.Contains("[mm]", StringComparison.OrdinalIgnoreCase)
               || name.Contains("curs", StringComparison.OrdinalIgnoreCase)
               || name.Contains("deplas", StringComparison.OrdinalIgnoreCase)
               || name.Contains("stroke", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsForceUnit(ChannelHint h)
    {
        var u = (h.Unit ?? "").Trim();
        if (u.Equals("N", StringComparison.OrdinalIgnoreCase)
            || u.Equals("kN", StringComparison.OrdinalIgnoreCase)
            || u.Equals("lbf", StringComparison.OrdinalIgnoreCase))
            return true;
        if (u.Contains("N", StringComparison.Ordinal) && !u.Contains("mm", StringComparison.OrdinalIgnoreCase))
            return true;
        var name = h.Name ?? "";
        return name.Contains("forț", StringComparison.OrdinalIgnoreCase)
               || name.Contains("fort", StringComparison.OrdinalIgnoreCase)
               || name.Contains("force", StringComparison.OrdinalIgnoreCase)
               || name.Contains("load", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Romanian range label for Status, e.g. «0..7».</summary>
    public static string FormatChannelRangeRo(int channelCount)
    {
        if (channelCount <= 0) return "(niciun canal)";
        if (channelCount == 1) return "0";
        return $"0..{channelCount - 1}";
    }

    /// <summary>True when any sensor / cursă / forță index is outside 0..channelCount-1.</summary>
    public bool HasOutOfRangeChannels(int channelCount)
    {
        if (channelCount <= 0) return true;
        EnsureShape(SensorCount);
        for (var i = 0; i < SensorCount; i++)
        {
            var ch = SensorChannelIndices[i];
            if (ch < 0 || ch >= channelCount) return true;
        }
        if (StrokeChannelIndex < 0 || StrokeChannelIndex >= channelCount) return true;
        if (ForceChannelIndex is int f && f >= 0 && f >= channelCount) return true;
        return false;
    }

    /// <summary>
    /// Maps S1…Sn onto the first N columns and cursă onto the next (or last if short).
    /// Indices are 0-based packed Rec order.
    /// </summary>
    public bool MapToAvailableChannels(int channelCount)
    {
        if (channelCount <= 0) return false;
        EnsureShape(SensorCount);
        var n = SensorCount;

        if (channelCount >= n + 1)
        {
            SensorChannelIndices = Enumerable.Range(0, n).ToList();
            StrokeChannelIndex = n;
        }
        else if (channelCount >= n)
        {
            SensorChannelIndices = Enumerable.Range(0, n).ToList();
            StrokeChannelIndex = channelCount - 1; // overlaps Sn when exactly n columns
        }
        else
        {
            // Not enough distinct columns — clamp (caller should still report clear Status).
            SensorChannelIndices = Enumerable.Range(0, n)
                .Select(i => Math.Min(i, channelCount - 1))
                .ToList();
            StrokeChannelIndex = channelCount - 1;
        }

        if (ForceChannelIndex is int f)
        {
            if (f < 0 || f >= channelCount)
                ForceChannelIndex = null;
        }

        return channelCount >= n;
    }

    /// <summary>
    /// Remaps hardware CH indices → packed Rec column indices (CSV order).
    /// <paramref name="recordHardwareIndices"/> is the list used at ArmRecording.
    /// </summary>
    public bool TryRemapHardwareToPacked(
        IReadOnlyList<int> recordHardwareIndices,
        out string? errorRo)
    {
        errorRo = null;
        if (recordHardwareIndices is null || recordHardwareIndices.Count == 0)
        {
            errorRo = "Niciun canal Rec — nu se poate mapa conturul.";
            return false;
        }

        EnsureShape(SensorCount);
        var packedSensors = new List<int>(SensorCount);
        for (var i = 0; i < SensorCount; i++)
        {
            var hw = SensorChannelIndices[i];
            var packed = IndexOfHardware(recordHardwareIndices, hw);
            if (packed < 0)
            {
                errorRo =
                    $"CH{hw} (S{i + 1}) nu este în înregistrare (Rec). " +
                    $"Canale Rec: {FormatPackedRecListRo(recordHardwareIndices)}. " +
                    "Remapați S1…Sn în Start Exp pe canale cu Rec.";
                return false;
            }
            packedSensors.Add(packed);
        }

        var strokePacked = IndexOfHardware(recordHardwareIndices, StrokeChannelIndex);
        if (strokePacked < 0)
        {
            // Prefer column after last sensor when available; else last Rec column.
            strokePacked = packedSensors.Count < recordHardwareIndices.Count
                ? packedSensors.Count
                : recordHardwareIndices.Count - 1;
        }

        SensorChannelIndices = packedSensors;
        StrokeChannelIndex = strokePacked;

        if (ForceChannelIndex is int fh)
        {
            var fp = IndexOfHardware(recordHardwareIndices, fh);
            ForceChannelIndex = fp >= 0 ? fp : null;
        }

        return true;
    }

    /// <summary>
    /// Binds config indices to session columns: prefer header name CH{n}, else column index.
    /// If still out of range and enough columns exist → auto-map first N Rec columns.
    /// </summary>
    public bool TryBindToSession(
        IReadOnlyList<string> channelNames,
        int columnCount,
        out string? bindNoteRo)
    {
        bindNoteRo = null;
        if (columnCount <= 0) return false;
        EnsureShape(SensorCount);

        var sensors = new List<int>(SensorCount);
        var allOk = true;
        for (var i = 0; i < SensorCount; i++)
        {
            var raw = SensorChannelIndices[i];
            var col = ResolveColumnIndex(channelNames, columnCount, raw);
            if (col is null) { allOk = false; break; }
            sensors.Add(col.Value);
        }

        int? stroke = null;
        int? force = null;
        if (allOk)
        {
            stroke = ResolveColumnIndex(channelNames, columnCount, StrokeChannelIndex);
            if (stroke is null && StrokeChannelIndex >= 0)
                allOk = false;
            if (ForceChannelIndex is int fh && fh >= 0)
            {
                force = ResolveColumnIndex(channelNames, columnCount, fh);
                // Force is optional — drop if missing
                if (force is null) force = -1;
            }
        }

        if (allOk && stroke is not null)
        {
            SensorChannelIndices = sensors;
            StrokeChannelIndex = stroke.Value;
            if (force is int f)
                ForceChannelIndex = f >= 0 ? f : null;
            return true;
        }

        // Auto-map when recording has enough packed columns for S1…Sn (+ cursă preferred).
        if (columnCount >= SensorCount)
        {
            MapToAvailableChannels(columnCount);
            bindNoteRo =
                $"Mapare automată pe canalele {FormatChannelRangeRo(columnCount)} din înregistrare " +
                $"(S1…S{SensorCount} → CH0…CH{Math.Min(SensorCount, columnCount) - 1}).";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Resolve configured CH index against session: name «CH{n}» first, then column index.
    /// </summary>
    public static int? ResolveColumnIndex(
        IReadOnlyList<string> channelNames,
        int columnCount,
        int configuredIndex)
    {
        if (configuredIndex < 0) return null;

        var byName = FindColumnByHardwareName(channelNames, configuredIndex);
        if (byName is not null) return byName;

        if (configuredIndex < columnCount) return configuredIndex;
        return null;
    }

    public static int? FindColumnByHardwareName(IReadOnlyList<string> channelNames, int hardwareIndex)
    {
        if (channelNames is null || hardwareIndex < 0) return null;
        for (var i = 0; i < channelNames.Count; i++)
        {
            if (ChannelNameMatchesHardwareIndex(channelNames[i], hardwareIndex))
                return i;
        }
        return null;
    }

    public static bool ChannelNameMatchesHardwareIndex(string? name, int hardwareIndex)
    {
        if (string.IsNullOrWhiteSpace(name) || hardwareIndex < 0) return false;
        var n = name.Trim();
        var bracket = n.IndexOf('[');
        if (bracket > 0) n = n[..bracket].Trim();
        var m = HardwareChToken.Match(n);
        if (!m.Success) return false;
        return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ch)
               && ch == hardwareIndex;
    }

    public static string FormatInvalidSensorRo(int sensorOneBased, int channelIndex, int columnCount)
    {
        return
            $"CH{channelIndex} nu există în înregistrare (canale {FormatChannelRangeRo(columnCount)}). " +
            $"Remapați S{sensorOneBased} în Start Exp.";
    }

    public void EnsureShape(int sensorCount)
    {
        var n = sensorCount == 8 ? 8 : 4;
        SensorCount = n;
        while (SensorChannelIndices.Count < n)
            SensorChannelIndices.Add(SensorChannelIndices.Count);
        if (SensorChannelIndices.Count > n)
            SensorChannelIndices.RemoveRange(n, SensorChannelIndices.Count - n);

        var defaults = DefaultAnglesDeg(n);
        while (SensorAnglesDeg.Count < n)
            SensorAnglesDeg.Add(defaults[SensorAnglesDeg.Count]);
        if (SensorAnglesDeg.Count > n)
            SensorAnglesDeg.RemoveRange(n, SensorAnglesDeg.Count - n);
    }

    public IReadOnlyList<double> EffectiveAnglesDeg()
    {
        EnsureShape(SensorCount);
        var defaults = DefaultAnglesDeg(SensorCount);
        var result = new double[SensorCount];
        for (var i = 0; i < SensorCount; i++)
        {
            var a = i < SensorAnglesDeg.Count ? SensorAnglesDeg[i] : defaults[i];
            if (double.IsNaN(a) || double.IsInfinity(a))
                a = defaults[i];
            result[i] = a;
        }
        return result;
    }

    public double ResolveInitialRadiusMm(double sampleDiameterMm = 0)
    {
        if (InitialRadiusMm > 0) return InitialRadiusMm;
        if (sampleDiameterMm > 0) return sampleDiameterMm / 2.0;
        return 0;
    }

    public string ToStatusSummary()
    {
        var force = ForceChannelIndex is int f && f >= 0 ? $"F=CH{f}" : "F=—";
        var stroke = StrokeChannelIndex >= 0 ? $"cursă=CH{StrokeChannelIndex}" : "cursă=—";
        var r0 = InitialRadiusMm > 0
            ? $"R₀={InitialRadiusMm.ToString("0.###", CultureInfo.InvariantCulture)} mm"
            : "R₀=—";
        return $"{SensorCount} senzori · {force} · {stroke} · {r0}";
    }

    private static int IndexOfHardware(IReadOnlyList<int> recordHardwareIndices, int hardwareIndex)
    {
        for (var i = 0; i < recordHardwareIndices.Count; i++)
        {
            if (recordHardwareIndices[i] == hardwareIndex) return i;
        }
        return -1;
    }

    private static string FormatPackedRecListRo(IReadOnlyList<int> recordHardwareIndices)
    {
        if (recordHardwareIndices.Count == 0) return "—";
        return string.Join(", ", recordHardwareIndices.Select(i => $"CH{i}"));
    }
}
