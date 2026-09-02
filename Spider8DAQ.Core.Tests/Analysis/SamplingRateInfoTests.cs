using Spider8DAQ.Core.Analysis;
using Xunit;

namespace Spider8DAQ.Core.Tests.Analysis;

public class SamplingRateInfoTests
{
    [Fact]
    public void EstimateEffectiveHz_FromSteadyDeltas()
    {
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var stamps = Enumerable.Range(0, 11)
            .Select(i => t0.AddSeconds(i * 0.032))
            .ToList();
        var fs = SamplingRateInfo.EstimateEffectiveHz(stamps, 50);
        Assert.InRange(fs, 31.0, 31.5);
        var dt = SamplingRateInfo.MeanDeltaSeconds(stamps);
        Assert.InRange(dt, 0.031, 0.033);
        var rel = SamplingRateInfo.RelativeSeconds(stamps);
        Assert.Equal(0.0, rel[0], 9);
        Assert.Equal(0.32, rel[^1], 5);
    }

    [Fact]
    public void FormatExperimentTime_IncludesStartStopDuration()
    {
        var t0 = new DateTime(2026, 8, 10, 13, 29, 36, DateTimeKind.Local);
        var stamps = new[] { t0, t0.AddMinutes(15).AddSeconds(26) };
        var line = SamplingRateInfo.FormatExperimentTime(stamps);
        Assert.Contains("Start", line, StringComparison.Ordinal);
        Assert.Contains("Stop", line, StringComparison.Ordinal);
        Assert.Contains("Durată", line, StringComparison.Ordinal);
        Assert.Contains("15 min", line, StringComparison.Ordinal);
    }

    [Fact]
    public void IsRelativeTimeHeader_RecognizesAliases()
    {
        Assert.True(SamplingRateInfo.IsRelativeTimeHeader("t_s"));
        Assert.True(SamplingRateInfo.IsRelativeTimeHeader("t [s]"));
        Assert.False(SamplingRateInfo.IsRelativeTimeHeader("CH0"));
    }
}
