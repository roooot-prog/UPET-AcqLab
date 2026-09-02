using Spider8DAQ.Core.Analysis;
using Xunit;

namespace Spider8DAQ.Core.Tests.Analysis;

public class PolynomialFitTests
{
    [Fact]
    public void PolynomialFit_Degree2_RecoversKnownQuadratic()
    {
        // y = 1 + 2x + 3x^2
        var xs = Enumerable.Range(0, 21).Select(i => i * 0.5).ToArray();
        var ys = xs.Select(x => 1 + 2 * x + 3 * x * x).ToArray();
        var fit = SignalAnalysis.PolynomialFit(xs, ys, 2);
        Assert.True(fit.Count >= 3);
        Assert.True(fit.RSquared > 0.999);
        Assert.Equal(3, fit.Coeffs.Length);
        Assert.InRange(fit.Coeffs[0], 0.99, 1.01);
        Assert.InRange(fit.Coeffs[1], 1.99, 2.01);
        Assert.InRange(fit.Coeffs[2], 2.99, 3.01);
    }
}
