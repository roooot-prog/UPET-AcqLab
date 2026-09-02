using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class CylinderContourExportTests
{
    [Theory]
    [InlineData("Compresiune cilindru – contur")]
    [InlineData("Compresiune cilindru - contur")]
    [InlineData("compresiune cilindru — contur")]
    [InlineData("  Compresiune cilindru – contur  ")]
    public void IsCylinderContour_AcceptsDashVariants(string label)
        => Assert.True(ExperimentTypes.IsCylinderContour(label));

    [Fact]
    public void ShouldAttempt_WhenConfigPresent_EvenIfTypeBlank()
    {
        var meta = new ProjectMeta
        {
            ExperimentType = "",
            CylinderContour = CylinderContourConfig.CreateDefault(4, 100),
            SampleDiameterMm = 100
        };
        Assert.True(CylinderContourExport.ShouldAttempt(meta));
    }

    [Fact]
    public void ShouldAttempt_False_ForStrainGauges_EvenWithLeftoverContourConfig()
    {
        var session = MakeSession(n: 20, channels: 5);
        var meta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.StrainGauges,
            SampleDiameterMm = 90,
            CylinderContour = CylinderContourConfig.CreateDefault(4, 90)
        };
        Assert.True(ExperimentTypes.IsStrainGauges(meta.ExperimentType));
        Assert.True(ExperimentTypes.IsExplicitNonContour(meta.ExperimentType));
        Assert.False(CylinderContourExport.CanInferFromDiameterAndChannels(meta, session));
        Assert.False(CylinderContourExport.ShouldAttempt(meta, session));
        Assert.Null(CylinderContourExport.EnsureConfig(meta, session.Columns.Count, session));
        Assert.Equal(ExperimentTypes.StrainGauges, meta.ExperimentType);
        var extra = CylinderContourExport.BuildRecordingMetaExtraLines(meta).ToList();
        Assert.DoesNotContain(extra, l => l.StartsWith("ContourConfig=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Mărci tensometrice")]
    [InlineData("tensometrie")]
    [InlineData("Tensometrie / deformații")]
    public void IsStrainGauges_AcceptsLabels(string label)
        => Assert.True(ExperimentTypes.IsStrainGauges(label));

    [Fact]
    public void MergeFromSession_DoesNotCopyContour_WhenLiveTypeIsStrainGauges()
    {
        var session = MakeSession(n: 10, channels: 5);
        session.AttachedMeta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleDiameterMm = 100,
            CylinderContour = CylinderContourConfig.CreateDefault(4, 100)
        };
        var live = new ProjectMeta { ExperimentType = ExperimentTypes.StrainGauges };
        CylinderContourExport.MergeFromSession(live, session);
        Assert.Equal(ExperimentTypes.StrainGauges, live.ExperimentType);
        Assert.Null(live.CylinderContour);
        Assert.False(CylinderContourExport.ShouldAttempt(live, session));
    }

    [Fact]
    public void EnsureConfig_FillsDefault_WhenTypeIsContour()
    {
        var meta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleDiameterMm = 80
        };
        var cfg = CylinderContourExport.EnsureConfig(meta, sessionChannelCount: 8);
        Assert.NotNull(cfg);
        Assert.True(cfg!.InitialRadiusMm > 0);
        Assert.Equal(40, cfg.InitialRadiusMm, 3);
    }

    [Fact]
    public void TryCompute_ProducesValidPlot_WithDiameterAndChannels()
    {
        var session = MakeSession(n: 50, channels: 5);
        var meta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleDiameterMm = 100,
            CylinderContour = new CylinderContourConfig
            {
                SensorCount = 4,
                SensorChannelIndices = new List<int> { 0, 1, 2, 3 },
                SensorAnglesDeg = CylinderContourConfig.DefaultAnglesDeg(4).ToList(),
                StrokeChannelIndex = 4,
                ForceChannelIndex = null,
                InitialRadiusMm = 0
            }
        };

        var r = CylinderContourExport.TryCompute(session, meta);
        Assert.NotNull(r);
        Assert.True(r!.IsValid, r.Error);
        Assert.Equal(50, r.InitialRadiusMm, 3);

        var dir = Path.Combine(Path.GetTempPath(), $"upet_contour_exp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var jpg = Path.Combine(dir, "c.jpg");
        try
        {
            var saved = CylinderContourPlotRenderer.TrySaveJpeg(session, meta, jpg);
            Assert.NotNull(saved);
            Assert.True(File.Exists(jpg));
            Assert.True(new FileInfo(jpg).Length > 200);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void IndustrialPdf_EmbedsDedicatedContourPage()
    {
        var session = MakeSession(n: 40, channels: 5);
        var meta = new ProjectMeta
        {
            Operator = "Lab",
            SampleId = "CYL-01",
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleDiameterMm = 100,
            SampleRateHz = 50,
            CylinderContour = CylinderContourConfig.CreateDefault(4, 100)
        };
        meta.CylinderContour!.StrokeChannelIndex = 4;
        meta.CylinderContour.SensorChannelIndices = new List<int> { 0, 1, 2, 3 };

        var dir = Path.Combine(Path.GetTempPath(), $"upet_ind_contour_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var jpg = Path.Combine(dir, "contour.jpg");
        var pdf = Path.Combine(dir, "ind.pdf");
        try
        {
            Assert.NotNull(CylinderContourPlotRenderer.TrySaveJpeg(session, meta, jpg));
            IndustrialReportExporter.Export(pdf, session, meta, new IndustrialReportExporter.Options
            {
                SoftwareVersion = "3.3.66",
                ContourJpegPath = jpg,
                CursorA = 0,
                CursorB = 39
            });
            Assert.True(File.Exists(pdf));
            var ascii = System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(pdf));
            Assert.Contains("Contur cilindru", ascii);
            Assert.Contains("/Count", ascii);
            // At least 2 pages (cover + contour); often 3 with quality page
            Assert.True(ascii.Contains("/Count 2") || ascii.Contains("/Count 3"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ContourConfig_RoundTripsCsvComment()
    {
        var cfg = CylinderContourConfig.CreateDefault(4, 90);
        var line = CylinderContourExport.ToCsvCommentLine(cfg);
        Assert.NotNull(line);
        Assert.StartsWith("ContourConfig=", line);
        var json = line!["ContourConfig=".Length..];
        var back = CylinderContourExport.TryParseCsvCommentValue(json);
        Assert.NotNull(back);
        Assert.Equal(4, back!.SensorCount);
        Assert.Equal(45, back.InitialRadiusMm, 3);
    }

    [Fact]
    public void CreateDefault_ClampsToAvailableChannels()
    {
        var cfg = CylinderContourConfig.CreateDefault(4, 100, availableChannelCount: 5);
        Assert.Equal(new[] { 0, 1, 2, 3 }, cfg.SensorChannelIndices);
        Assert.Equal(4, cfg.StrokeChannelIndex);
        Assert.False(cfg.HasOutOfRangeChannels(5));
    }

    [Fact]
    public void EnsureConfig_AutoMaps_WhenIndicesOutOfRange()
    {
        var session = MakeSession(n: 20, channels: 5);
        var meta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleDiameterMm = 100,
            CylinderContour = new CylinderContourConfig
            {
                SensorCount = 4,
                // Hardware-style indices that exceed packed Rec column count
                SensorChannelIndices = new List<int> { 1, 2, 3, 4 },
                SensorAnglesDeg = CylinderContourConfig.DefaultAnglesDeg(4).ToList(),
                StrokeChannelIndex = 5,
                InitialRadiusMm = 50
            }
        };

        var cfg = CylinderContourExport.EnsureConfig(meta, session.Columns.Count, session);
        Assert.NotNull(cfg);
        Assert.False(cfg!.HasOutOfRangeChannels(session.Columns.Count));
        var r = CylinderContourExport.TryCompute(session, meta);
        Assert.NotNull(r);
        Assert.True(r!.IsValid, r.Error);
    }

    [Fact]
    public void TryBind_ResolvesHardwareName_ToPackedColumn()
    {
        var names = new[] { "CH1 [mm]", "CH2 [mm]", "CH3 [mm]", "CH4 [mm]", "CH5 [mm]" };
        var cfg = new CylinderContourConfig
        {
            SensorCount = 4,
            SensorChannelIndices = new List<int> { 1, 2, 3, 4 },
            SensorAnglesDeg = CylinderContourConfig.DefaultAnglesDeg(4).ToList(),
            StrokeChannelIndex = 5
        };
        Assert.True(cfg.TryBindToSession(names, names.Length, out _));
        Assert.Equal(new[] { 0, 1, 2, 3 }, cfg.SensorChannelIndices);
        Assert.Equal(4, cfg.StrokeChannelIndex);
    }

    [Fact]
    public void TryRemapHardwareToPacked_MapsRecOrder()
    {
        var cfg = CylinderContourConfig.CreateDefault(4, 100);
        cfg.SensorChannelIndices = new List<int> { 2, 3, 4, 5 };
        cfg.StrokeChannelIndex = 6;
        var hwRec = new[] { 2, 3, 4, 5, 6 };
        Assert.True(cfg.TryRemapHardwareToPacked(hwRec, out _));
        Assert.Equal(new[] { 0, 1, 2, 3 }, cfg.SensorChannelIndices);
        Assert.Equal(4, cfg.StrokeChannelIndex);
    }

    [Fact]
    public void InvalidChannel_ErrorMentionsRemapInStartExp()
    {
        var msg = CylinderContourConfig.FormatInvalidSensorRo(2, 1, 1);
        Assert.Contains("CH1 nu există în înregistrare", msg);
        Assert.Contains("canale 0", msg);
        Assert.Contains("Remapați S2", msg);
    }

    private static OfflineSession MakeSession(int n, int channels)
    {
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Local);
        var session = new OfflineSession { SourcePath = "unit-contour.csv" };
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
