namespace Spider8DAQ.Core.Analysis;

/// <summary>
/// Elevation (side-view) geometry for a compressed cylinder:
/// undeformed L0 x D0 rectangle and a barreled profile at Fmax
/// (ends stay near R0 from platen friction; mid-height swells by u).
/// </summary>
public static class CylinderBarrelGeometry
{
    public const int GeneratorSampleCount = 48;

    /// <summary>
    /// Vertical samples for the u(z) color fill. Dense enough that Turbo looks
    /// continuous (not ~8–20 brick cells); 120 stays cheap to draw.
    /// </summary>
    public const int DefaultFillBandCount = 120;

    /// <summary>
    /// Azimuth columns on the visible elevation face (left generator → right).
    /// Enough to interpolate S1…Sn smoothly; not a 3D scan.
    /// </summary>
    public const int DefaultFillColumnCount = 32;

    /// <summary>
    /// Do not interpolate measured u across an empty arc larger than this.
    /// A missing/NaN channel leaves a gap instead of inventing a value.
    /// </summary>
    public const double MaxInterpolateArcDeg = 180.0;

    /// <summary>
    /// Adjacent fill bands overlap by this fraction of band height so polygon
    /// edges do not show as 1 px “brick” seams.
    /// </summary>
    public const double FillBandOverlapFraction = 0.25;

    /// <summary>Floor for deformed height when |cursă| exceeds L0 (bad / missing length).</summary>
    public const double MinHeightFractionOfL0 = 0.15;

    /// <summary>Nominal L0/D0 when lungime is missing at Start experiment (slenderness 2).</summary>
    public const double NominalSlenderness = 2.0;

    /// <summary>Same 2–8% visual band as top-view Contur (tiny |cursă| only).</summary>
    public const double AxialMinVisualFractionOfL0 = 0.025;

    /// <summary>Target visual |cursă| / L0 when compression would otherwise be invisible.</summary>
    public const double AxialTargetVisualFractionOfL0 = 0.05;

    /// <summary>
    /// Resolve axial length L0 [mm]: SampleLengthMm, else grosime/înălțime,
    /// else schematic max(2*D0, 3*|cursă|).
    /// </summary>
    public static double ResolveInitialLengthMm(
        double sampleLengthMm,
        double sampleThicknessMm,
        double r0Mm,
        double absStrokeMm,
        out bool isNominal,
        out string sourceRo)
    {
        if (sampleLengthMm > 0 && double.IsFinite(sampleLengthMm))
        {
            isNominal = false;
            sourceRo = "Start exp. (lungime)";
            return sampleLengthMm;
        }

        if (sampleThicknessMm > 0 && double.IsFinite(sampleThicknessMm))
        {
            isNominal = false;
            sourceRo = "Start exp. (grosime/inaltime)";
            return sampleThicknessMm;
        }

        isNominal = true;
        var fromDiameter = r0Mm > 0 ? NominalSlenderness * 2.0 * r0Mm : 0;
        var fromStroke = absStrokeMm > 0 && double.IsFinite(absStrokeMm) ? 3.0 * absStrokeMm : 0;
        var l0 = Math.Max(fromDiameter, fromStroke);
        sourceRo = fromStroke > fromDiameter
            ? "nominal 3*|cursa| (L0 nesetat)"
            : "nominal 2*D0 (L0 nesetat)";
        return l0 > 0 ? l0 : 0;
    }

    /// <summary>L = L0 - |cursă|, floored so the specimen stays drawable.</summary>
    public static double DeformedHeightMm(double l0Mm, double? strokeMm)
    {
        if (!(l0Mm > 0) || double.IsNaN(l0Mm) || double.IsInfinity(l0Mm))
            return 0;
        var dl = strokeMm is double s && double.IsFinite(s) ? Math.Abs(s) : 0;
        var l = l0Mm - dl;
        var floor = l0Mm * MinHeightFractionOfL0;
        return l < floor ? floor : l;
    }

