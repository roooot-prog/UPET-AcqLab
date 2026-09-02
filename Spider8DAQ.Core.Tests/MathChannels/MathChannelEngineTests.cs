using Spider8DAQ.Core.MathChannels;
using Xunit;

namespace Spider8DAQ.Core.Tests.MathChannels;

public class MathChannelEngineTests
{
    [Fact]
    public void Formula_CHn_MatchesGridIndex_ZeroBased()
    {
        // physical[1]=pressure, physical[2]=force — CH1/CH2 = grid names
        double[] physical = [10, 50, 200];
        var v = MathChannelEngine.EvaluateFormula("CH1-CH2", physical);
        Assert.Equal(50 - 200, v, 6);
    }

    [Fact]
    public void Formula_CH0_IsFirstChannel()
    {
        double[] physical = [3.5, 1, 2];
        var v = MathChannelEngine.EvaluateFormula("CH0*2", physical);
        Assert.Equal(7.0, v, 6);
    }
}
