using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Metrology;
using Xunit;

namespace Spider8DAQ.Core.Tests.Metrology;

public class MetrologyTests
{
    [Fact]
    public void ForceFromMassKg_UsesStandardG()
    {
        var f = MetrologyMath.ForceFromMassKg(1.0);
        Assert.Equal(9.80665, f, 5);
    }

    [Fact]
    public void Median_OddAndEven()
    {
        Assert.Equal(2.0, RollingSampleWindow.Median(new[] { 1.0, 2.0, 3.0 }));
        Assert.Equal(2.5, RollingSampleWindow.Median(new[] { 1.0, 2.0, 3.0, 4.0 }));
    }

    [Fact]
    public void Linearity_PerfectLine_R2NearOne()
    {
        var f = new[] { 0.0, 100.0, 200.0, 300.0 };
        var e = new[] { 0.0, 10.0, 20.0, 30.0 };
        var r = MetrologyMath.FitStrainVsForce(f, e);
        Assert.True(r.PassesThreshold);
        Assert.True(r.RSquared >= 0.999);
        Assert.Equal(0.1, r.Slope, 6);
    }

    [Fact]
    public void Linearity_Noisy_FlagsBelowThreshold()
    {
        var f = new[] { 0.0, 100.0, 200.0, 300.0 };
        var e = new[] { 0.0, 5.0, 40.0, 10.0 };
        var r = MetrologyMath.FitStrainVsForce(f, e, 0.999);
        Assert.False(r.PassesThreshold);
    }

    [Fact]
    public void Biquad_PassesDc()
    {
        var f = new BiquadLowPass();
        f.Configure(50, 5, DigitalFilterKind.Bessel);
        f.SettleTo(100);
        var y = f.Process(100);
        Assert.InRange(y, 99.0, 101.0);
    }

    [Fact]
    public void Pipeline_CaptureStep_UsesWindow()
    {
        var pipe = new MetrologyPipeline { StepWindowSeconds = 0.5, AggregateKind = StepAggregateKind.Mean };
        var ch = new[]
        {
            new ChannelConfig { Index = 0, Enabled = true, FilterHz = 5, Capacity = 5000, Name = "U2B" },
            new ChannelConfig { Index = 1, Enabled = true, FilterHz = 5, Name = "Strain" }
        };
        pipe.ConfigureFilters(50, ch, DigitalFilterKind.Butterworth);
        for (var i = 0; i < 40; i++)
        {
            var physical = new[] { 980.665, 100.0 };
            pipe.ProcessPhysicalFrame(
                new SampleFrame { Sequence = i, Timestamp = DateTime.UtcNow },
                physical, ch);
        }

        var step = pipe.CaptureStep(new[] { "U2B", "Strain" }, "2kg", 2.0, 0);
        Assert.Equal(1, step.StepIndex);
        Assert.InRange(step.ReferenceForceN, 19.6, 19.7);
        Assert.InRange(step.ChannelValues[0], 900, 1100);
    }

    [Fact]
    public void ScaleSuspect_TriggersAboveCapacity()
    {
        var pipe = new MetrologyPipeline();
        var ch = new[]
        {
            new ChannelConfig { Index = 0, Enabled = true, FilterHz = 0, Capacity = 5000, Name = "U2B", Unit = "N" }
        };
        pipe.ConfigureFilters(50, ch, DigitalFilterKind.Off);
        var physical = new[] { 7000.0 };
        pipe.ProcessPhysicalFrame(new SampleFrame { Sequence = 1, Timestamp = DateTime.UtcNow }, physical, ch);
        var alert = pipe.CheckScaleSuspect(ch, idleAfterZero: false);
        Assert.NotNull(alert);
        Assert.Contains("Scale suspect", alert!.Message);
    }

    [Fact]
    public void HalfBridge_ThermalTip()
    {
        var tips = ConnectDiagnostics.CheckHalfBridgeThermal(new[]
        {
            new ChannelConfig
            {
                Enabled = true, Bridge = BridgeType.Half, Name = "CH2", Unit = "µm/m",
                SensorCategory = "Mărci tensometrice"
            }
        });
        Assert.NotEmpty(tips);
        Assert.Contains("compensare", tips[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PairForceStrain_BySequence()
    {
        var pairs = MetrologyMath.PairForceStrainBySequence(
            new long[] { 1, 2, 3 },
            new[] { 10.0, 20.0, 30.0 },
            new[] { 1.0, 2.0, 3.0 });
        Assert.Equal(3, pairs.Count);
        Assert.Equal(20.0, pairs[1].Force);
    }

    [Fact]
    public void DeltaPercent_Works()
    {
        var pct = MetrologyMath.DeltaPercent(110, 100);
        Assert.Equal(10.0, pct, 6);
    }
}
