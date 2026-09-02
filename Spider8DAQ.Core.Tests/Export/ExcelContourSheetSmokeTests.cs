using ClosedXML.Excel;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

/// <summary>
/// Smoke: Contur Excel sheet must carry PNG and/or uᵢ/ovalitate cells when ContourConfig is set.
/// Mirrors ExcelReportExporter Contur sheet pipeline (App; Raport is first tab).
/// </summary>
public class ExcelContourSheetSmokeTests
{
    [Fact]
    public void ExcelConturSheet_WithContourConfig_HasPngAndOvalityCells()
    {
        var session = MakeSession(n: 40, channels: 5);
        var meta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleDiameterMm = 100,
            SampleRateHz = 50,
            CylinderContour = CylinderContourConfig.CreateDefault(4, 100)
        };
        meta.CylinderContour!.StrokeChannelIndex = 4;
        meta.CylinderContour.SensorChannelIndices = new List<int> { 0, 1, 2, 3 };

        Assert.True(CylinderContourExport.ShouldAttempt(meta, session));
        var result = CylinderContourExport.TryCompute(session, meta);
        Assert.NotNull(result);
        Assert.True(result!.IsValid, result.Error);

        var dir = Path.Combine(Path.GetTempPath(), $"upet_xlsx_contour_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var png = Path.Combine(dir, "contour.png");
        var xlsx = Path.Combine(dir, "raport.xlsx");
        try
        {
            CylinderContourPlotRenderer.BuildPlot(result).SavePng(
                png, CylinderContourPlotRenderer.DefaultWidth, CylinderContourPlotRenderer.DefaultHeight);
            Assert.True(File.Exists(png));
            var pngLen = new FileInfo(png).Length;
            Assert.True(pngLen > 0, "PNG Contur trebuie să aibă length > 0");

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Contur");
                ws.Position = 1;
                ws.Cell(1, 1).Value = "Contur cilindru — plan + elevație + secțiune (R0 + Contur Fmax · S1…Sn)";
                ws.Cell(2, 1).Value = "Status";
                ws.Cell(2, 2).Value = "OK — grafic Contur inclus (plan + elevație + secțiune)";
                var row = 4;
                ws.Cell(row, 1).Value = "Indicator";
                ws.Cell(row, 2).Value = "Valoare";
                row++;
                foreach (var (label, value) in result.BuildReportRows())
                {
                    ws.Cell(row, 1).Value = label;
                    ws.Cell(row, 2).Value = value;
                    row++;
                }
                ws.AddPicture(png).MoveTo(ws.Cell(row + 1, 1)).Scale(0.85);
                wb.Worksheets.Add("Raport").Cell(1, 1).Value = "cover";
                wb.SaveAs(xlsx);
            }

            Assert.True(File.Exists(xlsx));
            using var reopen = new XLWorkbook(xlsx);
            Assert.True(reopen.Worksheets.TryGetWorksheet("Contur", out var contur));
            Assert.Equal(1, contur.Position);
            Assert.True(contur.Pictures.Count >= 1, "Foaia Contur trebuie să conțină imaginea PNG");

            var used = contur.RangeUsed();
            Assert.NotNull(used);
            var blob = string.Join(" | ", used!.CellsUsed().Select(c => c.GetString()));
            Assert.Contains("Ovalitate", blob, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("bombare", blob, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("u_S", blob, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("OK", blob, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ExcelConturSheet_OnComputeFailure_StillWritesNonEmptyStatusCells()
    {
        var session = MakeSession(n: 10, channels: 2);
        var meta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleDiameterMm = 80,
            CylinderContour = new CylinderContourConfig
            {
                SensorCount = 4,
                SensorChannelIndices = new List<int> { 0, 1, 2, 3 },
                SensorAnglesDeg = CylinderContourConfig.DefaultAnglesDeg(4).ToList(),
                StrokeChannelIndex = 4,
                InitialRadiusMm = 40
            }
        };

        Assert.True(CylinderContourExport.ShouldAttempt(meta, session));
        var result = CylinderContourExport.TryCompute(session, meta);
        Assert.NotNull(result);
        Assert.False(result!.IsValid);
        var reason = result.Error ?? CylinderContourExport.DescribeSkipReason(meta, session);
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.Contains("canal", reason, StringComparison.OrdinalIgnoreCase);

        var dir = Path.Combine(Path.GetTempPath(), $"upet_xlsx_contour_err_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var xlsx = Path.Combine(dir, "raport_err.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Contur");
                ws.Position = 1;
                ws.Cell(1, 1).Value = "Contur cilindru";
                ws.Cell(2, 1).Value = "Status";
                ws.Cell(2, 2).Value = reason;
                wb.SaveAs(xlsx);
            }

            using var reopen = new XLWorkbook(xlsx);
            var ws2 = reopen.Worksheet("Contur");
            Assert.False(string.IsNullOrWhiteSpace(ws2.Cell(2, 2).GetString()));
            Assert.True(ws2.Cell(2, 2).GetString().Length > 8);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ShouldAttempt_InfersFromDiameterAndFiveChannels()
    {
        var session = MakeSession(n: 20, channels: 5);
        var meta = new ProjectMeta { SampleDiameterMm = 90, ExperimentType = "" };
        Assert.True(CylinderContourExport.CanInferFromDiameterAndChannels(meta, session));
        Assert.True(CylinderContourExport.ShouldAttempt(meta, session));
        var cfg = CylinderContourExport.EnsureConfig(meta, session.Columns.Count, session);
        Assert.NotNull(cfg);
        Assert.True(cfg!.InitialRadiusMm > 0);
    }

    [Fact]
    public void MergeFromSession_CopiesContourConfigFromAttachedMeta()
    {
        var session = MakeSession(n: 10, channels: 5);
        session.AttachedMeta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleDiameterMm = 100,
            CylinderContour = CylinderContourConfig.CreateDefault(4, 100)
        };
        var live = new ProjectMeta { Operator = "Lab" };
        CylinderContourExport.MergeFromSession(live, session);
        Assert.NotNull(live.CylinderContour);
        Assert.Equal(ExperimentTypes.CylinderContour, live.ExperimentType);
        Assert.Equal(100, live.SampleDiameterMm);
    }

    [Fact]
    public void StrainGauges_ExcelSmoke_DoesNotRequireContourPng()
    {
        var session = MakeSession(n: 24, channels: 5);
        var meta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.StrainGauges,
            SampleDiameterMm = 90,
            SampleRateHz = 50,
            CylinderContour = CylinderContourConfig.CreateDefault(4, 90),
            ApparentPoissonNu = 0.3,
            ApparentPoissonSummary = "ν_ap≈0.30",
            ElasticRecoverySummary = "ε_perm≈0.1"
        };

        Assert.False(CylinderContourExport.ShouldAttempt(meta, session));
        Assert.Null(CylinderContourExport.EnsureConfig(meta, session.Columns.Count, session));

        using var pack = ReportGraphPack.Build(session, 50, meta);
        Assert.True(string.IsNullOrWhiteSpace(pack.ContourPng));
        Assert.True(string.IsNullOrWhiteSpace(pack.ContourSkipReasonRo));
        Assert.Empty(pack.DeformationCurvePngs);
        Assert.True(pack.ChannelYtPngs.Count > 0, "Tensometrie: Y(t) pe canale rămâne în raport");

        var dir = Path.Combine(Path.GetTempPath(), $"upet_xlsx_strain_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var html = Path.Combine(dir, "raport.html");
            HtmlReportExporter.Export(html, session, meta, session.Stats);
            var htmlText = File.ReadAllText(html);
            Assert.DoesNotContain("Contur cilindru", htmlText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Curbe deformare", htmlText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Mărci tensometrice", htmlText);
            Assert.Contains("Poisson", htmlText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Y(t)", htmlText);

            var pdf = Path.Combine(dir, "ind.pdf");
            IndustrialReportExporter.Export(pdf, session, meta, new IndustrialReportExporter.Options
            {
                SoftwareVersion = "test",
                ContourJpegPath = pack.ContourJpeg
            });
            var ascii = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf));
            Assert.DoesNotContain("Contur cilindru", ascii);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    private static OfflineSession MakeSession(int n, int channels)
    {
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Local);
        var session = new OfflineSession { SourcePath = "unit-excel-contour.csv" };
        for (var c = 0; c < channels; c++)
        {
            session.ChannelNames.Add($"CH{c}");
            session.Columns.Add(new double[n]);
        }
        for (var i = 0; i < n; i++)
        {
            session.Timestamps.Add(t0.AddSeconds(i / 50.0));
            session.Sequences.Add(i);
            for (var c = 0; c < channels; c++)
                session.Columns[c][i] = c < 4 ? 0.01 * i * (c + 1) : i * 0.1;
        }
        session.CursorB = n - 1;
        session.RecomputeStats();
        return session;
    }
}
