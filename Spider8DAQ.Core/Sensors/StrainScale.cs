using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Core.Sensors;

/// <summary>
/// Convert Spider8 electrical value (mV/V from OMB digits) to strain µm/m.
/// Half Activ + timbru pasiv (compensare T°) (NI Quarter II): 4000/GF (BF=1);
/// Half Simplu: 2000/GF; Half Poisson: 2000/(GF*(1+ν)); Half Încovoiere: 1000/GF;
/// Full: 1000/GF; Quarter: 4000/GF.
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

    /// <summary>Engineering scale: physical = (mV/V − tare) × scale. Default Half Simplu.</summary>
    public static double FromGaugeFactor(BridgeType bridge, double gaugeFactor)
        => FromGaugeFactor(bridge, gaugeFactor, HalfConfigSimplu, 0.3);

    public static double FromGaugeFactor(BridgeType bridge, double gaugeFactor, string? halfConfig, double poissonRatio)
    {
        var gf = gaugeFactor > 1e-9 ? gaugeFactor : 2.0;
        var nu = poissonRatio > 0 && poissonRatio < 1 ? poissonRatio : 0.3;
        var cfg = string.IsNullOrWhiteSpace(halfConfig) ? HalfConfigSimplu : halfConfig.Trim();

        return bridge switch
        {
            BridgeType.Quarter => 4000.0 / gf,
            BridgeType.Full => 1000.0 / gf,
            BridgeType.Half => IsActivDummyT(cfg)
                ? 4000.0 / gf // NI Quarter II: one active strain arm (BF=1)
                : cfg.Equals(HalfConfigPoisson, StringComparison.OrdinalIgnoreCase)
                ? 2000.0 / (gf * (1.0 + nu))
                : cfg.Equals(HalfConfigIncovoiere, StringComparison.OrdinalIgnoreCase)
                    || cfg.Equals("Încovoiere", StringComparison.OrdinalIgnoreCase)
                    || cfg.Equals("Incovoiere", StringComparison.OrdinalIgnoreCase)
                    ? 1000.0 / gf
                    : 2000.0 / gf, // Simplu (2 active equal contribution)
            _ => 2000.0 / gf
        };
    }

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
           + " (4000/GF · B)";
}
