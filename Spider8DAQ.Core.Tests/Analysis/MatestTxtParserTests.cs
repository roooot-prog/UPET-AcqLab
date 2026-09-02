using Spider8DAQ.Core.Analysis;
using Xunit;

namespace Spider8DAQ.Core.Tests.Analysis;

public class MatestTxtParserTests
{
    [Fact]
    public void ParseContent_GraphSection_ReadsForceAndTime()
    {
        var txt =
            "[HEADER]\nfoo=1\n[GRAPH]\nSarcina[kN]\tTimp[sec]\n0.100\t0.0\n1.114\t1.5\n0.500\t3.0\n";
        var s = MatestTxtParser.ParseContent(txt, "test.txt");
        Assert.Equal(3, s.Points.Count);
        Assert.Equal(1.114, s.PeakForceKn, 3);
        Assert.Equal(1.5, s.PeakTimeSec, 3);
    }

    [Fact]
    public void ComparePeaks_ComputesDeltaPercent()
    {
        var matestPeak = 1.0;
        var upet = new[] { 0.2, 0.95, 1.05, 0.4 };
        var r = MatestTxtParser.ComparePeaks(matestPeak, upet);
        Assert.Equal(1.0, r.MatestPeakKn);
        Assert.Equal(1.05, r.UpetPeakKn);
        Assert.Equal(0.95, r.UpetAtMatestLevelKn); // nearest to 1.0
        Assert.InRange(r.DeltaPercent, -5.1, -4.9); // (0.95-1)/1 * 100 = -5
        Assert.Contains("Δ=", r.Summary);
    }

    [Fact]
    public void ToForceKn_ConvertsNewtons()
    {
        var kn = MatestTxtParser.ToForceKn(new[] { 1114.0 }, "N");
        Assert.Equal(1.114, kn[0], 3);
        var already = MatestTxtParser.ToForceKn(new[] { 1.114 }, "kN");
        Assert.Equal(1.114, already[0], 3);
    }
}
