namespace Spider8DAQ.Core.Physics;

/// <summary>Convert hydraulic pressure (bar) to equivalent mass (kg) via piston area.</summary>
public static class PressureToMass
{
    public const double StandardGravity = 9.80665;

    /// <summary>bar → Pa.</summary>
    public const double BarToPa = 1e5;

    /// <summary>cm² → m². Must be 1e-4 (NOT 1e-2 — that would inflate kg by ×100).</summary>
    public const double Cm2ToM2 = 1e-4;

    /// <summary>mm² → cm² (divide mm² by 100).</summary>
    public const double Mm2ToCm2 = 1e-2;

    /// <summary>UPET hydraulic press piston area (cm²).</summary>
    public const double DefaultPistonAreaCm2 = 201.06;

    /// <summary>Same area expressed in mm² — common mistaken entry in the cm² field.</summary>
    public const double DefaultPistonAreaMm2 = DefaultPistonAreaCm2 * 100.0;

    /// <summary>Legacy app default before hydraulic press calibration.</summary>
    public const double LegacyPistonAreaCm2 = 50;

    /// <summary>kg per bar for <see cref="DefaultPistonAreaCm2"/> (≈205.02).</summary>
    public const double DefaultKgPerBar = 205.02;

    /// <summary>Fixed live-gauge full-scale for UPET hydraulic press (50 t).</summary>
    public const double HydraulicPressGaugeMaxTons = 50.0;

    /// <summary><see cref="HydraulicPressGaugeMaxTons"/> expressed in kg (dial maximum).</summary>
    public const double HydraulicPressGaugeMaxKg = HydraulicPressGaugeMaxTons * 1000.0;

    /// <summary>Named calibration profile for UPET hydraulic press.</summary>
    public const string HydraulicPressProfileId = "presa-hidraulica-upet";
    public const string HydraulicPressProfileName = "Presa hidraulica UPET";

    /// <param name="pressureBar">Physical pressure after sensor scaling, in bar.</param>
    /// <param name="areaCm2">Piston area in cm².</param>
    public static double BarCm2ToKg(double pressureBar, double areaCm2)
    {
        if (double.IsNaN(pressureBar) || areaCm2 <= 0) return double.NaN;
        if (pressureBar == 0) return 0;

        var pressurePa = pressureBar * BarToPa;
        var areaM2 = areaCm2 * Cm2ToM2;
        var forceN = pressurePa * areaM2;
        return forceN / StandardGravity;
    }

    /// <summary>kg per bar for a given piston area in cm².</summary>
    public static double KgPerBar(double areaCm2) => BarCm2ToKg(1.0, areaCm2);

    /// <param name="pressureBar">Physical pressure after sensor scaling, in bar.</param>
    /// <param name="areaMm2">Piston area in mm².</param>
    public static double BarMm2ToKg(double pressureBar, double areaMm2)
        => BarCm2ToKg(pressureBar, areaMm2 * Mm2ToCm2);

    /// <summary>Normalize piston area from persisted settings (cm² field).</summary>
    public static double NormalizePistonAreaCm2(double areaCm2)
    {
        if (areaCm2 <= 0
            || Math.Abs(areaCm2 - LegacyPistonAreaCm2) < 0.001)
            return DefaultPistonAreaCm2;

        // mm² pasted into cm² field (20106 mm² = 201.06 cm²) — same ×100 error as using 1e-2 for cm²→m².
        if (areaCm2 > 2000
            && Math.Abs(areaCm2 - DefaultPistonAreaMm2) < 1.0)
            return DefaultPistonAreaCm2;

        if (areaCm2 > 2000)
            return areaCm2 / 100.0;

        return areaCm2;
    }

#if DEBUG
    /// <summary>Reference checks for UPET press (A=201.06 cm²).</summary>
    public static void AssertReferenceValues()
    {
        const double tol = 0.5;
        var kgPerBar = KgPerBar(DefaultPistonAreaCm2);
        if (Math.Abs(kgPerBar - DefaultKgPerBar) > tol)
            throw new InvalidOperationException($"KgPerBar expected ~{DefaultKgPerBar}, got {kgPerBar}");

        if (Math.Abs(BarCm2ToKg(1.0, DefaultPistonAreaCm2) - 205.02) > tol)
            throw new InvalidOperationException("1 bar reference failed");

        if (Math.Abs(BarCm2ToKg(0.016, DefaultPistonAreaCm2) - 3.28) > 0.1)
            throw new InvalidOperationException("0.016 bar reference failed");

        if (BarCm2ToKg(0, DefaultPistonAreaCm2) != 0)
            throw new InvalidOperationException("0 bar must yield 0 kg");

        if (Math.Abs(BarCm2ToKg(4.88, DefaultPistonAreaCm2) - 1000) > 5)
            throw new InvalidOperationException("4.88 bar (~1 t) reference failed");
    }
#endif
}
