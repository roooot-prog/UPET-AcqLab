using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Sensors;
using Spider8DAQ.Core.Specimens;
using Xunit;

namespace Spider8DAQ.Core.Tests.Sensors;

public class StrainAutorangeTests
{
    [Theory]
    [InlineData(SpecimenClasses.Sare, 10000)]
    [InlineData(SpecimenClasses.Roca, 10000)]
    [InlineData(SpecimenClasses.Beton, 10000)]
    [InlineData(SpecimenClasses.Metal, 2000)]
    [InlineData(SpecimenClasses.Personalizat, 5000)]
    [InlineData("", 5000)]
    public void SpecimenClass_suggests_discrete_domain(string cls, double expected)
    {
        Assert.Equal(expected, StrainAutorange.SuggestFromSpecimenClass(cls));
    }

    [Fact]
    public void PickDomain_uses_smallest_step_with_headroom()
    {
        var floor = StrainScale.FromGaugeFactor(BridgeType.Half, 2.0, StrainScale.HalfConfigActivDummyT, 0.3);
        Assert.Equal(2000, floor);

        Assert.Equal(2000, StrainAutorange.PickDomain(800, null, floor));
        Assert.Equal(5000, StrainAutorange.PickDomain(2000, null, floor));
        Assert.Equal(10000, StrainAutorange.PickDomain(4000, null, floor));
        Assert.Equal(20000, StrainAutorange.PickDomain(9000, null, floor));
        Assert.Equal(20000, StrainAutorange.PickDomain(20000, null, floor));
    }

    [Fact]
    public void PickDomain_never_below_formula_floor()
    {
        var floor4000 = StrainScale.FromGaugeFactor(BridgeType.Half, 1.0, StrainScale.HalfConfigActivDummyT, 0.3);
        Assert.Equal(4000, floor4000);
        // 1.5 × 100 = 150 would pick 2000, but floor 4000 snaps to 5000.
        Assert.Equal(5000, StrainAutorange.PickDomain(100, SpecimenClasses.Metal, floor4000));
    }

    [Fact]
    public void PickDomain_without_peak_uses_specimen_then_floor()
    {
        var floor = 2000.0;
        Assert.Equal(2000, StrainAutorange.PickDomain(null, SpecimenClasses.Metal, floor));
        Assert.Equal(10000, StrainAutorange.PickDomain(null, SpecimenClasses.Roca, floor));
        Assert.Equal(5000, StrainAutorange.PickDomain(null, "", floor));
    }

    [Fact]
    public void HardwareRange_snaps_to_3_or_12()
    {
        Assert.Equal(3.0, StrainAutorange.HardwareRangeMvPerV(2000, 2000));
        Assert.Equal(3.0, StrainAutorange.HardwareRangeMvPerV(5000, 2000));
        Assert.Equal(12.0, StrainAutorange.HardwareRangeMvPerV(10000, 2000));
        Assert.Equal(12.0, StrainAutorange.HardwareRangeMvPerV(20000, 2000));
    }

    [Fact]
    public void NearZero_after_Zero_is_not_a_live_peak()
    {
        Assert.False(StrainAutorange.IsMeaningfulPeak(5, 2000));
        Assert.True(StrainAutorange.IsMeaningfulPeak(80, 2000));
        Assert.Equal(10000, StrainAutorange.PickDomain(5, SpecimenClasses.Roca, 2000));
    }

    [Fact]
    public void PickDomain_ignores_polluted_91885_as_formula_floor()
    {
        // 91885 is not GF Scale — must not force domain to 20000.
        Assert.Equal(2000, StrainAutorange.PickDomain(null, SpecimenClasses.Metal, 91885));
        Assert.Equal(10000, StrainAutorange.PickDomain(null, SpecimenClasses.Roca, 91885));
    }

    [Fact]
    public void Overflow_at_95_percent_of_domain()
    {
        Assert.False(StrainAutorange.IsOverflow(1899, 2000));
        Assert.True(StrainAutorange.IsOverflow(1901, 2000));
        Assert.True(StrainAutorange.IsOverflow(-9600, 10000));
        Assert.Contains("Overflow", StrainAutorange.OverflowMessage("CH0"), StringComparison.Ordinal);
    }
}
