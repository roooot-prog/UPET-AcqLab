using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Analysis;

public class PoissonAndRecoveryTests
{
    [Fact]
    public void Poisson_NuEqualsMinusSlope()
    {
        // ε_t = −0.3 · ε_l
        var el = new double[50];
        var et = new double[50];
        for (var i = 0; i < 50; i++)
        {
            el[i] = i * 10.0;
            et[i] = -0.3 * el[i];
        }

        var r = PoissonRatioAnalysis.Compute(el, et, 0, 40);
        Assert.True(r.IsValid);
        Assert.Equal(0.3, r.Nu, 5);
        Assert.True(r.RSquared > 0.999);
    }

    [Fact]
    public void Poisson_AutoEarlyWindow_UsesFirstPortion()
    {
        var (a, b) = PoissonRatioAnalysis.AutoEarlyWindow(100, 0.35, 12);
        Assert.Equal(0, a);
        Assert.Equal(35, b);
    }

    [Fact]
    public void ElasticRecovery_ComputesPermanentAndRecovery()
    {
        // load 0→100, unload to 20 permanent
        var e = new double[30];
        for (var i = 0; i < 20; i++) e[i] = i * 5.0; // peak 95 at 19
        for (var i = 20; i < 30; i++) e[i] = 95 - (i - 19) * 7.5; // down toward ~20
        e[29] = 20;

        var r = ElasticRecoveryAnalysis.Compute(e, unloadStartIndex: 19, unloadEndIndex: 29);
        Assert.True(r.IsValid);
        Assert.Equal(0, r.Epsilon0, 5);
        Assert.Equal(95, r.EpsilonMax, 5);
        Assert.Equal(20, r.EpsilonEnd, 5);
        Assert.Equal(20, r.PermanentStrain, 5);
        Assert.Equal(75, r.ElasticRecovery, 5);
    }

    [Fact]
    public void StrainIndicators_ReportRows_Optional()
    {
        var meta = new ProjectMeta
        {
            ApparentPoissonNu = 0.28,
            ApparentPoissonSummary = "ν_ap≈0.28 · n=40",
            ElasticRecoverySummary = "ε_perm≈12 · recup≈80"
        };
        var rows = StrainAnalysisIndicators.BuildReportMetaRows(meta);
        Assert.Contains(rows, r => r.Label.Contains("ν aparent") && r.Value.Contains("0.28"));
        Assert.Contains(rows, r => r.Label.Contains("Recuperare") && r.Value.Contains("ε_perm"));
    }

    [Fact]
    public void StrainIndicators_OmitsWhenUnset()
    {
        var rows = StrainAnalysisIndicators.BuildReportMetaRows(new ProjectMeta());
        Assert.Empty(rows);
    }
}