    /// <summary>
    /// Exaggerate tiny |cursă|/L0 into the same ~5% band as the top-view Contur.
    /// Large compression is left at true scale (never shrunk).
    /// Returns 1 when stroke is ~0 or L0 is invalid.
    /// </summary>
    public static double ComputeAxialVisualScale(double l0Mm, double absStrokeMm)
    {
        if (!(l0Mm > 0) || !(absStrokeMm > 1e-15))
            return 1.0;
        var frac = absStrokeMm / l0Mm;
        if (frac >= AxialMinVisualFractionOfL0)
            return 1.0;
        var k = AxialTargetVisualFractionOfL0 * l0Mm / absStrokeMm;
        if (k >= 20) return Math.Round(k);
        if (k >= 10) return Math.Round(k);
        return Math.Round(k, 1);
    }

    /// <summary>Closed rectangle: x=[-R0,R0], y=[0,L0] (Y=0 = bottom platen).</summary>
    public static IReadOnlyList<(double X, double Y)> BuildUndeformedRectangle(double r0Mm, double l0Mm)
    {
        if (!(r0Mm > 0) || !(l0Mm > 0))
            return Array.Empty<(double, double)>();
        return new[]
        {
            (-r0Mm, 0.0),
            (r0Mm, 0.0),
            (r0Mm, l0Mm),
            (-r0Mm, l0Mm),
            (-r0Mm, 0.0)
        };
    }

    /// <summary>
    /// Radial displacement along a generator: u(y) = uMid * sin^2(pi * y / H).
    /// Platens (y=0, y=H) stay at 0; mid-height is the sensor value uMid.
    /// This is a 1-D barrel model, not a dense surface scan.
    /// </summary>
    public static double DisplacementAtHeight(double uMidMm, double yMm, double heightMm)
    {
        if (!(heightMm > 1e-15))
            return uMidMm;
        var t = Math.Clamp(yMm / heightMm, 0, 1);
        var s = Math.Sin(Math.PI * t);
        return uMidMm * s * s;
    }

    /// <summary>
    /// Radius along a generator: R(y) = R0 + u * sin^2(pi * y / H).
    /// Ends (y=0, y=H) stay at R0; mid-height is R0+u.
    /// </summary>
    public static double RadiusAtHeight(double r0Mm, double uMm, double yMm, double heightMm)
        => Math.Max(0, r0Mm + DisplacementAtHeight(uMm, yMm, heightMm));

    /// <summary>
    /// Color-scale span from measured u_i. Platens contribute u=0 (contact, no
    /// lateral sensor) so the scale includes 0 when all readings are positive.
    /// Does not invent a span when every sample is ~0.
    /// </summary>
    public static void BarrelUColorRange(
        IReadOnlyList<ContourSensorPoint>? sensors,
        out double uLo,
        out double uHi)
    {
        uLo = 0;
        uHi = 0;
        var any = false;
        if (sensors is null)
            return;
        foreach (var s in sensors)
        {
            var u = s.RadialDisplacementMm;
            if (!double.IsFinite(u))
                continue;
            if (!any)
            {
                uLo = u;
                uHi = u;
                any = true;
            }
            else
            {
                if (u < uLo) uLo = u;
                if (u > uHi) uHi = u;
            }
        }
        if (!any)
            return;
        uLo = Math.Min(0, uLo);
        uHi = Math.Max(0, uHi);
        if (!double.IsFinite(uLo) || !double.IsFinite(uHi))
        {
            uLo = 0;
            uHi = 0;
        }
    }

    /// <summary>
    /// Height bands of the barreled silhouette. X follows visual (possibly exaggerated)
    /// generators so the fill matches the dashed Fmax outline.
    /// </summary>
    public static IReadOnlyList<BarrelFillBand> BuildBarrelFillBands(
        double r0Mm,
        double uRightVisMm,
        double uLeftVisMm,
        double heightMm,
        int bands = DefaultFillBandCount)
    {
        if (!(r0Mm > 0) || !(heightMm > 0) || bands < 2)
            return Array.Empty<BarrelFillBand>();

        var overlap = (heightMm / bands) * FillBandOverlapFraction;
        var list = new BarrelFillBand[bands];
        for (var i = 0; i < bands; i++)
        {
            var y0 = heightMm * i / bands;
            var y1 = heightMm * (i + 1) / bands;
            var yMid = 0.5 * (y0 + y1);
            var y0o = Math.Max(0, y0 - overlap);
            var y1o = Math.Min(heightMm, y1 + overlap);
            list[i] = new BarrelFillBand
            {
                Y0 = y0o,
                Y1 = y1o,
                YMid = yMid,
                XLeft0 = -RadiusAtHeight(r0Mm, uLeftVisMm, y0o, heightMm),
                XRight0 = RadiusAtHeight(r0Mm, uRightVisMm, y0o, heightMm),
                XLeft1 = -RadiusAtHeight(r0Mm, uLeftVisMm, y1o, heightMm),
                XRight1 = RadiusAtHeight(r0Mm, uRightVisMm, y1o, heightMm)
            };
        }
        return list;
    }

