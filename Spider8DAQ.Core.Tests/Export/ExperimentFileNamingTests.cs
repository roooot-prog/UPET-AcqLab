using Spider8DAQ.Core.Export;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class ExperimentFileNamingTests
{
    [Fact]
    public void BuildFileName_Csv_HasNoRoleSuffix()
    {
        var when = new DateTime(2026, 8, 15, 15, 30, 12);
        var name = ExperimentFileNaming.BuildFileName("TRAC-01", when, null, ".csv");
        Assert.Equal("TRAC-01_20260815_153012.csv", name);
    }

    [Fact]
    public void BuildFileName_BeforeAfterRaport_UsesUnifiedRoles()
    {
        var when = new DateTime(2026, 8, 15, 15, 30, 12);
        Assert.Equal(
            "proba_20260815_153012_before.jpg",
            ExperimentFileNaming.BuildFileName("proba", when, ExperimentFileNaming.RoleBefore, ".jpg"));
        Assert.Equal(
            "proba_20260815_153012_after.jpg",
            ExperimentFileNaming.BuildFileName("proba", when, ExperimentFileNaming.RoleAfter, ".jpg"));
        Assert.Equal(
            "proba_20260815_153012_raport.xlsx",
            ExperimentFileNaming.BuildFileName("proba", when, ExperimentFileNaming.RoleRaport, "xlsx"));
        Assert.Equal(
            "proba_20260815_153012_industrial.pdf",
            ExperimentFileNaming.BuildFileName("proba", when, ExperimentFileNaming.RoleIndustrial, ".pdf"));
    }

    [Fact]
    public void SanitizeToken_ReplacesInvalidChars()
    {
        var s = ExperimentFileNaming.SanitizeToken("a/b:c*");
        Assert.DoesNotContain("/", s);
        Assert.DoesNotContain(":", s);
        Assert.DoesNotContain("*", s);
    }
}
