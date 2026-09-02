using System.Globalization;
namespace Spider8DAQ.Core.Display;

/// <summary>
/// Operator-facing number formatting. Does not change stored doubles / Scale physics.
/// </summary>
public static class EngineeringDisplay
{
    /// <summary>Live Citire: 1 decimal, culture-aware (comma vs point). <paramref name="unit"/> kept for callers.</summary>
    public static string FormatReading(double v, string? unit, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (double.IsNaN(v) || double.IsInfinity(v)) return "—";
        _ = unit;
        return v.ToString("0.0", culture);
    }

    public static string FormatRange(double mvPerV, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (double.IsNaN(mvPerV) || double.IsInfinity(mvPerV)) return "—";
        return mvPerV.ToString("0.0", culture);
    }

    /// <summary>Scale column: 1886.8 not 1886.7924528301885. Underlying value unchanged.</summary>
    public static string FormatScale(double scale, CultureInfo? culture = null, bool fullPrecision = false)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (double.IsNaN(scale) || double.IsInfinity(scale)) return "—";
        if (fullPrecision)
            return scale.ToString("G15", culture);
        return scale.ToString("0.0", culture);
    }

    public static string FormatExcitation(double volts, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return volts.ToString("0.##", culture);
    }

    public static string FormatGaugeFactor(double gf, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (gf <= 0) return "—";
        return gf.ToString("0.###", culture);
    }
}