    /// <summary>
    /// Elevation fill of the whole silhouette as one body: each cell is measured
    /// u(θ) interpolated on the sensor ring, then geometrically faded to u=0 at
    /// the platens (contact, no lateral sensor). NaN channels are skipped; a
    /// gap larger than <see cref="MaxInterpolateArcDeg"/> is left uncolored.
    /// </summary>
    public static IReadOnlyList<BarrelFillCell> BuildBarrelFillCells(
        double r0Mm,
        double uRightVisMm,
        double uLeftVisMm,
        double heightMm,
        IReadOnlyList<ContourSensorPoint>? sensors,
        double viewPlaneDeg,
        int bands = DefaultFillBandCount,
        int columns = DefaultFillColumnCount)
    {
        var slices = BuildBarrelFillBands(r0Mm, uRightVisMm, uLeftVisMm, heightMm, bands);
        if (slices.Count == 0 || columns < 2)
            return Array.Empty<BarrelFillCell>();

        var overlapT = FillBandOverlapFraction / columns;
        var list = new List<BarrelFillCell>(slices.Count * columns);
        for (var i = 0; i < slices.Count; i++)
        {
            var b = slices[i];
            for (var j = 0; j < columns; j++)
            {
                var t0 = Math.Max(0, j / (double)columns - overlapT);
                var t1 = Math.Min(1, (j + 1) / (double)columns + overlapT);
                var tMid = (j + 0.5) / columns;
                var theta = VisibleAzimuthDeg(viewPlaneDeg, tMid);
                if (!TryInterpolateUAtAngleDeg(sensors, theta, out var uRing))
                    continue;
                list.Add(new BarrelFillCell
                {
                    Y0 = b.Y0,
                    Y1 = b.Y1,
                    XLeft0 = Lerp(b.XLeft0, b.XRight0, t0),
                    XRight0 = Lerp(b.XLeft0, b.XRight0, t1),
                    XLeft1 = Lerp(b.XLeft1, b.XRight1, t0),
                    XRight1 = Lerp(b.XLeft1, b.XRight1, t1),
                    U = DisplacementAtHeight(uRing, b.YMid, heightMm)
                });
            }
        }
        return list;
    }

    /// <summary>
    /// Visible-face azimuth on the elevation: t=0 left generator (view+180 deg),
    /// t=1 right generator (view plane). Orthographic: theta = view + acos(2t−1).
    /// </summary>
    public static double VisibleAzimuthDeg(double viewPlaneDeg, double tLeftToRight)
    {
        var t = Math.Clamp(tLeftToRight, 0, 1);
        var xNorm = 2.0 * t - 1.0;
        var phiDeg = Math.Acos(Math.Clamp(xNorm, -1, 1)) * (180.0 / Math.PI);
        return viewPlaneDeg + phiDeg;
    }

