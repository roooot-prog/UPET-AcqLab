using Spider8DAQ.Core.Physics;
using Xunit;

namespace Spider8DAQ.Core.Tests.Physics;

public class PressureToMassTests
{
    private const double Area = PressureToMass.DefaultPistonAreaCm2;

    [Fact]
    public void Cm2_to_m2_factor_is_1e4_not_1e2()
    {
        Assert.Equal(1e-4, PressureToMass.Cm2ToM2);
        Assert.NotEqual(1e-2, PressureToMass.Cm2ToM2);
    }

    [Theory]
    [InlineData(1.0, 205.02)]
    [InlineData(0.016, 3.28)]
    [InlineData(4.88, 1000.0)]
    [InlineData(3.8, 779.0)]
    public void BarCm2ToKg_UPET_reference(double bar, double expectedKg)
    {
        var kg = PressureToMass.BarCm2ToKg(bar, Area);
        Assert.InRange(kg, expectedKg - 1.0, expectedKg + 1.0);
    }

    [Fact]
    public void Zero_bar_yields_zero_kg()
    {
        Assert.Equal(0, PressureToMass.BarCm2ToKg(0, Area));
    }

    [Fact]
    public void Wrong_1e2_conversion_would_match_reported_bug()
    {
        var wrongKg = 0.016 * 1e5 * (Area * 1e-2) / PressureToMass.StandardGravity;
        Assert.InRange(wrongKg, 327, 329);
    }

    [Fact]
    public void Correct_formula_does_not_match_buggy_reading()
    {
        var kg = PressureToMass.BarCm2ToKg(0.016, Area);
        Assert.InRange(kg, 3.0, 3.6);
    }

    [Fact]
    public void Normalize_fixes_mm2_pasted_as_cm2()
    {
        Assert.Equal(Area, PressureToMass.NormalizePistonAreaCm2(20106));
        Assert.Equal(Area, PressureToMass.NormalizePistonAreaCm2(50));
        Assert.Equal(120.5, PressureToMass.NormalizePistonAreaCm2(120.5));
    }

    [Fact]
    public void Hydraulic_press_gauge_max_is_50_t()
    {
        Assert.Equal(50.0, PressureToMass.HydraulicPressGaugeMaxTons);
        Assert.Equal(50_000.0, PressureToMass.HydraulicPressGaugeMaxKg);
    }

    [Fact]
    public void P15_capacity_200_bar_is_below_50_t_gauge_max()
    {
        var kgAt200Bar = PressureToMass.BarCm2ToKg(200, Area);
        Assert.InRange(kgAt200Bar, 40_900, 41_200);
        Assert.True(kgAt200Bar < PressureToMass.HydraulicPressGaugeMaxKg);
    }
}
