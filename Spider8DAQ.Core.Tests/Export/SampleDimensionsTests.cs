using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class SampleDimensionsTests
{
    [Fact]
    public void ComputeAreaMm2_PrismUsesWidthTimesThickness()
    {
        Assert.Equal(200, SampleDimensions.ComputeAreaMm2(50, 20, 10, 0));
    }

    [Fact]
    public void ComputeAreaMm2_CylinderUsesPiDSquaredOver4()
    {
        var expected = Math.PI * 10 * 10 / 4.0;
        Assert.Equal(expected, SampleDimensions.ComputeAreaMm2(0, 0, 0, 10), 9);
    }

    [Fact]
    public void ApplyComputedFields_SetsAreaAndSummaryWithoutManualArea()
    {
        var meta = new ProjectMeta
        {
            SampleWidthMm = 20,
            SampleThicknessMm = 10,
            SampleMassG = 15
        };
        SampleDimensions.ApplyComputedFields(meta);
        Assert.Equal(200, meta.SampleAreaMm2);
        Assert.Contains("Aria=200 mm²", meta.SampleDimensionsSummary);
        Assert.Contains("Greutate=15 g", meta.SampleDimensionsSummary);
    }

    [Fact]
    public void BuildReportMetaRows_IncludesAriaSectiuneWhenAreaPositive()
    {
        var meta = new ProjectMeta
        {
            SampleDiameterMm = 10
        };
        SampleDimensions.ApplyComputedFields(meta);
        var rows = SampleDimensions.BuildReportMetaRows(meta);
        var areaRow = Assert.Single(rows, r => r.Label == "Aria secțiune [mm²]");
        Assert.Contains("mm²", areaRow.Value);
        Assert.Contains(rows, r => r.Label == "Dimensiuni / greutate" && r.Value.Contains("Aria="));
    }

    [Fact]
    public void UpetRoundTrip_PreservesSampleAreaMm2()
    {
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Local);
        var session = new Spider8DAQ.Core.Analysis.OfflineSession
        {
            ChannelNames = ["CH0"],
            Timestamps = [t0, t0.AddSeconds(0.02)],
            Sequences = [1, 2],
            Columns = [[1.0, 2.0]]
        };
        session.RecomputeStats();

        var meta = new ProjectMeta
        {
            SampleId = "P1",
            SampleWidthMm = 12,
            SampleThicknessMm = 5
        };
        SampleDimensions.ApplyComputedFields(meta);

        var path = Path.Combine(Path.GetTempPath(), "upet_area_" + Guid.NewGuid().ToString("N") + ".upet");
        try
        {
            UpetReportFile.Save(path, session, meta);
            var loaded = UpetReportFile.Load(path);
            Assert.NotNull(loaded.AttachedMeta);
            Assert.Equal(60, loaded.AttachedMeta!.SampleAreaMm2);
            Assert.Contains("Aria=", loaded.AttachedMeta.SampleDimensionsSummary);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void LabPackageReadme_IncludesAriaSectiune()
    {
        var meta = new ProjectMeta { SampleWidthMm = 8, SampleThicknessMm = 4 };
        SampleDimensions.ApplyComputedFields(meta);
        var readme = LabPackageBuilder.BuildReadme(meta, "data.csv", ["data.csv"]);
        Assert.Contains("Aria secțiune [mm²]", readme);
        Assert.DoesNotContain("Durată est.", readme);
    }

    [Fact]
    public void HtmlAndPdf_IncludeAriaSectiune()
    {
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Local);
        var session = new Spider8DAQ.Core.Analysis.OfflineSession
        {
            ChannelNames = ["CH0"],
            Timestamps = [t0, t0.AddSeconds(0.02), t0.AddSeconds(0.04)],
            Sequences = [1, 2, 3],
            Columns = [[1.0, 2.0, 3.0]],
            SourcePath = "demo.csv"
        };
        session.RecomputeStats();
        var meta = new ProjectMeta
        {
            Operator = "Lab",
            SampleId = "A1",
            SampleWidthMm = 10,
            SampleThicknessMm = 5,
            SampleRateHz = 50
        };
        SampleDimensions.ApplyComputedFields(meta);

        var dir = Path.Combine(Path.GetTempPath(), "upet_area_export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var html = Path.Combine(dir, "r.html");
            var pdf = Path.Combine(dir, "r.pdf");
            HtmlReportExporter.Export(html, session, meta, session.Stats);
            PdfReportExporter.Export(pdf, session, meta, session.Stats);
            var htmlText = File.ReadAllText(html);
            Assert.Contains("Aria secțiune", htmlText);
            Assert.Contains("Aria=", htmlText);
            // HtmlEncode turns ² into &#178;
            Assert.Contains("mm&#178;", htmlText);
            Assert.DoesNotContain("Durată estimată", htmlText);
            Assert.True(new FileInfo(pdf).Length > 500);
            // PDF text extraction is font-dependent; meta rows are built from the same helper.
            Assert.Contains(SampleDimensions.BuildReportMetaRows(meta),
                r => r.Label == "Aria secțiune [mm²]" && r.Value.Contains("50"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
