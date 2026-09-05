using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Core.Sensors;

/// <summary>
/// Convert Spider8 electrical value (mV/V from OMB digits) to strain µm/m.
/// Scale = 4000/(GF·B). Lab BF (Asistent Timbru): quarter/half+dummy=1; Poisson=1+ν
/// (B=2·BF); Half Simplu=2; Încovoiere/Full=4. R Ω does not enter Scale.
/// </summary>
public static class StrainScale
{
    public const string HalfConfigSimplu = "Simplu";
    public const string HalfConfigPoisson = "Poisson";
    public const string HalfConfigIncovoiere = "Incovoiere";
    /// <summary>NI Quarter Bridge Type II: active on specimen + passive T° gauge on same channel (BF=1).</summary>
    public const string HalfConfigActivDummyT = "Activ + timbru pasiv (compensare T°)";

    public static bool IsStrainUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        return unit.Contains("µm/m", StringComparison.OrdinalIgnoreCase)
               || unit.Contains("um/m", StringComparison.OrdinalIgnoreCase)
               || unit.Contains("µε", StringComparison.OrdinalIgnoreCase)
               || unit.Contains("ue", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsActivDummyT(string? halfConfig)
    {
        if (string.IsNullOrWhiteSpace(halfConfig)) return false;
        var c = halfConfig.Trim();
        return c.Equals(HalfConfigActivDummyT, StringComparison.OrdinalIgnoreCase)
               || c.Contains("dummy", StringComparison.OrdinalIgnoreCase) // legacy label
               || c.Contains("timbru pasiv", StringComparison.OrdinalIgnoreCase)
               || c.Contains("compensare T", StringComparison.OrdinalIgnoreCase)
               || c.Contains("Quarter II", StringComparison.OrdinalIgnoreCase)
               || c.Contains("QuarterII", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPoisson(string? halfConfig)
        => !string.IsNullOrWhiteSpace(halfConfig)
           && halfConfig.Contains("Poiss", StringComparison.OrdinalIgnoreCase);

    public static bool IsIncovoiere(string? halfConfig)
    {
        if (string.IsNullOrWhiteSpace(halfConfig)) return false;
        var c = halfConfig.Trim();
        return c.Equals(HalfConfigIncovoiere, StringComparison.OrdinalIgnoreCase)
               || c.Equals("Încovoiere", StringComparison.OrdinalIgnoreCase)
               || c.Contains("ncovoi", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Factor de punte (BF) shown in Asistent Timbru.
    /// Quarter / Half+dummy T°: 1; Poisson: 1+ν; Half Simplu: 2; Încovoiere / Full: 4.
    /// </summary>
    public static double LabBridgeFactor(BridgeType bridge, string? halfConfig, double poissonRatio)
    {
        var nu = poissonRatio > 0 && poissonRatio < 1 ? poissonRatio : 0.3;
        var cfg = string.IsNullOrWhiteSpace(halfConfig) ? HalfConfigSimplu : halfConfig.Trim();
        return bridge switch
        {
            BridgeType.Quarter => 1.0,
            BridgeType.Full => 4.0, // Scale = 1000/GF = 4000/(GF·4)
            BridgeType.Half => IsActivDummyT(cfg) ? 1.0
                : IsPoisson(cfg) ? 1.0 + nu
                : IsIncovoiere(cfg) ? 4.0
                : 2.0, // Simplu
            _ => 2.0
        };
    }

    public static double LabBridgeFactor(string? bridge, string? halfConfig, double poissonRatio)
    {
        var b = Enum.TryParse<BridgeType>(bridge, true, out var parsed) ? parsed : BridgeType.Half;
        return LabBridgeFactor(b, halfConfig, poissonRatio);
    }

    /// <summary>
    /// Wheatstone B in Scale = 4000/(GF·B). Poisson lab BF is 1+ν, so B = 2·BF.
    /// </summary>
    public static double ToWheatstoneB(double labBridgeFactor, BridgeType bridge, string? halfConfig)
    {
        var bf = labBridgeFactor > 1e-9 ? labBridgeFactor : 1.0;
        if (bridge == BridgeType.Half && IsPoisson(halfConfig))
            return 2.0 * bf;
        return bf;
    }

    public static double ToWheatstoneB(double labBridgeFactor, string? bridge, string? halfConfig)
    {
        var b = Enum.TryParse<BridgeType>(bridge, true, out var parsed) ? parsed : BridgeType.Half;
        return ToWheatstoneB(labBridgeFactor, b, halfConfig);
    }

    /// <summary>Scale = 4000/(GF·B). R Ω does not enter.</summary>
    public static double FromGaugeFactor(double gaugeFactor, double wheatstoneB)
    {
        var gf = gaugeFactor > 1e-9 ? gaugeFactor : 2.0;
        var b = wheatstoneB > 1e-9 ? wheatstoneB : 1.0;
        return 4000.0 / (gf * b);
    }

    public static double FromLabBridgeFactor(
        BridgeType bridge, double gaugeFactor, string? halfConfig, double labBridgeFactor)
        => FromGaugeFactor(gaugeFactor, ToWheatstoneB(labBridgeFactor, bridge, halfConfig));

    public static double FromLabBridgeFactor(
        string? bridge, double gaugeFactor, string? halfConfig, double labBridgeFactor)
    {
        var b = Enum.TryParse<BridgeType>(bridge, true, out var parsed) ? parsed : BridgeType.Half;
        return FromLabBridgeFactor(b, gaugeFactor, halfConfig, labBridgeFactor);
    }

    /// <summary>Engineering scale: physical = (mV/V − tare) × scale. Default Half Simplu.</summary>
    public static double FromGaugeFactor(BridgeType bridge, double gaugeFactor)
        => FromGaugeFactor(bridge, gaugeFactor, HalfConfigSimplu, 0.3);

    public static double FromGaugeFactor(BridgeType bridge, double gaugeFactor, string? halfConfig, double poissonRatio)
        => FromLabBridgeFactor(bridge, gaugeFactor, halfConfig, LabBridgeFactor(bridge, halfConfig, poissonRatio));

    public static double FromGaugeFactor(string? bridge, double gaugeFactor)
    {
        var b = Enum.TryParse<BridgeType>(bridge, true, out var parsed) ? parsed : BridgeType.Half;
        return FromGaugeFactor(b, gaugeFactor);
    }

    public static double FromGaugeFactor(string? bridge, double gaugeFactor, string? halfConfig, double poissonRatio)
    {
        var b = Enum.TryParse<BridgeType>(bridge, true, out var parsed) ? parsed : BridgeType.Half;
        return FromGaugeFactor(b, gaugeFactor, halfConfig, poissonRatio);
    }

    /// <summary>
    /// If library Scale is still 1 but unit is strain, derive scale from GF (Sensitivity).
    /// </summary>
    public static double Resolve(string? unit, string? bridge, double libraryScale, double sensitivityGf)
    {
        if (!IsStrainUnit(unit)) return libraryScale;
        // Keep catalog 2000 (Half dummy GF=2). Drop only 91885 / domain 5k–20k.
        var a = Math.Abs(libraryScale);
        var polluted = a > TimbruScaleMaxPlausible
                       || a is >= 4999 and <= 5001
                       || a is >= 9999 and <= 10001
                       || a is >= 19999 and <= 20001;
        if (polluted)
            return sensitivityGf > 1e-6
                ? FromGaugeFactor(bridge, sensitivityGf)
                : FromGaugeFactor(BridgeType.Half, 2.0);
        if (Math.Abs(libraryScale - 1.0) > 1e-6 && Math.Abs(libraryScale) > 1e-9)
            return libraryScale;
        if (sensitivityGf > 1e-6)
            return FromGaugeFactor(bridge, sensitivityGf);
        return FromGaugeFactor(BridgeType.Half, 2.0);
    }

    /// <summary>Autorange µm/m domain list — never a Timbru conversion Scale (except 2000 ≈ 4000/GF=2).</summary>
    public static readonly double[] AutorangeDomainStepsUmPerM = [2000, 5000, 10000, 20000];

    public const double TimbruScaleMinPlausible = 500;
    public const double TimbruScaleMaxPlausible = 8000;

    /// <summary>
    /// True when |scale| is not a GF conversion (4000/(k·B) ≈ 500–4000).
    /// 91885, Autorange 5000/10000/20000, and ASA range do not belong in Scale.
    /// </summary>
    public static bool LooksAbsurdTimbruScale(double scale, double? formulaScale = null)
    {
        if (!double.IsFinite(scale) || scale == 0)
            return true;
        var a = Math.Abs(scale);
        if (a < TimbruScaleMinPlausible || a > TimbruScaleMaxPlausible)
            return true;
        if (formulaScale is double f && double.IsFinite(f) && Math.Abs(f) >= 10)
        {
            var ratio = a / Math.Abs(f);
            if (ratio > 2.5 || ratio < 0.35)
                return true;
        }

        return IsForeignDomainStep(a, formulaScale);
    }

    /// <summary>5000/10000/20000 are domain steps, never GF Scale. 2000 is OK when formula ≈ 2000.</summary>
    public static bool IsForeignDomainStep(double absScale, double? formulaScale)
    {
        foreach (var step in AutorangeDomainStepsUmPerM)
        {
            if (Math.Abs(absScale - step) > 0.6)
                continue;
            if (step <= 2000 && formulaScale is double f && Math.Abs(Math.Abs(f) - step) < 150)
                return false;
            return step > 2000 || formulaScale is double g && Math.Abs(Math.Abs(g) - step) >= 150;
        }

        return false;
    }

    /// <summary>Keep polarity; replace domain / 91885 / garbage with GF formula.</summary>
    public static double PinTimbruScale(double currentScale, double formulaScale)
    {
        if (!double.IsFinite(formulaScale) || Math.Abs(formulaScale) < 10)
            return currentScale;
        if (LooksAbsurdTimbruScale(currentScale, formulaScale))
            return currentScale < 0 ? -Math.Abs(formulaScale) : Math.Abs(formulaScale);
        return currentScale;
    }

    public static string SuggestedTimbruScaleLabel(double formulaScale)
        => "Scale=" + formulaScale.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
           + " (4000/(GF·B))";
}
