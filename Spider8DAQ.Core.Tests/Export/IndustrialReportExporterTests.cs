using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class IndustrialReportExporterTests
{
    [Fact]
    public void Export_WritesPdf_WithValidityZoneAndDisclaimer()
    {
        var session = MakeForceSession(n: 100, fs: 50);
        session.CursorA = 10;
        session.CursorB = 80;
        var meta = new ProjectMeta
        {
            Operator = "Lab",
            SampleId = "IND-01",
            ProjectName = "Traction",
            Backend = "Simulator",
            SampleRateHz = 50,
            ExperimentType = "Tractiune",
            SampleWidthMm = 10,
            SampleThicknessMm = 5,
            MeasurementFingerprint = "UPET-ABCD-EF01-2345",
            ApparentPoissonNu = 0.28,
            ApparentPoissonSummary = "ν_ap≈0.28",
            ElasticRecoverySummary = "ε_perm≈0.1 · recup≈0.2",
            CalibrationNotes = "Zero inainte de Record"
        };
        SampleDimensions.ApplyComputedFields(meta);

        var dir = Path.Combine(Path.GetTempPath(), $"upet_ind_pdf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var pdfPath = Path.Combine(dir, "industrial.pdf");
        try
        {
            IndustrialReportExporter.Export(pdfPath, session, meta, new IndustrialReportExporter.Options
            {
                SoftwareVersion = "3.3.56",
                FingerprintStatusLabel = "Validă",
                CursorA = 10,
                CursorB = 80
            });

            Assert.True(File.Exists(pdfPath));
            var pdf = File.ReadAllBytes(pdfPath);
            Assert.True(pdf.Length > 800);
            Assert.Equal(0x25, pdf[0]);
            var ascii = System.Text.Encoding.ASCII.GetString(pdf);
            Assert.Contains("Raport industrial", ascii);
            Assert.Contains("Zona predare", ascii);
            Assert.Contains("index 10-80", ascii);
            Assert.Contains("Disclaimer", ascii);
            Assert.Contains("laborator metrologic", ascii);
            Assert.Contains("Semnatura expert", ascii);
            Assert.DoesNotContain("Responsabil laborator", ascii);
            Assert.Contains("/Count", ascii);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Export_FullRecording_WhenCursorsEqual()
    {
        var session = MakeForceSession(n: 40, fs: 50);
        session.CursorA = 0;
        session.CursorB = 0;
        var meta = new ProjectMeta { SampleId = "EQ", SampleRateHz = 50, Operator = "X" };
        var dir = Path.Combine(Path.GetTempPath(), $"upet_ind_eq_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var pdfPath = Path.Combine(dir, "eq.pdf");
        try
        {
            IndustrialReportExporter.Export(pdfPath, session, meta);
            var ascii = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdfPath));
            Assert.Contains("intreaga inregistrare", ascii);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static OfflineSession MakeForceSession(int n, double fs)
    {
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Local);
        var session = new OfflineSession
        {
            SourcePath = "unit-industrial.csv",
            ChannelNames = { "Force [N]", "eps_l" },
            Columns = { new double[n], new double[n] }
        };
        for (var i = 0; i < n; i++)
        {
            session.Timestamps.Add(t0.AddSeconds(i / fs));
            session.Sequences.Add(i);
            session.Columns[0][i] = 100 + i * 2.5;
            session.Columns[1][i] = i * 0.01;
        }
        session.CursorB = n - 1;
        session.RecomputeStats();
        return session;
    }
}