    /// <summary>
    /// Closed barrel outline (right generator bottom-to-top, left top-to-bottom).
    /// Left/right u may differ (0° vs 180° sensors) so ovalitate shows in elevation.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> BuildBarrelOutline(
        double r0Mm,
        double uRightMm,
        double uLeftMm,
        double heightMm,
        int samples = GeneratorSampleCount)
    {
        if (!(r0Mm > 0) || !(heightMm > 0) || samples < 4)
            return Array.Empty<(double, double)>();

        var n = samples;
        var pts = new (double X, double Y)[2 * (n + 1) + 1];
        var k = 0;
        for (var i = 0; i <= n; i++)
        {
            var y = heightMm * i / n;
            pts[k++] = (RadiusAtHeight(r0Mm, uRightMm, y, heightMm), y);
        }
        for (var i = n; i >= 0; i--)
        {
            var y = heightMm * i / n;
            pts[k++] = (-RadiusAtHeight(r0Mm, uLeftMm, y, heightMm), y);
        }
        pts[k] = pts[0];
        return pts;
    }

    /// <summary>
    /// Inner radius for a half-section. ID must be finite, &gt; 0 and &lt; D0.
    /// Otherwise 0 = solid (hatch to the axis).
    /// </summary>
    public static double ResolveInnerRadiusMm(double r0Mm, double innerDiameterMm, out string sourceRo)
    {
        if (r0Mm > 0 && innerDiameterMm > 0 && double.IsFinite(innerDiameterMm))
        {
            var ri = innerDiameterMm / 2.0;
            if (ri > 0 && ri < r0Mm * 0.98)
            {
                sourceRo = "ID tub";
                return ri;
            }
        }
        sourceRo = "solid (fara ID)";
        return 0;
    }

    /// <summary>
    /// Undeformed cross-section A0 [mm²]: π R0² (solid) or π(R0² − Ri²) (tube).
    /// </summary>
    public static double CrossSectionAreaMm2(double r0Mm, double innerRadiusMm)
    {
        if (!(r0Mm > 0) || double.IsNaN(r0Mm) || double.IsInfinity(r0Mm))
            return 0;
        var a0 = Math.PI * r0Mm * r0Mm;
        if (innerRadiusMm > 0 && innerRadiusMm < r0Mm
            && double.IsFinite(innerRadiusMm))
            a0 -= Math.PI * innerRadiusMm * innerRadiusMm;
        return a0;
    }

    /// <summary>
    /// End diameter D_capat = 2 R0 (platens, little bulge).
    /// Mid diameter D_mijloc = 2 R0 + u_right + u_left in the u_max plane,
    /// else 2 (R0 + u_med).
    /// </summary>
    public static void EndAndMidDiametersMm(
        double r0Mm,
        IReadOnlyList<ContourSensorPoint>? sensors,
        double uMeanMm,
        double uMaxAngleDeg,
        out double dCapatMm,
        out double dMijlocMm)
    {
        dCapatMm = r0Mm > 0 ? 2.0 * r0Mm : 0;
        var uMed = double.IsFinite(uMeanMm) ? uMeanMm : 0.0;
        if (sensors is { Count: > 0 } && double.IsFinite(uMaxAngleDeg))
        {
            var uR = TryInterpolateUAtAngleDeg(sensors, uMaxAngleDeg, out var ur) ? ur : uMed;
            var uL = TryInterpolateUAtAngleDeg(sensors, uMaxAngleDeg + 180.0, out var ul) ? ul : uMed;
            dMijlocMm = 2.0 * r0Mm + uR + uL;
            return;
        }
        dMijlocMm = 2.0 * (r0Mm + uMed);
    }

    /// <summary>
    /// σ [MPa] = |F| / A0 with F treated as N and A0 in mm² (N/mm² = MPa).
    /// ε [-] = |cursă| / L0. Returns false when A0, L0, or series are unusable.
    /// </summary>
    public static bool TryBuildStressStrain(
        IReadOnlyList<double> forceN,
        IReadOnlyList<double> strokeMm,
        double r0Mm,
        double innerRadiusMm,
        double l0Mm,
        out double[] eps,
        out double[] sigmaMpa)
    {
        eps = Array.Empty<double>();
        sigmaMpa = Array.Empty<double>();
        var a0 = CrossSectionAreaMm2(r0Mm, innerRadiusMm);
        if (!(a0 > 0) || !(l0Mm > 0) || forceN is null || strokeMm is null)
            return false;
        var n = Math.Min(forceN.Count, strokeMm.Count);
        if (n < 2)
            return false;

        var xs = new double[n];
        var ys = new double[n];
        var k = 0;
        for (var i = 0; i < n; i++)
        {
            var f = forceN[i];
            var s = strokeMm[i];
            if (!double.IsFinite(f) || !double.IsFinite(s))
                continue;
            xs[k] = Math.Abs(s) / l0Mm;
            ys[k] = Math.Abs(f) / a0;
            k++;
        }
        if (k < 2)
            return false;
        if (k < n)
        {
            Array.Resize(ref xs, k);
            Array.Resize(ref ys, k);
        }
        eps = xs;
        sigmaMpa = ys;
        return true;
    }

    /// <summary>ASCII load-curve kind for the mini inset.</summary>
    public static string ResolveLoadCurveKind(
        bool hasForce,
        bool hasStroke,
        bool canStressStrain = false)
    {
        if (canStressStrain && hasForce && hasStroke) return "sigma vs eps";
        if (hasForce && hasStroke) return "F vs cursa";
        if (hasForce) return "F vs index";
        if (hasStroke) return "cursa vs index";
        return "";
    }

    /// <summary>
    /// Closed +X wall polygon for a half-section (Y=0 bottom platen).
    /// Inner generator uses proportional bulge uInner = uOuter * (ri/r0) when ri&gt;0
    /// so wall thickness stays roughly constant; ri=0 → inner is the axis.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> BuildHalfSectionWallPolygon(
        double r0Mm,
        double uOuterMm,
        double innerRadiusMm,
        double heightMm,
        int samples = GeneratorSampleCount)
    {
        if (!(r0Mm > 0) || !(heightMm > 0) || samples < 4)
            return Array.Empty<(double, double)>();

        var ri = innerRadiusMm > 0 && innerRadiusMm < r0Mm ? innerRadiusMm : 0.0;
        var uInner = ri > 0 ? uOuterMm * (ri / r0Mm) : 0.0;
        var n = samples;
        var pts = new (double X, double Y)[2 * (n + 1) + 1];
        var k = 0;
        for (var i = 0; i <= n; i++)
        {
            var y = heightMm * i / n;
            pts[k++] = (RadiusAtHeight(r0Mm, uOuterMm, y, heightMm), y);
        }
        for (var i = n; i >= 0; i--)
        {
            var y = heightMm * i / n;
            var xIn = ri > 0 ? RadiusAtHeight(ri, uInner, y, heightMm) : 0.0;
            pts[k++] = (xIn, y);
        }
        pts[k] = pts[0];
        return pts;
    }

    /// <summary>Horizontal hatch chords across the +X wall (solid: axis to outer; tube: inner to outer).</summary>
    public static IReadOnlyList<(double X0, double Y, double X1)> BuildHalfSectionHatch(
        double r0Mm,
        double uOuterMm,
        double innerRadiusMm,
        double heightMm,
        int count = 16)
    {
        if (!(r0Mm > 0) || !(heightMm > 0) || count < 2)
            return Array.Empty<(double, double, double)>();

        var ri = innerRadiusMm > 0 && innerRadiusMm < r0Mm ? innerRadiusMm : 0.0;
        var uInner = ri > 0 ? uOuterMm * (ri / r0Mm) : 0.0;
        var list = new (double, double, double)[count - 1];
        var k = 0;
        for (var i = 1; i < count; i++)
        {
            var y = heightMm * i / count;
            var xOut = RadiusAtHeight(r0Mm, uOuterMm, y, heightMm);
            var xIn = ri > 0 ? RadiusAtHeight(ri, uInner, y, heightMm) : 0.0;
            if (xOut < xIn) (xOut, xIn) = (xIn, xOut);
            list[k++] = (xIn, y, xOut);
        }
        return list;
    }

    /// <summary>
    /// Periodic cosine interpolation of measured u around the sensor ring.
    /// Skips NaN/Inf samples (does not replace them with 0). A query that falls
    /// in an empty arc larger than <see cref="MaxInterpolateArcDeg"/> fails
    /// (leave a gap). A single valid sample is used at every azimuth.
    /// </summary>
    public static bool TryInterpolateUAtAngleDeg(
        IReadOnlyList<ContourSensorPoint>? sensors,
        double targetDeg,
        out double uMm)
    {
        uMm = 0;
        if (sensors is null || sensors.Count == 0)
            return false;

        var nValid = 0;
        double onlyU = 0;
        for (var i = 0; i < sensors.Count; i++)
        {
            var u = sensors[i].RadialDisplacementMm;
            if (!double.IsFinite(u))
                continue;
            nValid++;
            onlyU = u;
        }
        if (nValid == 0)
            return false;
        if (nValid == 1)
        {
            uMm = onlyU;
            return true;
        }

        var n = nValid;
        var packed = new (double A, double U)[n];
        var k = 0;
        for (var i = 0; i < sensors.Count; i++)
        {
            var u = sensors[i].RadialDisplacementMm;
            if (!double.IsFinite(u))
                continue;
            packed[k++] = (NormalizeDeg(sensors[i].AngleDeg), u);
        }
        Array.Sort(packed, (a, b) => a.A.CompareTo(b.A));

        var angles = new double[n + 1];
        var us = new double[n + 1];
        for (var i = 0; i < n; i++)
        {
            angles[i] = packed[i].A;
            us[i] = packed[i].U;
        }
        angles[n] = packed[0].A + 360.0;
        us[n] = packed[0].U;

        var q = NormalizeDeg(targetDeg);
        if (q < angles[0])
            q += 360.0;

        for (var i = 0; i < n; i++)
        {
            if (q < angles[i] || q > angles[i + 1])
                continue;
            var span = angles[i + 1] - angles[i];
            if (span > MaxInterpolateArcDeg + 1e-9)
                return false;
            if (span <= 1e-12)
            {
                uMm = us[i];
                return true;
            }
            var t = (q - angles[i]) / span;
            var s = 0.5 - 0.5 * Math.Cos(Math.PI * Math.Clamp(t, 0, 1));
            uMm = us[i] * (1 - s) + us[i + 1] * s;
            return true;
        }
        return false;
    }

    /// <summary>Measured u interpolated on the ring at <paramref name="targetDeg"/>, else <paramref name="fallback"/>.</summary>
    public static double UAtAngleDeg(
        IReadOnlyList<ContourSensorPoint> sensors,
        double targetDeg,
        double fallback)
    {
        if (TryInterpolateUAtAngleDeg(sensors, targetDeg, out var u))
            return u;
        if (sensors is null || sensors.Count == 0)
            return fallback;

        var best = fallback;
        var bestDist = double.PositiveInfinity;
        foreach (var s in sensors)
        {
            if (!double.IsFinite(s.RadialDisplacementMm))
                continue;
            var d = AngularDistanceDeg(s.AngleDeg, targetDeg);
            if (d < bestDist)
            {
                bestDist = d;
                best = s.RadialDisplacementMm;
            }
        }
        return best;
    }

    private static double Lerp(double a, double b, double t)
        => a + (b - a) * t;

    public static double AngularDistanceDeg(double aDeg, double bDeg)
    {
        var d = Math.Abs(NormalizeDeg(aDeg) - NormalizeDeg(bDeg)) % 360.0;
        return d > 180.0 ? 360.0 - d : d;
    }

    private static double NormalizeDeg(double deg)
    {
        if (double.IsNaN(deg) || double.IsInfinity(deg)) return 0;
        deg %= 360.0;
        if (deg < 0) deg += 360.0;
        return deg;
    }
}

/// <summary>
/// One height band of the barreled elevation silhouette (visual generators).
/// </summary>
public readonly struct BarrelFillBand
{
    public double Y0 { get; init; }
    public double Y1 { get; init; }
    /// <summary>Un-overlapped band center (sensor-height model uses this y).</summary>
    public double YMid { get; init; }
    public double XLeft0 { get; init; }
    public double XRight0 { get; init; }
    public double XLeft1 { get; init; }
    public double XRight1 { get; init; }
}

/// <summary>
/// One fill cell on the elevation: visual quad + true measured u at (θ, z).
/// </summary>
public readonly struct BarrelFillCell
{
    public double Y0 { get; init; }
    public double Y1 { get; init; }
    public double XLeft0 { get; init; }
    public double XRight0 { get; init; }
    public double XLeft1 { get; init; }
    public double XRight1 { get; init; }
    /// <summary>True radial displacement from the sensor ring (height-faded to platens).</summary>
    public double U { get; init; }
}
