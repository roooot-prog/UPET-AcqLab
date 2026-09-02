using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Sensors;
using Xunit;

namespace Spider8DAQ.Core.Tests.Sensors;

public class StrainScaleTests
{
    [Fact]
    public void FromGaugeFactor_HalfSimplu_2000OverGf()
    {
        Assert.Equal(1000.0, StrainScale.FromGaugeFactor(BridgeType.Half, 2.0), 6);
        Assert.Equal(1000.0, StrainScale.FromGaugeFactor(BridgeType.Half, 2.0, "Simplu", 0.3), 6);
    }

    [Fact]
    public void FromGaugeFactor_HalfPoisson_UsesNu()
    {
        var s = StrainScale.FromGaugeFactor(BridgeType.Half, 2.0, "Poisson", 0.3);
        Assert.InRange(s, 769.23, 769.24);
    }

    [Fact]
    public void FromGaugeFactor_HalfIncovoiere_1000OverGf()
    {
        Assert.Equal(500.0, StrainScale.FromGaugeFactor(BridgeType.Half, 2.0, "Incovoiere", 0.3), 6);
    }

    [Fact]
    public void FromGaugeFactor_HalfActivDummyT_4000OverGf_LikeQuarter()
    {
        Assert.Equal(2000.0, StrainScale.FromGaugeFactor(BridgeType.Half, 2.0, "Activ + timbru pasiv (compensare T°)", 0.3), 6);
        Assert.Equal(2000.0, StrainScale.FromGaugeFactor(BridgeType.Half, 2.0, "Activ+dummy T", 0.3), 6); // legacy label
        Assert.Equal(2000.0, StrainScale.FromGaugeFactor(BridgeType.Quarter, 2.0), 6);
    }

    [Fact]
    public void FromGaugeFactor_Full_And_Quarter()
    {
        Assert.Equal(500.0, StrainScale.FromGaugeFactor(BridgeType.Full, 2.0), 6);
        Assert.Equal(2000.0, StrainScale.FromGaugeFactor(BridgeType.Quarter, 2.0), 6);
    }

    [Fact]
    public void HalfDummyT_Gf212_IsAbout1887_Not91885()
    {
        var s = StrainScale.FromGaugeFactor(
            BridgeType.Half, 2.12, StrainScale.HalfConfigActivDummyT, 0.3);
        Assert.InRange(s, 1886.7, 1886.9);
        Assert.True(StrainScale.LooksAbsurdTimbruScale(91885, s));
        Assert.False(StrainScale.LooksAbsurdTimbruScale(s, s));
        Assert.Equal(s, StrainScale.PinTimbruScale(91885, s), 6);
        Assert.Equal(s, StrainScale.PinTimbruScale(10000, s), 6);
        Assert.Equal(s, StrainScale.PinTimbruScale(5000, s), 6);
        Assert.Equal(-s, StrainScale.PinTimbruScale(-91885, s), 6);
    }

    [Fact]
    public void Resolve_DoesNotKeepCapacityDomainAsScale()
    {
        var dummy = StrainScale.FromGaugeFactor(
            BridgeType.Half, 2.12, StrainScale.HalfConfigActivDummyT, 0.3);
        var viaResolve = StrainScale.Resolve("µm/m", "Half", 91885, 2.12);
        Assert.False(StrainScale.LooksAbsurdTimbruScale(viaResolve, dummy));
        Assert.InRange(viaResolve, 900, 2000);
        Assert.False(StrainScale.LooksAbsurdTimbruScale(
            StrainScale.Resolve("µm/m", "Half", 10000, 2.12), dummy));
    }
}
