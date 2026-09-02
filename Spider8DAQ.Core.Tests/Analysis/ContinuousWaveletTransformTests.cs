using Spider8DAQ.Core.Analysis;
using Xunit;

namespace Spider8DAQ.Core.Tests.Analysis;

public class ContinuousWaveletTransformTests
{
    [Fact]
    public void MorletCwt_DetectsSinusoidFrequencyBand()
    {
        const double fs = 200;
        const double f0 = 20;
        var n = 400;
        var samples = new double[n];
        for (var i = 0; i < n; i++)
            samples[i] = Math.Sin(2 * Math.PI * f0 * i / fs);

        var cwt = ContinuousWaveletTransform.Compute(
            samples, fs,
            new CwtOptions { MaxSamples = 256, ScaleCount = 32, MinFrequencyHz = 5, MaxFrequencyHz = 60 },
            "sine");

        Assert.True(cwt.AnalyzedSamples >= 64);
        Assert.Equal(cwt.FrequenciesHz.Length, cwt.Magnitudes.GetLength(0));
        Assert.Equal(cwt.TimesSec.Length, cwt.Magnitudes.GetLength(1));

        // Peak mean magnitude across time should be near f0
        var bestSi = 0;
        var best = double.MinValue;
        for (var si = 0; si < cwt.FrequenciesHz.Length; si++)
        {
            var sum = 0.0;
            var cols = cwt.TimesSec.Length;
            for (var t = cols / 4; t < cols * 3 / 4; t++)
                sum += cwt.Magnitudes[si, t];
            var mean = sum / Math.Max(1, cols / 2);
            if (mean > best)
            {
                best = mean;
                bestSi = si;
            }
        }

        Assert.InRange(cwt.FrequenciesHz[bestSi], f0 * 0.5, f0 * 2.0);
    }

    [Fact]
    public void Cwt_ShortSignal_ReturnsEmptyAnalyzed()
    {
        var cwt = ContinuousWaveletTransform.Compute(new double[] { 1, 2, 3 }, 50);
        Assert.Equal(0, cwt.AnalyzedSamples);
    }
}
