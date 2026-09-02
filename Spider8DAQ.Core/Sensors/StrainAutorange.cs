using Spider8DAQ.Core.Specimens;

namespace Spider8DAQ.Core.Sensors;

/// <summary>
/// catman-style Autorange for strain: pick a discrete µm/m domain before Record,
/// lock Scale + Spider8 ASA range during Rec, overflow instead of mid-Rec AGC.
/// </summary>
public static class StrainAutorange
{
    public static readonly double[] DomainStepsUmPerM = StrainScale.AutorangeDomainStepsUmPerM;

    public const double PeakHeadroom = 1.5;
    public const double OverflowFraction = 0.95;
    /// <summary>Ignore near-zero live after Zero — that is not a measuring peak.</summary>
    public const double MeaningfulPeakUmPerM = 50;

    /// <summary>Specimen-class suggestion when no live |ε| peak is available yet.</summary>
    public static double SuggestFromSpecimenClass(string? specimenClass)
    {
        var cls = specimenClass?.Trim() ?? "";
        if (cls.Equals(SpecimenClasses.Metal, StringComparison.OrdinalIgnoreCase)
            || cls.Contains("metal", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("oțel", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("otel", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("steel", StringComparison.OrdinalIgnoreCase))
            return 2000;

        if (cls.Equals(SpecimenClasses.Sare, StringComparison.OrdinalIgnoreCase)
            || cls.Equals(SpecimenClasses.Roca, StringComparison.OrdinalIgnoreCase)
            || cls.Equals(SpecimenClasses.Beton, StringComparison.OrdinalIgnoreCase)
            || cls.Contains("sare", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("salt", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("roc", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("granit", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("beton", StringComparison.OrdinalIgnoreCase)
            || cls.Contains("concrete", StringComparison.OrdinalIgnoreCase))
            return 10000;

        return 5000;
    }

    /// <summary>
    /// Smallest list step ≥ 1.5 × live peak (or specimen suggestion). Never below
    /// the GF formula floor (4000/GF half-dummy default) so the first live sample cannot clip.
    /// </summary>
    public static double PickDomain(double? liveAbsPeakUmPerM, string? specimenClass, double formulaScaleFloor)
    {
        double needed;
        if (IsMeaningfulPeak(liveAbsPeakUmPerM, formulaScaleFloor))
            needed = liveAbsPeakUmPerM!.Value * PeakHeadroom;
        else
            needed = SuggestFromSpecimenClass(specimenClass);

        // Floor is GF conversion Scale (4000/(k·B)), never Capacity / 91885 / domain.
        var floor = double.IsFinite(formulaScaleFloor) && formulaScaleFloor > 0
            && !StrainScale.LooksAbsurdTimbruScale(formulaScaleFloor)
            ? formulaScaleFloor
            : 0;
        if (floor > 0 && needed < floor)
            needed = floor;

        foreach (var step in DomainStepsUmPerM)
        {
            if (step + 1e-9 >= needed)
                return step;
        }

        return DomainStepsUmPerM[^1];
    }

    /// <summary>Spider8 bridge ASA is only 3 or 12 mV/V.</summary>
    public static double HardwareRangeMvPerV(double domainUmPerM, double conversionScale)
    {
        var scale = Math.Abs(conversionScale);
        if (scale < 1e-12)
            return 3.0;
        var needed = domainUmPerM / scale;
        return needed > 3.5 ? 12.0 : 3.0;
    }

    public static bool IsMeaningfulPeak(double? liveAbsPeakUmPerM, double formulaScaleFloor)
    {
        if (liveAbsPeakUmPerM is not double peak || !double.IsFinite(peak))
            return false;
        var min = Math.Max(MeaningfulPeakUmPerM,
            double.IsFinite(formulaScaleFloor) && formulaScaleFloor > 0
                ? formulaScaleFloor * 0.02
                : 0);
        return peak >= min;
    }

    public static double SnapToList(double domainUmPerM)
    {
        foreach (var step in DomainStepsUmPerM)
        {
            if (step + 1e-9 >= domainUmPerM)
                return step;
        }

        return DomainStepsUmPerM[^1];
    }

    public static bool IsOverflow(double physicalUmPerM, double domainUmPerM)
        => domainUmPerM > 0
           && double.IsFinite(physicalUmPerM)
           && Math.Abs(physicalUmPerM) > OverflowFraction * domainUmPerM;

    public static string OverflowMessage(string channelName)
        => $"{channelName} Overflow — oprește, mărește domeniul sau lasă Autorange să aleagă înainte de Rec, Zero, reia";
}
