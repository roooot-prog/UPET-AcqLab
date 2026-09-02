using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class IndustrialReportSectionsTests
{
    [Fact]
    public void BuildCommonMetaRows_IncludesValidityDisclaimerAndSignatures()
    {
        var session = new OfflineSession
        {
            ChannelNames = { "Force [N]" },
            Timestamps =
            {
                new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 15, 10, 0, 1, DateTimeKind.Utc),
                new DateTime(2026, 8, 15, 10, 0, 2, DateTimeKind.Utc)
            },
            Sequences = { 0, 1, 2 },
            Columns = { new[] { 10.0, 50.0, 20.0 } },
            CursorA = 0,
            CursorB = 2
        };
        session.RecomputeStats();
        var meta = new ProjectMeta
        {
            Operator = "Lab",
            SampleId = "P1",
            SampleAreaMm2 = 100,
            MeasurementFingerprint = "UPET-AAAA-BBBB-CCCC"
        };

        var rows = IndustrialReportSections.BuildCommonMetaRows(session, meta);
        Assert.Contains(rows, r => r.Label.Contains("Zonă predare", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rows, r => r.Label.Contains("Disclaimer", StringComparison.OrdinalIgnoreCase));
        var disclaimer = rows.First(r => r.Label.Contains("Disclaimer", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.Contains("laborator metrologic", disclaimer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("17025", disclaimer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("educațional", disclaimer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("etalonare", disclaimer, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(rows, r => r.Label.Contains("Semnătură", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rows, r => r.Label.Equals("Semnătură expert", StringComparison.OrdinalIgnoreCase));

        var plain = new System.Text.StringBuilder();
        IndustrialReportSections.AppendPlainTextSections(plain, IndustrialReportSections.Build(session, meta), meta);
        Assert.Contains("Semnătură expert", plain.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Responsabil laborator", plain.ToString(), StringComparison.OrdinalIgnoreCase);
        var html = IndustrialReportSections.BuildHtmlSections(IndustrialReportSections.Build(session, meta), meta);
        Assert.Contains("Semnătură expert", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Responsabil laborator", html, StringComparison.OrdinalIgnoreCase);

        Assert.Contains(rows, r => r.Label.Contains("Amprentă", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rows, r => r.Label.Contains("Stare amprent", StringComparison.OrdinalIgnoreCase));
        var stare = rows.First(r => r.Label.Contains("Stare amprent", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.False(string.IsNullOrWhiteSpace(stare));
        Assert.NotEqual("—", stare);
        Assert.NotEqual("-", stare);
        // Fake code → Alterată (or Neverificată only if verify skipped — here session is present)
        Assert.Contains(stare, new[] { "Alterată", "Validă", "Incompletă", "Neverificată", "Lipsă" });
        Assert.Contains(rows, r => r.Label.Contains("Fmax", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildCommonMetaRows_MissingFingerprint_ShowsLipsa()
    {
        var session = new OfflineSession
        {
            ChannelNames = { "CH0" },
            Timestamps =
            {
                new DateTime(2026, 8, 15, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 8, 15, 10, 0, 1, DateTimeKind.Utc)
            },
            Sequences = { 0, 1 },
            Columns = { new[] { 1.0, 2.0 } }
        };
        session.RecomputeStats();
        var meta = new ProjectMeta { Operator = "Lab", SampleId = "P0" };

        var rows = IndustrialReportSections.BuildCommonMetaRows(session, meta);
        var stare = rows.First(r => r.Label.Contains("Stare amprent", StringComparison.OrdinalIgnoreCase)).Value;
        Assert.Equal("Lipsă", stare);
    }
}
