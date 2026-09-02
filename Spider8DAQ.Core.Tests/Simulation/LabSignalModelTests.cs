using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Simulation;
using Xunit;

namespace Spider8DAQ.Core.Tests.Simulation;

public class LabSignalModelTests
{
    [Fact]
    public void CompressionLoadFraction_RisesPeaksAndUnloads()
    {
        var cycle = 40.0;
        var early = LabSignalModel.CompressionLoadFraction(0.1 * cycle, cycle);
        var mid = LabSignalModel.CompressionLoadFraction(0.55 * cycle, cycle);
        var late = LabSignalModel.CompressionLoadFraction(0.95 * cycle, cycle);
        Assert.True(early < 0.35, $"early={early}");
        Assert.True(mid > 0.98, $"mid={mid}");
        Assert.True(late < 0.25, $"late={late}");
        Assert.True(mid > early && mid > late);
    }

    [Fact]
    public void ContourEvaluate_ForcePeaks_StrokeLags_RadialsAsymmetric()
    {
        var model = new LabSignalModel(6, seed: 7);
        var roles = new[]
        {
            SimChannelRole.RadialDisplacement,
            SimChannelRole.RadialDisplacement,
            SimChannelRole.RadialDisplacement,
            SimChannelRole.RadialDisplacement,
            SimChannelRole.Stroke,
            SimChannelRole.Force
        };
        var angles = new[] { 0.0, 90.0, 180.0, 270.0 };
        var hint = new SimulatorScenarioHint
        {
            Kind = SimulatorScenarioKind.CylinderContour,
            CycleSeconds = 40,
            PeakForce = 100_000,
            PeakStrokeMm = 3,
            PeakRadialBulgeMm = 0.5
        };
        var eng = new double[6];
        var noise = new Random(0);

        // Near Fmax
        model.Evaluate(0.55 * 40, roles, angles, hint, SimulatorScenarioKind.CylinderContour, eng, noise);
        var fPeak = eng[5];
        var strokePeak = eng[4];
        var radialsPeak = eng.Take(4).ToArray();

        // After unload
        model.Evaluate(0.98 * 40, roles, angles, hint, SimulatorScenarioKind.CylinderContour, eng, noise);
        var fEnd = eng[5];
        var strokeEnd = eng[4];
        var radialsEnd = eng.Take(4).ToArray();

        Assert.True(fPeak > 80_000, $"Fpeak={fPeak}");
        Assert.True(fEnd < fPeak * 0.3, $"Fend={fEnd}");
        Assert.True(strokePeak > 1.5, $"strokePeak={strokePeak}");
        // Plasticity: stroke recovers less than force
        Assert.True(strokeEnd > strokePeak * 0.35, $"stroke residual={strokeEnd}");
        Assert.True(radialsPeak.Average() > 0.2, "bulge at peak");
        Assert.True(radialsEnd.Average() < radialsPeak.Average() * 0.4, "bulge unloads");
        Assert.True(radialsPeak.Max() - radialsPeak.Min() > 0.01, "ovality / asymmetry");
    }

    [Fact]
    public void SoftZeroPath_ToRawThenApply_NearZeroAtStart()
    {
        var model = new LabSignalModel(2, seed: 1);
        var roles = new[] { SimChannelRole.Force, SimChannelRole.Stroke };
        var eng = new double[2];
        model.Evaluate(0, roles, null, null, SimulatorScenarioKind.CylinderContour, eng, new Random(1));
        var scale = 1.0;
        var raw0 = LabSignalModel.ToRaw(eng[0], scale, 1e-5);
        var ch = new ChannelConfig { Scale = scale, TareValue = raw0 };
        var physical = ch.Apply(raw0);
        Assert.True(Math.Abs(physical) < 1e-9, $"soft-zero physical={physical}");
    }

    [Fact]
    public void Classifier_ManyMmPlusForce_ResolvesContour()
    {
        var channels = new List<ChannelConfig>
        {
            new() { Index = 0, Name = "CH0", Unit = "mm" },
            new() { Index = 1, Name = "CH1", Unit = "mm" },
            new() { Index = 2, Name = "CH2", Unit = "mm" },
            new() { Index = 3, Name = "CH3", Unit = "mm" },
            new() { Index = 4, Name = "CH4", Unit = "mm" },
            new() { Index = 5, Name = "CH5", Unit = "N" }
        };
        var kind = SimChannelClassifier.ResolveKind(null, channels);
        Assert.Equal(SimulatorScenarioKind.CylinderContour, kind);
        var roles = SimChannelClassifier.ClassifyAll(channels, null, kind);
        Assert.Contains(SimChannelRole.Force, roles);
        Assert.Contains(SimChannelRole.Stroke, roles);
        Assert.True(roles.Count(r => r == SimChannelRole.RadialDisplacement) >= 3);
    }

