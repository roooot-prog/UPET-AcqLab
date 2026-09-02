using Spider8DAQ.Core.Simulation;
using Xunit;

namespace Spider8DAQ.Core.Tests.Simulation;

public class ContourSimAssignmentTests
{
    [Fact]
    public void Default_IsFourHigh_OneLow_ThreeMid()
    {
        var a = ContourSimAssignment.Default();
        Assert.True(a.IsValid);
        Assert.Equal(new[] { 8.0, 8.0, 8.0, 8.0, 1.0, 3.0, 3.0, 3.0 }, a.TargetsMm());
        Assert.Contains("S1, S2, S3, S4 = 8 mm", a.ToSummaryRo());
        Assert.Contains("S5 = 1 mm", a.ToSummaryRo());
        Assert.Contains("S6, S7, S8 = 3 mm", a.ToSummaryRo());
        Assert.Contains("10 s", a.ToSummaryRo());
    }

    [Fact]
    public void RandomDraw_Seeded_IsDeterministicAndValid()
    {
        var a = ContourSimAssignment.RandomDraw(new Random(123));
        var b = ContourSimAssignment.RandomDraw(new Random(123));
        Assert.True(a.IsValid);
        Assert.Equal(a.HighSensorIndices, b.HighSensorIndices);
        Assert.Equal(a.LowSensorIndex, b.LowSensorIndex);

        var targets = a.TargetsMm();
        Assert.Equal(4, targets.Count(t => t == 8));
        Assert.Equal(1, targets.Count(t => t == 1));
        Assert.Equal(3, targets.Count(t => t == 3));
        Assert.Equal(8, targets.Length);
        Assert.DoesNotContain(a.LowSensorIndex, a.HighSensorIndices);
    }

    [Fact]
    public void RandomDraw_Unseeded_VariesAcrossCalls()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 40; i++)
        {
            var a = ContourSimAssignment.RandomDraw();
            Assert.True(a.IsValid);
            seen.Add(string.Join(",", a.HighSensorIndices) + "|" + a.LowSensorIndex);
        }

        Assert.True(seen.Count > 1, "expected more than one distinct random draw");
    }

    [Fact]
    public void TryValidate_RejectsOverlap()
    {
        Assert.False(ContourSimAssignment.TryValidate(new[] { 0, 1, 2, 3 }, 3, out var err));
        Assert.Contains("S4", err);
    }
}
