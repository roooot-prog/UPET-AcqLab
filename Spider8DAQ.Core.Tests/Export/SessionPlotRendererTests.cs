using Spider8DAQ.Core.Export;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class SessionPlotRendererTests
{
    [Fact]
    public void PadRange_VaryingSignal_AddsFivePercent()
    {
        var (lo, hi) = SessionPlotRenderer.PadRange(0, 100, 0.05, absoluteFloor: 1e-6);
        Assert.Equal(-5, lo, 6);
        Assert.Equal(105, hi, 6);
    }

    [Fact]
    public void PadRange_ConstantNearZero_UsesSymmetricAbsolutePad()
    {
        var (lo, hi) = SessionPlotRenderer.PadRange(0, 0, 0.05, absoluteFloor: 0.05);
        Assert.Equal(-0.05, lo, 6);
        Assert.Equal(0.05, hi, 6);
    }

    [Fact]
    public void PadRange_ConstantForce_PadsRelativeToMagnitude()
    {
        // Constant 5000 N → ±5% of |value|, not a shared ±5250 dump of another sensor
        var (lo, hi) = SessionPlotRenderer.PadRange(5000, 5000, 0.05, absoluteFloor: 1e-6);
        Assert.Equal(4750, lo, 3);
        Assert.Equal(5250, hi, 3);
    }

    [Fact]
    public void PadRange_SmallPressureSwing_ScalesToChannelOnly()
    {
        var (lo, hi) = SessionPlotRenderer.PadRange(1.0, 2.0, 0.05, absoluteFloor: 1e-6);
        Assert.Equal(0.95, lo, 6);
        Assert.Equal(2.05, hi, 6);
    }
}
