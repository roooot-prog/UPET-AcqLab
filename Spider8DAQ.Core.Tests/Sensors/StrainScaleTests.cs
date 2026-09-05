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

    [Fact]
    public void LabBridgeFactor_MatchesExperimentDefaults()
    {
        Assert.Equal(1.0, StrainScale.LabBridgeFactor(BridgeType.Half, StrainScale.HalfConfigActivDummyT, 0.3), 6);
        Assert.Equal(1.3, StrainScale.LabBridgeFactor(BridgeType.Half, StrainScale.HalfConfigPoisson, 0.3), 6);
        Assert.Equal(1.33, StrainScale.LabBridgeFactor(BridgeType.Half, StrainScale.HalfConfigPoisson, 0.33), 6);
        Assert.Equal(2.0, StrainScale.LabBridgeFactor(BridgeType.Half, StrainScale.HalfConfigSimplu, 0.3), 6);
        Assert.Equal(4.0, StrainScale.LabBridgeFactor(BridgeType.Half, StrainScale.HalfConfigIncovoiere, 0.3), 6);
        Assert.Equal(1.0, StrainScale.LabBridgeFactor(BridgeType.Quarter, null, 0.3), 6);
        Assert.Equal(4.0, StrainScale.LabBridgeFactor(BridgeType.Full, null, 0.3), 6);
    }

    [Fact]
    public void FromLabBridgeFactor_HalfDummy_Gf212_Bf1_IsAbout1887()
    {
        var s = StrainScale.FromLabBridgeFactor(
            BridgeType.Half, 2.12, StrainScale.HalfConfigActivDummyT, 1.0);
        Assert.InRange(s, 1886.7, 1886.9);
        Assert.Equal(s, StrainScale.FromGaugeFactor(2.12, 1.0), 6);
        Assert.Equal(
            StrainScale.FromGaugeFactor(BridgeType.Half, 2.12, StrainScale.HalfConfigActivDummyT, 0.3),
            s, 6);
    }

    [Fact]
    public void FromLabBridgeFactor_Poisson_UsesOnePlusNu_SameAsOldFormula()
    {
        var viaLab = StrainScale.FromLabBridgeFactor(
            BridgeType.Half, 2.0, StrainScale.HalfConfigPoisson, 1.3);
        var viaLegacy = StrainScale.FromGaugeFactor(BridgeType.Half, 2.0, "Poisson", 0.3);
        Assert.InRange(viaLab, 769.23, 769.24);
        Assert.Equal(viaLegacy, viaLab, 6);
        var overridden = StrainScale.FromLabBridgeFactor(
            BridgeType.Half, 2.0, StrainScale.HalfConfigPoisson, 1.4);
        Assert.InRange(overridden, 714.28, 714.29);
    }

    [Fact]
    public void FromLabBridgeFactor_IgnoresResistance()
    {
        var a = StrainScale.FromLabBridgeFactor(
            BridgeType.Half, 2.12, StrainScale.HalfConfigActivDummyT, 1.0);
        var b = StrainScale.FromGaugeFactor(
            BridgeType.Half, 2.12, StrainScale.HalfConfigActivDummyT, 0.3);
        Assert.Equal(a, b, 6);
    }
}
