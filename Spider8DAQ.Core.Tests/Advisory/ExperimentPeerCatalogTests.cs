using System.Globalization;
using System.Text;
using Spider8DAQ.Core.Advisory;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Projects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Advisory;

public class ExperimentPeerCatalogTests
{
    [Fact]
    public void AreSameKind_never_mixes_contour_and_strain()
    {
        Assert.True(ExperimentTypes.AreSameKind(
            ExperimentTypes.CylinderContour, "Compresiune cilindru - contur"));
        Assert.True(ExperimentTypes.AreSameKind(
            ExperimentTypes.StrainGauges, "tensometrie"));
        Assert.False(ExperimentTypes.AreSameKind(
            ExperimentTypes.CylinderContour, ExperimentTypes.StrainGauges));
        Assert.False(ExperimentTypes.AreSameKind("Mixt / multi-senzor", ExperimentTypes.StrainGauges));
    }

    [Fact]
    public void FindPeers_only_same_experiment_type()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upet-peers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var current = WriteCsv(dir, "current_strain.csv", ExperimentTypes.StrainGauges, 120, 210);
            WriteCsv(dir, "old_strain.csv", ExperimentTypes.StrainGauges, 80, 190);
            WriteCsv(dir, "contour.csv", ExperimentTypes.CylinderContour, 50, 0.04);
            File.SetLastWriteTime(Path.Combine(dir, "old_strain.csv"), DateTime.Now.AddDays(-2));

            var peers = ExperimentPeerCatalog.FindPeers(
                dir, ExperimentTypes.StrainGauges, current, maxPeers: 3, computeMetrics: true);

            Assert.Single(peers);
            Assert.True(ExperimentTypes.IsStrainGauges(peers[0].ExperimentType));
            Assert.Equal("old_strain.csv", peers[0].FileName);
            Assert.NotNull(peers[0].EpsMax);
            Assert.True(peers[0].EpsMax > 100);
            Assert.Null(peers[0].OvalityMm);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void Compare_contour_ovalitate_wording()
    {
        var current = new ExperimentSummary
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            OvalityMm = 0.07,
            BarrelingIndex = 0.12,
            SampleCount = 2000,
            ModifiedLocal = DateTime.Now
        };
        var peer = new ExperimentSummary
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            OvalityMm = 0.05,
            BarrelingIndex = 0.12,
            SampleCount = 1800,
            ModifiedLocal = new DateTime(2026, 8, 10),
            FilePath = "old.csv",
            FileName = "old.csv"
        };
        var item = ExperimentPeerCatalog.Compare(current, new[] { peer }, scanComplete: true);
        Assert.NotNull(item);
        Assert.Equal("peer-compare", item!.Id);
        Assert.Contains("ovalitate 0.07 vs 0.05 mm (mai mare)", item.Suggestion);
        Assert.Contains("bombare", item.Suggestion);
        Assert.Contains("similar", item.Suggestion);
        Assert.DoesNotContain("ε_max", item.Suggestion);
    }

    [Fact]
    public void Compare_strain_eps_similar()
    {
        var current = new ExperimentSummary
        {
            ExperimentType = ExperimentTypes.StrainGauges,
            EpsMax = 412,
            SampleCount = 500,
            ModifiedLocal = DateTime.Now
        };
        var peer = new ExperimentSummary
        {
            ExperimentType = ExperimentTypes.StrainGauges,
            EpsMax = 400,
            SampleCount = 480,
            ModifiedLocal = new DateTime(2026, 8, 10),
            FileName = "t.csv"
        };
        var item = ExperimentPeerCatalog.Compare(current, new[] { peer }, scanComplete: true);
        Assert.NotNull(item);
        Assert.Contains("ε_max", item!.Suggestion);
        Assert.Contains("similar", item.Suggestion);
        Assert.Contains("2026-08-10", item.Suggestion);
        Assert.DoesNotContain("ovalitate", item.Suggestion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bombare", item.Suggestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_no_peers_once()
    {
        var current = new ExperimentSummary
        {
            ExperimentType = ExperimentTypes.StrainGauges,
            SampleCount = 10,
            EpsMax = 50
        };
        var first = ExperimentPeerCatalog.Compare(current, Array.Empty<ExperimentSummary>(), scanComplete: true);
        Assert.NotNull(first);
        Assert.Equal("peer-none", first!.Id);
        Assert.Contains("Tensometrie", first.Suggestion, StringComparison.OrdinalIgnoreCase);

        var path = Path.Combine(Path.GetTempPath(), "adv-peer-none-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var mem = new AdvisorMemory(path);
            mem.RecordShown("peer-none", "scan");
            var again = ExperimentPeerCatalog.Compare(
                current, Array.Empty<ExperimentSummary>(), scanComplete: true, memory: mem, userAsked: false);
            Assert.Null(again);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SummarizeSession_strain_eps_max()
    {
        var session = new OfflineSession { SourcePath = "mem.csv" };
        session.ChannelNames.Add("SG1 [µm/m]");
        session.Columns.Add(new[] { 10.0, 80.0, 400.0, 120.0 });
        session.Timestamps.AddRange(new[]
        {
            new DateTime(2026, 8, 10, 10, 0, 0),
            new DateTime(2026, 8, 10, 10, 0, 1),
            new DateTime(2026, 8, 10, 10, 0, 2),
            new DateTime(2026, 8, 10, 10, 0, 3)
        });
        session.RecomputeStats();
        var s = ExperimentPeerCatalog.SummarizeSession(session, ExperimentTypes.StrainGauges);
        Assert.Equal(400, s.EpsMax);
        Assert.Null(s.OvalityMm);
    }

    private static string WriteCsv(string dir, string name, string experimentType, int n, double peak)
    {
        var path = Path.Combine(dir, name);
        var sb = new StringBuilder();
        sb.AppendLine("# ExperimentType=" + experimentType);
        var strain = ExperimentTypes.IsStrainGauges(experimentType);
        sb.AppendLine(strain
            ? "Timestamp,Sequence,CH0 [µm/m]"
            : "Timestamp,Sequence,CH0 [N]");
        var t0 = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 8; i++)
        {
            var y = i == 4 ? peak : peak * 0.2 * (i + 1) / 8.0;
            sb.Append(t0.AddMilliseconds(i * 20).ToString("o", CultureInfo.InvariantCulture))
                .Append(',').Append(i).Append(',')
                .Append(y.ToString("G9", CultureInfo.InvariantCulture))
                .AppendLine();
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }
}
