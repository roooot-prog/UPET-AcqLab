using System.Globalization;
using System.Text.Json;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Resolves whether a cylinder-contour plot should be generated for a report,
/// fills missing R₀ / default mapping, and explains skip reasons (RO).
/// </summary>
public static class CylinderContourExport
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    /// <summary>
    /// True when reports should include Contur / Curbe deformare / contour PNG.
    /// Gated on experiment type: leftover ContourConfig on tensometrie (or any other type) is ignored.
    /// Blank type still infers from config / Ø+channels for legacy CSV/.upet.
    /// </summary>
    public static bool ShouldAttempt(ProjectMeta? meta)
        => ShouldAttempt(meta, session: null);

    /// <inheritdoc cref="ShouldAttempt(ProjectMeta?)"/>
    public static bool ShouldAttempt(ProjectMeta? meta, OfflineSession? session)
    {
        if (meta is null) return false;
        // Live type wins: «Mărci tensometrice», mixt, forță, … never get a Contur chapter.
        if (ExperimentTypes.IsExplicitNonContour(meta.ExperimentType))
            return false;
        if (ExperimentTypes.IsCylinderContour(meta.ExperimentType))
            return true;
        // Blank type: legacy recordings without ExperimentType in the CSV header.
        if (meta.CylinderContour is not null) return true;
        return CanInferFromDiameterAndChannels(meta, session);
    }

    /// <summary>
    /// Infer contour when type is blank/contour, Ø (or R₀) is known, and the session has ≥5 channels
    /// (4 radial + cursă) — typical Spider8 cylinder layout.
    /// </summary>
    public static bool CanInferFromDiameterAndChannels(ProjectMeta meta, OfflineSession? session)
    {
        if (ExperimentTypes.IsExplicitNonContour(meta.ExperimentType))
            return false;
        var r0 = meta.SampleDiameterMm > 0
            ? meta.SampleDiameterMm / 2.0
            : meta.CylinderContour?.InitialRadiusMm ?? 0;
        if (r0 <= 0) return false;
        var cols = session?.Columns.Count ?? 0;
        if (cols >= 5) return true;
        // No session yet: still attempt if type hints compression / cylinder.
        var t = meta.ExperimentType ?? "";
        return t.Contains("cilindru", StringComparison.OrdinalIgnoreCase)
               || t.Contains("compresiune", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Copies ContourConfig / ExperimentType / Ø from CSV/.upet <see cref="OfflineSession.AttachedMeta"/>
    /// into the live export meta when missing (DataViewer / ribbon re-export).
    /// </summary>
    public static void MergeFromSession(ProjectMeta meta, OfflineSession? session)
    {
        if (session?.AttachedMeta is not { } attached) return;
        if (string.IsNullOrWhiteSpace(meta.ExperimentType) &&
            !string.IsNullOrWhiteSpace(attached.ExperimentType))
            meta.ExperimentType = attached.ExperimentType;
        // Do not copy leftover ContourConfig onto tensometrie / other explicit types.
        if (meta.CylinderContour is null && attached.CylinderContour is not null
            && !ExperimentTypes.IsExplicitNonContour(meta.ExperimentType))
            meta.CylinderContour = attached.CylinderContour;
        if (meta.SampleDiameterMm <= 0 && attached.SampleDiameterMm > 0)
            meta.SampleDiameterMm = attached.SampleDiameterMm;
        if (meta.SampleLengthMm <= 0 && attached.SampleLengthMm > 0)
            meta.SampleLengthMm = attached.SampleLengthMm;
        if (meta.SampleThicknessMm <= 0 && attached.SampleThicknessMm > 0)
            meta.SampleThicknessMm = attached.SampleThicknessMm;
        if (meta.SampleInnerDiameterMm <= 0 && attached.SampleInnerDiameterMm > 0)
            meta.SampleInnerDiameterMm = attached.SampleInnerDiameterMm;
    }

    /// <summary>
    /// Ensures <see cref="ProjectMeta.CylinderContour"/> is usable for export:
    /// default mapping when type is contour / inferred, R₀ from Ø, shape normalized,
    /// and rebinds / auto-maps to session columns when indices are out of range.
    /// Returns null when contour should not be attempted.
    /// </summary>
    public static CylinderContourConfig? EnsureConfig(
        ProjectMeta meta,
        int sessionChannelCount = 8,
        OfflineSession? session = null)
    {
        MergeFromSession(meta, session);
        if (!ShouldAttempt(meta, session))
            return null;

        var cols = session?.Columns.Count ?? sessionChannelCount;
        if (cols <= 0) cols = sessionChannelCount;

        var cfg = meta.CylinderContour;
        if (cfg is null)
        {
            // Need n sensors + cursă when possible (≥5 → 4 senzori; ≥9 → 8).
            var n = cols >= 9 ? 8 : 4;
            cfg = CylinderContourConfig.CreateDefault(n, meta.SampleDiameterMm, cols);
            meta.CylinderContour = cfg;
            if (string.IsNullOrWhiteSpace(meta.ExperimentType))
                meta.ExperimentType = ExperimentTypes.CylinderContour;
        }

        cfg.EnsureShape(cfg.SensorCount == 8 ? 8 : 4);
        if (cfg.InitialRadiusMm <= 0 && meta.SampleDiameterMm > 0)
            cfg.InitialRadiusMm = meta.SampleDiameterMm / 2.0;

        if (session is not null && session.Columns.Count > 0)
        {
            // Prefer name match (CH1 header → packed col) then auto-map first N Rec columns.
            cfg.TryBindToSession(session.ChannelNames, session.Columns.Count, out _);
        }
        else if (cols > 0 && cfg.HasOutOfRangeChannels(cols))
        {
            cfg.MapToAvailableChannels(cols);
        }

        return cfg;
    }

    /// <summary>Human-readable reason when contour cannot be plotted (Romanian).</summary>
    public static string DescribeSkipReason(ProjectMeta? meta, OfflineSession? session = null)
    {
        if (meta is null)
            return "Contur cilindru omis: meta proiect lipsă.";

        if (session is not null)
            MergeFromSession(meta, session);

        if (ExperimentTypes.IsExplicitNonContour(meta.ExperimentType))
        {
            return "Contur cilindru omis: tipul experimentului «" +
                   meta.ExperimentType.Trim() +
                   "» nu include foaia Contur (doar «Compresiune cilindru – contur»).";
        }

        if (!ShouldAttempt(meta, session))
            return "Contur cilindru omis: tipul experimentului nu este «Compresiune cilindru – contur», lipsește ContourConfig, și nu s-a putut infera Ø + canale radiale.";

        var cfg = EnsureConfig(meta, session?.Columns.Count ?? 8, session);
        if (cfg is null)
            return "Contur cilindru omis: configurație indisponibilă.";

        if (cfg.InitialRadiusMm <= 0 && meta.SampleDiameterMm <= 0)
            return "Contur cilindru omis: lipsește diametrul probei (Ø) — setați Diametru [mm] la Start experiment (R₀=Ø/2).";

        if (!cfg.IsConfigured)
        {
            if (cfg.StrokeChannelIndex < 0)
                return "Contur cilindru omis: canalul de cursă presa nu este setat.";
            if (cfg.SensorChannelIndices.Count < cfg.SensorCount)
                return "Contur cilindru omis: mapping senzori circumferențiali incomplet.";
            return "Contur cilindru omis: configurație incompletă (senzori / cursă).";
        }

        if (session is not null)
        {
            if (session.Timestamps.Count <= 0)
                return "Contur cilindru omis: sesiune fără eșantioane.";
            var nCols = session.Columns.Count;
            for (var i = 0; i < cfg.SensorCount && i < cfg.SensorChannelIndices.Count; i++)
            {
                var ch = cfg.SensorChannelIndices[i];
                if (ch < 0 || ch >= nCols)
                    return "Contur cilindru omis: " +
                           CylinderContourConfig.FormatInvalidSensorRo(i + 1, ch, nCols);
            }
            if (nCols < cfg.SensorCount)
            {
                return
                    $"Contur cilindru omis: înregistrarea are doar {nCols} canal(e) " +
                    $"({CylinderContourConfig.FormatChannelRangeRo(nCols)}); " +
                    $"conturul cere {cfg.SensorCount} senzori + cursă. Activați Rec pe canalele radiale.";
            }
        }

        return "Contur cilindru omis: generare eșuată (verificați canalele și Ø).";
    }

    public static CylinderContourResult? TryCompute(
        OfflineSession session,
        ProjectMeta meta,
        int? cursorA = null,
        int? cursorB = null)
    {
        var cfg = EnsureConfig(meta, session.Columns.Count, session);
        if (cfg is null) return null;
        var innerMm = cfg.InnerDiameterMm > 0 ? cfg.InnerDiameterMm : meta.SampleInnerDiameterMm;
        return CylinderContourAnalysis.Compute(
            cfg, session, cursorA, cursorB,
            meta.SampleDiameterMm, meta.SampleLengthMm, meta.SampleThicknessMm, innerMm);
    }

    public static string? ToCsvCommentLine(CylinderContourConfig? cfg)
    {
        if (cfg is null) return null;
        try
        {
            var json = JsonSerializer.Serialize(cfg, JsonOpts);
            return "ContourConfig=" + json;
        }
        catch
        {
            return null;
        }
    }

    public static CylinderContourConfig? TryParseCsvCommentValue(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<CylinderContourConfig>(json.Trim(), JsonOpts);
        }
        catch
        {
            return null;
        }
    }

    public static IEnumerable<string> BuildRecordingMetaExtraLines(ProjectMeta meta)
    {
        if (!string.IsNullOrWhiteSpace(meta.ExperimentType))
            yield return "ExperimentType=" + meta.ExperimentType.Trim();
        if (meta.SampleDiameterMm > 0)
            yield return "SampleDiameterMm=" +
                         meta.SampleDiameterMm.ToString("G17", CultureInfo.InvariantCulture);
        if (ShouldAttempt(meta))
        {
            var line = ToCsvCommentLine(meta.CylinderContour);
            if (line is not null)
                yield return line;
        }
    }
}
