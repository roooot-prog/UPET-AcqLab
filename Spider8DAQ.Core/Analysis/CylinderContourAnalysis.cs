using System.Globalization;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Analysis;

/// <summary>How the contour sample index (Fmax / fallback) was chosen.</summary>
public enum ContourIndexRule
{
    /// <summary>Index of maximum |force| on the configured force channel.</summary>
    ForceAbsMax,
    /// <summary>Operator Cursor B when a validity zone is set (cursors ≠ full span).</summary>
    CursorB,
    /// <summary>Index of maximum |press stroke| (axial compression).</summary>
    StrokeAbsMax,
    /// <summary>Index of maximum mean radial bulge across circumferential sensors.</summary>
    MaxMeanRadialBulge,
    /// <summary>Could not resolve an index (empty data / invalid config).</summary>
    None
}

/// <summary>One circumferential sensor at the contour sample index.</summary>
public sealed class ContourSensorPoint
{
    public int SensorIndex { get; init; }
    public int ChannelIndex { get; init; }
    public double AngleDeg { get; init; }
    public double RadialDisplacementMm { get; init; }
    public double RadiusMm { get; init; }
    public double XMm { get; init; }
    public double YMm { get; init; }
}

/// <summary>Top-view cylinder contour at Fmax (or documented fallback index).</summary>
public sealed class CylinderContourResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
    public double InitialRadiusMm { get; init; }
    /// <summary>Undeformed axial length L0 [mm] (Start exp. lungime / grosime, else nominal).</summary>
    public double InitialLengthMm { get; init; }
    /// <summary>Deformed axial length L = L0 - |cursă| at contour index [mm].</summary>
    public double DeformedLengthMm { get; init; }
    /// <summary>True when L0 is a schematic fallback (lungime not set at Start experiment).</summary>
    public bool LengthIsNominal { get; init; }
    /// <summary>Romanian note: where L0 came from.</summary>
    public string LengthSourceRo { get; init; } = "";
    /// <summary>Tube inner radius [mm]; 0 = solid (half-section hatches to the axis).</summary>
    public double InnerRadiusMm { get; init; }
    /// <summary>Romanian note: solid vs ID tub.</summary>
    public string InnerRadiusSourceRo { get; init; } = "";
    /// <summary>Force column copy for the mini F-cursa inset (empty if no force channel).</summary>
    public IReadOnlyList<double> ForceSeries { get; init; } = Array.Empty<double>();
    /// <summary>Stroke column copy for the mini F-cursa inset (empty if no stroke channel).</summary>
    public IReadOnlyList<double> StrokeSeries { get; init; } = Array.Empty<double>();
    /// <summary>ASCII: "F vs cursa" | "F vs index" | "cursa vs index" | empty.</summary>
    public string LoadCurveKind { get; init; } = "";
    public int SampleIndex { get; init; }
    public ContourIndexRule IndexRule { get; init; } = ContourIndexRule.None;
    public string IndexRuleFootnoteRo { get; init; } = "";
    public double? ForceAtIndex { get; init; }
    public double? StrokeAtIndex { get; init; }
    public IReadOnlyList<ContourSensorPoint> Sensors { get; init; } = Array.Empty<ContourSensorPoint>();
    /// <summary>Closed polygon through R₀ at sensor angles (initial contour overlay).</summary>
    public IReadOnlyList<(double X, double Y)> InitialContourPolygon { get; init; } = Array.Empty<(double, double)>();
    /// <summary>Closed polygon through sensor tips only (first point repeated) — raw vertices.</summary>
    public IReadOnlyList<(double X, double Y)> DeformedPolygon { get; init; } = Array.Empty<(double, double)>();
    /// <summary>
    /// Dense closed curve through sensor tips via periodic polar interpolation of R(θ).
    /// Avoids the false “inward diamond” of straight chords between 4 sensors.
    /// </summary>
    public IReadOnlyList<(double X, double Y)> SmoothDeformedContour { get; init; } = Array.Empty<(double, double)>();
    /// <summary>
    /// Optional Contur at ~50% of Fmax (or |cursă|) for overlay — empty when unavailable.
    /// </summary>
    public IReadOnlyList<(double X, double Y)> MidLoadSmoothContour { get; init; } = Array.Empty<(double, double)>();
    /// <summary>Sample index used for MidLoadSmoothContour, or null.</summary>
    public int? MidLoadSampleIndex { get; init; }
    /// <summary>Circle samples for undeformed R₀ (closed) — smooth reference.</summary>
    public IReadOnlyList<(double X, double Y)> UndeformedCircle { get; init; } = Array.Empty<(double, double)>();

    /// <summary>u_max at Fmax [mm].</summary>
    public double UMaxMm { get; init; } = double.NaN;
    /// <summary>u_min at Fmax [mm].</summary>
    public double UMinMm { get; init; } = double.NaN;
    /// <summary>u_med (mean of u_i) at Fmax [mm].</summary>
    public double UMeanMm { get; init; } = double.NaN;
    /// <summary>Ovalitate = max(u)−min(u) = max(R)−min(R) at Fmax [mm].</summary>
    public double OvalityMm { get; init; } = double.NaN;
    /// <summary>
    /// Index de bombare (barreling) = u_med / |cursă| (sau |u_axial|) la indexul contur.
    /// NaN when stroke / axial displacement is missing or ~0.
    /// </summary>
    public double BarrelingIndex { get; init; } = double.NaN;
    /// <summary>Sensor with u = u_max at contour index (1-based S#), or 0 if unknown.</summary>
    public int UMaxSensorIndex { get; init; }
    /// <summary>Angle [deg] of the u_max sensor (direction of max radial bulge).</summary>
    public double UMaxAngleDeg { get; init; } = double.NaN;
    /// <summary>Optional warning: one circumferential sensor stayed flat while others grew.</summary>
    public string? FlatSensorWarningRo { get; init; }
    /// <summary>Plot / report footnote: R₀ vs Fmax overlay + index rule.</summary>
    public string PlotFootnoteRo { get; init; } = "";

    public string ToSummaryRo()
    {
        if (!IsValid)
            return string.IsNullOrWhiteSpace(Error) ? "Contur: —" : "Contur: " + Error;
        var inv = CultureInfo.InvariantCulture;
        var barrel = double.IsFinite(BarrelingIndex)
            ? $" · bombare={BarrelingIndex.ToString("0.####", inv)}"
            : "";
        return
            $"Contur Fmax idx={SampleIndex} · R₀={InitialRadiusMm.ToString("0.###", inv)} mm · " +
            $"ovalitate={OvalityMm.ToString("0.###", inv)} mm{barrel} · " +
            $"{Sensors.Count} senzori · {IndexRuleFootnoteRo}";
    }

    /// <summary>Rows for industrial / Excel / HTML result tables.</summary>
    public IReadOnlyList<(string Label, string Value)> BuildReportRows()
    {
        if (!IsValid) return Array.Empty<(string, string)>();
        var inv = CultureInfo.InvariantCulture;
        var rows = new List<(string, string)>
        {
            ("Contur cilindru",
                $"idx={SampleIndex} · R₀={InitialRadiusMm.ToString("0.###", inv)} mm · {Sensors.Count} senzori"),
            ("Ovalitate [mm]",
                OvalityMm.ToString("0.####", inv) + "  (max(u)−min(u) = max(R)−min(R) la Fmax)"),
            ("u_max [mm]", UMaxMm.ToString("0.####", inv)),
            ("u_min [mm]", UMinMm.ToString("0.####", inv)),
            ("u_med [mm]", UMeanMm.ToString("0.####", inv))
        };
        if (InitialLengthMm > 0)
        {
            var l0Note = LengthIsNominal ? "  (" + LengthSourceRo + ")" : "";
            rows.Add(("L0 [mm]", InitialLengthMm.ToString("0.###", inv) + l0Note));
            if (DeformedLengthMm > 0)
                rows.Add(("L la Fmax [mm]", DeformedLengthMm.ToString("0.###", inv) + "  (L0 - |cursă|)"));
        }
        if (InnerRadiusMm > 0)
            rows.Add(("R interior [mm]", InnerRadiusMm.ToString("0.###", inv) + "  (" + InnerRadiusSourceRo + ")"));
        if (double.IsFinite(BarrelingIndex))
        {
            rows.Add((
                "Index de bombare [-]",
                BarrelingIndex.ToString("0.####", inv) + "  (u_med / |cursă| · sau |u_axial|)"));
        }
        else
        {
            rows.Add(("Index de bombare [-]", "— (fără |cursă| / |u_axial| ≠ 0 la index)"));
        }
        if (UMaxSensorIndex > 0 && double.IsFinite(UMaxAngleDeg))
        {
            rows.Add((
                "Direcție u_max",
                $"S{UMaxSensorIndex} @ {UMaxAngleDeg.ToString("0.#", inv)}°"));
        }
        foreach (var s in Sensors)
        {
            rows.Add((
                $"u_S{s.SensorIndex} [mm] (CH{s.ChannelIndex}, {s.AngleDeg.ToString("0.#", inv)}°)",
                s.RadialDisplacementMm.ToString("0.####", inv)));
        }
        if (!string.IsNullOrWhiteSpace(FlatSensorWarningRo))
            rows.Add(("Avertisment senzor", FlatSensorWarningRo!));
        if (!string.IsNullOrWhiteSpace(IndexRuleFootnoteRo))
            rows.Add(("Index Fmax / contur", IndexRuleFootnoteRo));
        return rows;
    }
}

/// <summary>
/// Pure geometry + Fmax index selection for experiment type
/// <see cref="ExperimentTypes.CylinderContour"/>.
/// Circumferential channel values are treated as radial displacement [mm] via existing scale.
/// </summary>
public static class CylinderContourAnalysis
{
    public const int CircleSampleCount = 128;
    /// <summary>Samples for the smooth Fmax contour (closed; last = first).</summary>
    public const int SmoothContourSampleCount = 256;

    /// <summary>
    /// Index selection (documented in Status / report footnote):
    /// 1) force channel → ArgMax |F|;
    /// 2) else if Cursor A/B form a validity zone (not full span) → Cursor B;
    /// 3) else stroke channel → ArgMax |stroke|;
    /// 4) else ArgMax mean radial bulge across circumferential sensors.
    /// </summary>
    public static (int Index, ContourIndexRule Rule, string FootnoteRo) SelectContourIndex(
        IReadOnlyList<double>? forceColumn,
        IReadOnlyList<double>? strokeColumn,
        IReadOnlyList<IReadOnlyList<double>> circumferentialColumns,
        int sampleCount,
        int? cursorA,
        int? cursorB)
    {
        if (sampleCount <= 0)
            return (0, ContourIndexRule.None, "Fără eșantioane — index contur nedeterminat.");

        if (forceColumn is not null && forceColumn.Count > 0)
        {
            var idx = ArgMaxAbs(forceColumn, sampleCount);
            return (idx, ContourIndexRule.ForceAbsMax,
                "Index contur = ArgMax |F| pe canalul de forță configurat (Fmax).");
        }

        if (TryValidityCursorB(sampleCount, cursorA, cursorB, out var b))
        {
            return (b, ContourIndexRule.CursorB,
                "Fără canal forță — index contur = Cursor B (zonă de validitate A–B).");
        }

        if (strokeColumn is not null && strokeColumn.Count > 0)
        {
            var idx = ArgMaxAbs(strokeColumn, sampleCount);
            return (idx, ContourIndexRule.StrokeAbsMax,
                "Fără canal forță — index = max cursă (ArgMax |cursă presa| / |u_axial|).");
        }

        if (circumferentialColumns.Count > 0)
        {
            var idx = ArgMaxMeanAbs(circumferentialColumns, sampleCount);
            return (idx, ContourIndexRule.MaxMeanRadialBulge,
                "Fără forță / Cursor B / cursă — index contur = ArgMax media |u_radial| pe senzorii circumferențiali.");
        }

        return (Math.Clamp(cursorB ?? 0, 0, sampleCount - 1), ContourIndexRule.None,
            "Index contur nedeterminat (configurație incompletă).");
    }

    public static CylinderContourResult Compute(
        CylinderContourConfig config,
        OfflineSession session,
        int? cursorA = null,
        int? cursorB = null,
        double sampleDiameterMm = 0,
        double sampleLengthMm = 0,
        double sampleThicknessMm = 0,
        double sampleInnerDiameterMm = 0)
    {
        if (config is null || !config.IsConfigured)
        {
            return new CylinderContourResult
            {
                IsValid = false,
                Error = "Configurație contur lipsă sau incompletă (senzori / R₀ / cursă)."
            };
        }

        var n = session.Timestamps.Count;
        if (n <= 0)
        {
            return new CylinderContourResult
            {
                IsValid = false,
                Error = "Sesiune fără eșantioane."
            };
        }

        var r0 = config.ResolveInitialRadiusMm(sampleDiameterMm);
        if (r0 <= 0)
        {
            return new CylinderContourResult
            {
                IsValid = false,
                Error = "R₀ invalid — setați diametrul probei (Ø) la Start experiment."
            };
        }

        IReadOnlyList<double>? forceCol = null;
        if (config.ForceChannelIndex is int fi && fi >= 0 && fi < session.Columns.Count)
            forceCol = session.Columns[fi];

        IReadOnlyList<double>? strokeCol = null;
        if (config.StrokeChannelIndex >= 0 && config.StrokeChannelIndex < session.Columns.Count)
            strokeCol = session.Columns[config.StrokeChannelIndex];

        var nCols = session.Columns.Count;
        if (nCols < config.SensorCount)
        {
            return new CylinderContourResult
            {
                IsValid = false,
                Error =
                    $"Înregistrarea are doar {nCols} canal(e) ({CylinderContourConfig.FormatChannelRangeRo(nCols)}); " +
                    $"conturul cere {config.SensorCount} senzori circumferențiali. " +
                    "Activați Rec pe canalele radiale și remapați S1…Sn în Start Exp."
            };
        }

        var circCols = new List<IReadOnlyList<double>>();
        for (var si = 0; si < config.SensorCount; si++)
        {
            var ch = si < config.SensorChannelIndices.Count ? config.SensorChannelIndices[si] : -1;
            if (ch < 0 || ch >= nCols)
            {
                return new CylinderContourResult
                {
                    IsValid = false,
                    Error = CylinderContourConfig.FormatInvalidSensorRo(si + 1, ch, nCols)
                };
            }
            circCols.Add(session.Columns[ch]);
        }

        var (index, rule, footnote) = SelectContourIndex(
            forceCol,
            strokeCol,
            circCols,
            n,
            cursorA ?? session.CursorA,
            cursorB ?? session.CursorB);

        index = Math.Clamp(index, 0, n - 1);
        var angles = config.EffectiveAnglesDeg();
        var sensors = new List<ContourSensorPoint>(config.SensorCount);
        var initialPoly = new List<(double, double)>(config.SensorCount + 1);
        for (var i = 0; i < config.SensorCount; i++)
        {
            var ch = config.SensorChannelIndices[i];
            var u = session.Columns[ch][index];
            if (double.IsNaN(u) || double.IsInfinity(u)) u = 0;
            var angle = i < angles.Count ? angles[i] : CylinderContourConfig.DefaultAngleDeg(config.SensorCount, i);
            var rad = angle * Math.PI / 180.0;
            var r = r0 + u;
            sensors.Add(new ContourSensorPoint
            {
                SensorIndex = i + 1,
                ChannelIndex = ch,
                AngleDeg = angle,
                RadialDisplacementMm = u,
                RadiusMm = r,
                XMm = r * Math.Cos(rad),
                YMm = r * Math.Sin(rad)
            });
            initialPoly.Add((r0 * Math.Cos(rad), r0 * Math.Sin(rad)));
        }

        var poly = new List<(double, double)>(sensors.Count + 1);
        foreach (var s in sensors)
            poly.Add((s.XMm, s.YMm));
        if (poly.Count > 0)
            poly.Add(poly[0]);
        if (initialPoly.Count > 0)
            initialPoly.Add(initialPoly[0]);

        double uMax = double.NegativeInfinity, uMin = double.PositiveInfinity, uSum = 0;
        var uMaxSensor = 0;
        var uMaxAngle = double.NaN;
        foreach (var s in sensors)
        {
            var u = s.RadialDisplacementMm;
            if (u > uMax)
            {
                uMax = u;
                uMaxSensor = s.SensorIndex;
                uMaxAngle = s.AngleDeg;
            }
            if (u < uMin) uMin = u;
            uSum += u;
        }
        var uMean = sensors.Count > 0 ? uSum / sensors.Count : double.NaN;
        var ovality = sensors.Count > 0 ? uMax - uMin : double.NaN;

        var flatWarn = DetectFlatRadialSensor(circCols, sensors, index);
        var smooth = BuildSmoothPolarContour(sensors, SmoothContourSampleCount);
        var (midIdx, midSmooth) = TryBuildMidLoadContour(
            forceCol, strokeCol, circCols, angles, r0, config, index, n);

        var plotFootnote =
            "Schemă industrială: plan (R₀) + elevație + secțiune · Contur/profil la Fmax · " +
            "u_max · tabel u_i · mini sigma-eps / F-cursa. " +
            footnote +
            (string.IsNullOrWhiteSpace(flatWarn) ? "" : " · " + flatWarn) +
            " " + CylinderContourPlotRenderer.BarrelMapHonestyNote + ". " +
            CylinderContourPlotRenderer.PaletteLegendRing + ".";

        double? forceAt = forceCol is not null && index < forceCol.Count ? forceCol[index] : null;
        double? strokeAt = strokeCol is not null && index < strokeCol.Count ? strokeCol[index] : null;

        var barreling = double.NaN;
        if (strokeAt is double stAx && double.IsFinite(stAx) && Math.Abs(stAx) > 1e-12
            && double.IsFinite(uMean))
        {
            barreling = uMean / Math.Abs(stAx);
        }

        var absStroke = strokeAt is double stL && double.IsFinite(stL) ? Math.Abs(stL) : 0;
        var l0 = CylinderBarrelGeometry.ResolveInitialLengthMm(
            sampleLengthMm, sampleThicknessMm, r0, absStroke, out var l0Nominal, out var l0Source);
        var lDef = CylinderBarrelGeometry.DeformedHeightMm(l0, strokeAt);

        var cfgInner = config.InnerDiameterMm > 0 ? config.InnerDiameterMm : 0;
        var innerDiam = cfgInner > 0 ? cfgInner : sampleInnerDiameterMm;
        var ri = CylinderBarrelGeometry.ResolveInnerRadiusMm(r0, innerDiam, out var riSource);

        var forceSeries = CopySeries(forceCol, n);
        var strokeSeries = CopySeries(strokeCol, n);
        var canStressStrain = l0 > 0 && r0 > 0
            && forceSeries.Length > 1 && strokeSeries.Length > 1;
        var loadKind = CylinderBarrelGeometry.ResolveLoadCurveKind(
            forceSeries.Length > 1, strokeSeries.Length > 1, canStressStrain);

        return new CylinderContourResult
        {
            IsValid = true,
            InitialRadiusMm = r0,
            InitialLengthMm = l0,
            DeformedLengthMm = lDef,
            LengthIsNominal = l0Nominal,
            LengthSourceRo = l0Source,
            InnerRadiusMm = ri,
            InnerRadiusSourceRo = riSource,
            ForceSeries = forceSeries,
            StrokeSeries = strokeSeries,
            LoadCurveKind = loadKind,
            SampleIndex = index,
            IndexRule = rule,
            IndexRuleFootnoteRo = footnote,
            ForceAtIndex = forceAt,
            StrokeAtIndex = strokeAt,
            Sensors = sensors,
            InitialContourPolygon = initialPoly,
            DeformedPolygon = poly,
            SmoothDeformedContour = smooth,
            MidLoadSmoothContour = midSmooth,
            MidLoadSampleIndex = midIdx,
            UndeformedCircle = BuildCircle(r0, CircleSampleCount),
            UMaxMm = uMax,
            UMinMm = uMin,
            UMeanMm = uMean,
            OvalityMm = ovality,
            BarrelingIndex = barreling,
            UMaxSensorIndex = uMaxSensor,
            UMaxAngleDeg = uMaxAngle,
            FlatSensorWarningRo = flatWarn,
            PlotFootnoteRo = plotFootnote
        };
    }

    /// <summary>
    /// Contur at first sample where |driver| reaches ~50% of |driver| at Fmax index
    /// (force preferred, else stroke). Empty when driver missing or mid≈Fmax.
    /// </summary>
    public static (int? Index, IReadOnlyList<(double X, double Y)> Contour) TryBuildMidLoadContour(
        IReadOnlyList<double>? forceColumn,
        IReadOnlyList<double>? strokeColumn,
        IReadOnlyList<IReadOnlyList<double>> circumferentialColumns,
        IReadOnlyList<double> angles,
        double r0,
        CylinderContourConfig config,
        int fmaxIndex,
        int sampleCount)
    {
        IReadOnlyList<double>? driver = null;
        if (forceColumn is not null && forceColumn.Count > 0) driver = forceColumn;
        else if (strokeColumn is not null && strokeColumn.Count > 0) driver = strokeColumn;
        if (driver is null || fmaxIndex <= 1 || sampleCount < 3)
            return (null, Array.Empty<(double, double)>());

        var peak = Math.Abs(driver[Math.Min(fmaxIndex, driver.Count - 1)]);
        if (peak < 1e-12) return (null, Array.Empty<(double, double)>());
        var target = 0.5 * peak;
        var mid = -1;
        var n = Math.Min(sampleCount, driver.Count);
        for (var i = 0; i <= Math.Min(fmaxIndex, n - 1); i++)
        {
            if (Math.Abs(driver[i]) >= target)
            {
                mid = i;
                break;
            }
        }
        if (mid < 0 || mid >= fmaxIndex - 1)
            return (null, Array.Empty<(double, double)>());

        var sensors = new List<ContourSensorPoint>(config.SensorCount);
        for (var i = 0; i < config.SensorCount; i++)
        {
            if (i >= circumferentialColumns.Count) break;
            var col = circumferentialColumns[i];
            if (mid >= col.Count) continue;
            var u = col[mid];
            if (double.IsNaN(u) || double.IsInfinity(u)) u = 0;
            var angle = i < angles.Count ? angles[i] : CylinderContourConfig.DefaultAngleDeg(config.SensorCount, i);
            var rad = angle * Math.PI / 180.0;
            var r = r0 + u;
            sensors.Add(new ContourSensorPoint
            {
                SensorIndex = i + 1,
                ChannelIndex = i < config.SensorChannelIndices.Count ? config.SensorChannelIndices[i] : i,
                AngleDeg = angle,
                RadialDisplacementMm = u,
                RadiusMm = r,
                XMm = r * Math.Cos(rad),
                YMm = r * Math.Sin(rad)
            });
        }
        if (sensors.Count < 3)
            return (null, Array.Empty<(double, double)>());
        return (mid, BuildSmoothPolarContour(sensors, SmoothContourSampleCount));
    }

    /// <summary>
    /// Periodic cosine interpolation of radius vs angle so Contur Fmax bulges outside R₀
    /// when uᵢ &gt; 0 (barreling), instead of chord-diamond edges cutting inside the circle.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> BuildSmoothPolarContour(
        IReadOnlyList<ContourSensorPoint> sensors,
        int samples = SmoothContourSampleCount)
    {
        if (sensors is null || sensors.Count == 0 || samples < 8)
            return Array.Empty<(double, double)>();

        var ordered = sensors
            .Select(s => (Angle: NormalizeDeg(s.AngleDeg), Radius: Math.Max(0, s.RadiusMm)))
            .OrderBy(t => t.Angle)
            .ToList();

        // Degenerate: all same angle → circle at mean R
        if (ordered.Count == 1)
            return BuildCircle(ordered[0].Radius, samples);

        var n = ordered.Count;
        var angles = new double[n + 1];
        var radii = new double[n + 1];
        for (var i = 0; i < n; i++)
        {
            angles[i] = ordered[i].Angle;
            radii[i] = ordered[i].Radius;
        }
        angles[n] = ordered[0].Angle + 360.0;
        radii[n] = ordered[0].Radius;

        var pts = new (double, double)[samples + 1];
        for (var i = 0; i < samples; i++)
        {
            var deg = 360.0 * i / samples;
            var r = InterpolatePeriodicRadius(deg, angles, radii);
            var rad = deg * Math.PI / 180.0;
            pts[i] = (r * Math.Cos(rad), r * Math.Sin(rad));
        }
        pts[samples] = pts[0];
        return pts;
    }

    private static double InterpolatePeriodicRadius(double deg, double[] angles, double[] radii)
    {
        deg = NormalizeDeg(deg);
        // Map query into [angles[0], angles[0]+360) span already encoded in angles[^1]
        var q = deg;
        if (q < angles[0])
            q += 360.0;

        for (var i = 0; i < angles.Length - 1; i++)
        {
            if (q >= angles[i] && q <= angles[i + 1])
            {
                var span = angles[i + 1] - angles[i];
                if (span <= 1e-12)
                    return radii[i];
                var t = (q - angles[i]) / span;
                // Cosine ease — C1-ish soft blend between sensor radii
                var s = 0.5 - 0.5 * Math.Cos(Math.PI * Math.Clamp(t, 0, 1));
                return radii[i] * (1 - s) + radii[i + 1] * s;
            }
        }
        return radii[^1];
    }

    private static double NormalizeDeg(double deg)
    {
        if (double.IsNaN(deg) || double.IsInfinity(deg)) return 0;
        deg %= 360.0;
        if (deg < 0) deg += 360.0;
        return deg;
    }

    /// <summary>
    /// One circumferential channel nearly constant (flat) while peers show clear radial growth to Fmax.
    /// Uses peak-to-peak on [0..Fmax] similar in spirit to defect «semnal-plat».
    /// </summary>
    public static string? DetectFlatRadialSensor(
        IReadOnlyList<IReadOnlyList<double>> circumferentialColumns,
        IReadOnlyList<ContourSensorPoint> sensorsAtFmax,
        int fmaxIndex,
        double relativeFlatFrac = 0.12,
        double absRangeFloorMm = 0.02)
    {
        if (circumferentialColumns.Count < 2 || sensorsAtFmax.Count < 2 || fmaxIndex < 1)
            return null;

        var ranges = new double[circumferentialColumns.Count];
        for (var i = 0; i < circumferentialColumns.Count; i++)
        {
            var col = circumferentialColumns[i];
            var end = Math.Min(fmaxIndex, col.Count - 1);
            if (end < 1) { ranges[i] = 0; continue; }
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            for (var k = 0; k <= end; k++)
            {
                var v = col[k];
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                if (v < min) min = v;
                if (v > max) max = v;
            }
            ranges[i] = double.IsInfinity(min) ? 0 : max - min;
        }

        var meanRange = ranges.Average();
        if (meanRange < Math.Max(absRangeFloorMm * 3, 0.05))
            return null; // nothing grew — not a single-sensor fault

        for (var i = 0; i < ranges.Length; i++)
        {
            if (ranges[i] > meanRange * relativeFlatFrac) continue;
            if (ranges[i] > absRangeFloorMm && ranges[i] > meanRange * 0.05) continue;
            var peers = ranges.Where((_, j) => j != i).DefaultIfEmpty(0).Average();
            if (peers < absRangeFloorMm * 2) continue;
            if (ranges[i] > peers * relativeFlatFrac) continue;
            var s = i < sensorsAtFmax.Count ? sensorsAtFmax[i] : null;
            var label = s is null ? $"S{i + 1}" : $"S{s.SensorIndex} (CH{s.ChannelIndex})";
            return
                $"Senzor radial posibil defect/plat: {label} — variație ≈{ranges[i].ToString("0.###", CultureInfo.InvariantCulture)} mm " +
                $"până la Fmax, pe când media celorlalți ≈{peers.ToString("0.###", CultureInfo.InvariantCulture)} mm.";
        }

        return null;
    }

    public static IReadOnlyList<(double X, double Y)> BuildCircle(double radiusMm, int samples = CircleSampleCount)
    {
        if (radiusMm <= 0 || samples < 3)
            return Array.Empty<(double, double)>();
        var pts = new (double, double)[samples + 1];
        for (var i = 0; i < samples; i++)
        {
            var a = 2 * Math.PI * i / samples;
            pts[i] = (radiusMm * Math.Cos(a), radiusMm * Math.Sin(a));
        }
        pts[samples] = pts[0];
        return pts;
    }

    private static bool TryValidityCursorB(int sampleCount, int? cursorA, int? cursorB, out int b)
    {
        b = 0;
        if (sampleCount <= 1 || cursorB is null) return false;
        var a = Math.Clamp(cursorA ?? 0, 0, sampleCount - 1);
        b = Math.Clamp(cursorB.Value, 0, sampleCount - 1);
        var lo = Math.Min(a, b);
        var hi = Math.Max(a, b);
        var fullSpan = lo == 0 && hi == sampleCount - 1;
        return !fullSpan && a != b;
    }

    private static int ArgMaxAbs(IReadOnlyList<double> col, int sampleCount)
    {
        var n = Math.Min(sampleCount, col.Count);
        var best = 0;
        var bestAbs = double.NegativeInfinity;
        for (var i = 0; i < n; i++)
        {
            var v = col[i];
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            var a = Math.Abs(v);
            if (a > bestAbs)
            {
                bestAbs = a;
                best = i;
            }
        }
        return best;
    }

    private static int ArgMaxMeanAbs(IReadOnlyList<IReadOnlyList<double>> columns, int sampleCount)
    {
        var n = sampleCount;
        foreach (var c in columns)
            n = Math.Min(n, c.Count);
        if (n <= 0 || columns.Count == 0) return 0;

        var best = 0;
        var bestMean = double.NegativeInfinity;
        for (var i = 0; i < n; i++)
        {
            double sum = 0;
            var k = 0;
            foreach (var c in columns)
            {
                var v = c[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                sum += Math.Abs(v);
                k++;
            }
            if (k == 0) continue;
            var mean = sum / k;
            if (mean > bestMean)
            {
                bestMean = mean;
                best = i;
            }
        }
        return best;
    }

    private static double[] CopySeries(IReadOnlyList<double>? col, int sampleCount)
    {
        if (col is null || sampleCount <= 0 || col.Count <= 0)
            return Array.Empty<double>();
        var n = Math.Min(sampleCount, col.Count);
        var a = new double[n];
        for (var i = 0; i < n; i++)
            a[i] = col[i];
        return a;
    }
}