    [Fact]
    public void Classifier_UsesContourHintIndices()
    {
        var channels = Enumerable.Range(0, 8)
            .Select(i => new ChannelConfig { Index = i, Name = $"CH{i}", Unit = "mV/V" })
            .ToList();
        var hint = new SimulatorScenarioHint
        {
            Kind = SimulatorScenarioKind.CylinderContour,
            ExperimentType = ExperimentTypes.CylinderContour,
            ForceChannelIndex = 5,
            StrokeChannelIndex = 4,
            RadialChannelIndices = new[] { 0, 1, 2, 3 },
            RadialAnglesDeg = CylinderContourConfig.DefaultAnglesDeg(4)
        };
        var roles = SimChannelClassifier.ClassifyAll(channels, hint, SimulatorScenarioKind.CylinderContour);
        Assert.Equal(SimChannelRole.Force, roles[5]);
        Assert.Equal(SimChannelRole.Stroke, roles[4]);
        Assert.Equal(SimChannelRole.RadialDisplacement, roles[0]);
        Assert.Equal(SimChannelRole.RadialDisplacement, roles[3]);
    }

    [Fact]
    public void CreateDefault_AssignsForceWhenRoom()
    {
        var cfg = CylinderContourConfig.CreateDefault(4, 100, availableChannelCount: 8);
        Assert.Equal(4, cfg.StrokeChannelIndex);
        Assert.Equal(5, cfg.ForceChannelIndex);
    }

    [Fact]
    public void OneShotRamp_LinearToAssignedTargets_ThenHold()
    {
        var asg = ContourSimAssignment.Default();
        var roles = Enumerable.Repeat(SimChannelRole.RadialDisplacement, 8).ToArray();
        var hint = new SimulatorScenarioHint
        {
            Kind = SimulatorScenarioKind.CylinderContour,
            CycleSeconds = ContourSimAssignment.DurationSeconds,
            OneShotRamp = true,
            RadialChannelIndices = Enumerable.Range(0, 8).ToArray(),
            RadialTargetMm = asg.TargetsMm()
        };
        var model = new LabSignalModel(8, seed: 1);
        var eng = new double[8];
        var noise = new Random(0);

        model.Evaluate(0, roles, null, hint, SimulatorScenarioKind.CylinderContour, eng, noise);
        Assert.All(eng, v => Assert.Equal(0, v, 6));

        model.Evaluate(5, roles, null, hint, SimulatorScenarioKind.CylinderContour, eng, noise);
        Assert.Equal(4.0, eng[0], 6);
        Assert.Equal(0.5, eng[4], 6);
        Assert.Equal(1.5, eng[5], 6);

        model.Evaluate(10, roles, null, hint, SimulatorScenarioKind.CylinderContour, eng, noise);
        Assert.Equal(new[] { 8.0, 8.0, 8.0, 8.0, 1.0, 3.0, 3.0, 3.0 }, eng);

        model.Evaluate(12, roles, null, hint, SimulatorScenarioKind.CylinderContour, eng, noise);
        Assert.Equal(new[] { 8.0, 8.0, 8.0, 8.0, 1.0, 3.0, 3.0, 3.0 }, eng);
    }

    [Fact]
    public void Classifier_EightHintRadials_AreNotStolenForStroke()
    {
        var channels = Enumerable.Range(0, 8)
            .Select(i => new ChannelConfig { Index = i, Name = $"CH{i}", Unit = "mm" })
            .ToList();
        var hint = new SimulatorScenarioHint
        {
            Kind = SimulatorScenarioKind.CylinderContour,
            RadialChannelIndices = Enumerable.Range(0, 8).ToArray(),
            StrokeChannelIndex = 0,
            ForceChannelIndex = 7
        };
        var roles = SimChannelClassifier.ClassifyAll(channels, hint, SimulatorScenarioKind.CylinderContour);
        Assert.Equal(8, roles.Count(r => r == SimChannelRole.RadialDisplacement));
        Assert.DoesNotContain(SimChannelRole.Stroke, roles);
        Assert.DoesNotContain(SimChannelRole.Force, roles);
    }
}
