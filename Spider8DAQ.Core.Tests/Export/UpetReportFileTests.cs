using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class UpetReportFileTests
{
    [Fact]
    public void RoundTrip_PreservesSamplesAndChannels()
    {
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Local);
        var session = new OfflineSession
        {
            ChannelNames = ["CH0 [µm/m]", "CH2 [N]"],
            Timestamps = [t0, t0.AddSeconds(0.03), t0.AddSeconds(0.06)],
            Sequences = [10, 11, 12],
            Columns =
            [
                [0.1, 0.2, 0.3],
                [-4.5, -4.6, -4.7]
            ],
            SourcePath = "demo.csv"
        };
        session.RecomputeStats();

        var path = Path.Combine(Path.GetTempPath(), "upet_report_" + Guid.NewGuid().ToString("N") + ".upet");
        try
        {
            UpetReportFile.Save(path, session, new ProjectMeta
            {
                Operator = "Test",
                SampleId = "S1",
                SampleRateHz = 50
            });

            Assert.True(UpetReportFile.LooksLikeUpetReport(path));
            var loaded = UpetReportFile.Load(path);
            Assert.Equal(3, loaded.Timestamps.Count);
            Assert.Equal(2, loaded.ChannelNames.Count);
            Assert.Equal("CH0 [µm/m]", loaded.ChannelNames[0]);
            Assert.Equal(0.2, loaded.Columns[0][1], 9);
            Assert.Equal(-4.7, loaded.Columns[1][2], 9);
            Assert.Equal(11, loaded.Sequences[1]);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void RoundTrip_EmbedsMontagePhotos()
    {
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Local);
        var session = new OfflineSession
        {
            ChannelNames = ["CH0"],
            Timestamps = [t0, t0.AddSeconds(0.02)],
            Sequences = [1, 2],
            Columns = [[1.0, 2.0]]
        };
        session.RecomputeStats();

        var dir = Path.Combine(Path.GetTempPath(), "upet_montage_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var before = Path.Combine(dir, "before.jpg");
        var after = Path.Combine(dir, "after.jpg");
        // Minimal valid-enough JPEG SOI/EOI markers for round-trip bytes.
        File.WriteAllBytes(before, [0xFF, 0xD8, 0xFF, 0xD9, 0x01, 0x02, 0x03]);
        File.WriteAllBytes(after, [0xFF, 0xD8, 0xFF, 0xD9, 0x0A, 0x0B]);

        var path = Path.Combine(dir, "report.upet");
        try
        {
            UpetReportFile.Save(path, session, new ProjectMeta
            {
                SampleId = "S1",
                MontagePhotoPath = before,
                MontagePhotoAfterPath = after
            });

            // Simulate moving away original files — must still extract from .upet
            File.Delete(before);
            File.Delete(after);

            var loaded = UpetReportFile.Load(path);
            Assert.NotNull(loaded.AttachedMeta);
            Assert.True(File.Exists(loaded.AttachedMeta!.MontagePhotoPath));
            Assert.True(File.Exists(loaded.AttachedMeta.MontagePhotoAfterPath));
            Assert.Equal(7, File.ReadAllBytes(loaded.AttachedMeta.MontagePhotoPath).Length);
            Assert.Equal(6, File.ReadAllBytes(loaded.AttachedMeta.MontagePhotoAfterPath).Length);
            Assert.Contains(loaded.AttachedGraphs, g => g.Role == UpetReportFile.RoleMontageBefore);
            Assert.Contains(loaded.AttachedGraphs, g => g.Role == UpetReportFile.RoleMontageAfter);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Save_SucceedsWhenApparentPoissonIsNaN()
    {
        // Regression: ProjectMeta.ApparentPoissonNu defaults to NaN; System.Text.Json
        // rejects NaN unless NumberHandling allows named literals — broke all .upet exports.
        var t0 = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Local);
        var session = new OfflineSession
        {
            ChannelNames = ["CH0"],
            Timestamps = [t0, t0.AddSeconds(0.02)],
            Sequences = [1, 2],
            Columns = [[1.0, 2.0]]
        };
        session.RecomputeStats();

        var meta = new ProjectMeta
        {
            Operator = "Lab",
            SampleId = "S-NaN",
            ApparentPoissonNu = double.NaN
        };
        Assert.True(double.IsNaN(meta.ApparentPoissonNu));

        var path = Path.Combine(Path.GetTempPath(), "upet_nan_" + Guid.NewGuid().ToString("N") + ".upet");
        try
        {
            UpetReportFile.Save(path, session, meta);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 32);
            var loaded = UpetReportFile.Load(path);
            Assert.Equal(2, loaded.Timestamps.Count);
            Assert.NotNull(loaded.AttachedMeta);
            Assert.True(double.IsNaN(loaded.AttachedMeta!.ApparentPoissonNu));
            Assert.False(string.IsNullOrWhiteSpace(loaded.AttachedMeta.MeasurementFingerprint));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Load_RejectsPlainText()
    {
        var path = Path.Combine(Path.GetTempPath(), "fake_" + Guid.NewGuid().ToString("N") + ".upet");
        try
        {
            File.WriteAllText(path, "Timestamp,Sequence,CH0\n");
            Assert.ThrowsAny<Exception>(() => UpetReportFile.Load(path));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
