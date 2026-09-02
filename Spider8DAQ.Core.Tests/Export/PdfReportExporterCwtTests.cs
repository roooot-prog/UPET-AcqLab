using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class PdfReportExporterCwtTests
{
    [Fact]
    public void Export_IncludesCwtJpeg_OnSecondPage()
    {
        var session = MakeSineSession(n: 512, fs: 50);
        var dir = Path.Combine(Path.GetTempPath(), $"upet_pdf_cwt_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var pdfPath = Path.Combine(dir, "report.pdf");
        var ytPath = Path.Combine(dir, "yt.jpg");
        try
        {
            SessionPlotRenderer.SaveJpeg(session, ytPath, 800, 400);
            Assert.True(File.Exists(ytPath) && new FileInfo(ytPath).Length > 100);

            var cwt = CwtPlotRenderer.SavePerChannelJpegs(session, dir, fallbackSampleRateHz: 50);
            Assert.NotEmpty(cwt);
            Assert.All(cwt, t => Assert.True(File.Exists(t.Path) && new FileInfo(t.Path).Length > 1000));

            PdfReportExporter.Export(
                pdfPath,
                session,
                new ProjectMeta { Operator = "test", SampleRateHz = 50, SampleId = "CWT-PDF" },
                session.Stats,
                new[] { ytPath },
                cwt.Select(t => t.Path).ToList());

            Assert.True(File.Exists(pdfPath));
            var pdf = File.ReadAllBytes(pdfPath);
            Assert.True(pdf.Length > 10_000);
            // PDF header
            Assert.Equal(0x25, pdf[0]); // %
            Assert.Equal((byte)'P', pdf[1]);
            // Multi-page: /Count >= 2 and at least two /Page objects; CWT section string present
            var ascii = System.Text.Encoding.ASCII.GetString(pdf);
            Assert.Contains("/Type /Pages", ascii);
            // Parentheses are PDF-escaped as \( \)
            Assert.Contains("Analiza timp-frecventa \\(CWT - Morlet\\)", ascii);
            Assert.Contains("/Count 2", ascii);
            // Embedded DCT images: logo optional + yt + cwt => at least 2 DCT streams
            var dct = CountOccurrences(ascii, "/Filter /DCTDecode");
            Assert.True(dct >= 2, $"Expected >=2 JPEG XObjects, got {dct}");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void SavePerChannelJpegs_SkipsBadChannel_StillExportsOthers()
    {
        var session = MakeSineSession(n: 256, fs: 50);
        // Add an all-NaN inactive column — should be skipped, not throw
        session.ChannelNames.Add("Dead");
        session.Columns.Add(Enumerable.Repeat(double.NaN, session.Timestamps.Count).ToArray());
        session.RecomputeStats();

        var dir = Path.Combine(Path.GetTempPath(), $"upet_cwt_skip_{Guid.NewGuid():N}");
        try
        {
            var cwt = CwtPlotRenderer.SavePerChannelJpegs(session, dir, fallbackSampleRateHz: 50);
            Assert.Single(cwt);
            Assert.Equal("Force [N]", cwt[0].Name);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static OfflineSession MakeSineSession(int n, double fs)
    {
        var t0 = DateTime.UtcNow;
        var session = new OfflineSession
        {
            SourcePath = "unit-test",
            ChannelNames = { "Force [N]" },
            Columns = { new double[n] }
        };
        for (var i = 0; i < n; i++)
        {
            session.Timestamps.Add(t0.AddSeconds(i / fs));
            session.Sequences.Add(i);
            session.Columns[0][i] = Math.Sin(2 * Math.PI * 3.0 * i / fs) + 0.1 * Math.Sin(2 * Math.PI * 11.0 * i / fs);
        }
        session.CursorB = n - 1;
        session.RecomputeStats();
        return session;
    }

    private static int CountOccurrences(string hay, string needle)
    {
        var count = 0;
        for (var i = 0; (i = hay.IndexOf(needle, i, StringComparison.Ordinal)) >= 0; i += needle.Length)
            count++;
        return count;
    }
}
