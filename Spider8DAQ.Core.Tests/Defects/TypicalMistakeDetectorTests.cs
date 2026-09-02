using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Defects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Defects;

public class TypicalMistakeDetectorTests
{
    [Fact]
    public void AnalyzeLive_DetectsSaturation()
    {
        var findings = TypicalMistakeDetector.AnalyzeLive(
        [
            new ChannelSnapshot
            {
                Index = 0,
                Name = "Forță",
                Unit = "N",
                Enabled = true,
                RecordEnabled = true,
                Capacity = 100,
                Value = 99.5,
                Scale = 1,
                RecentSamples = Enumerable.Repeat(99.5, 30).ToArray()
            }
        ], TimeSpan.FromMinutes(5));

        Assert.Contains(findings, f => f.DefectId == "saturare");
    }

    [Fact]
    public void AnalyzeLive_DetectsFlatSignalAwayFromZero()
    {
        var flat = Enumerable.Repeat(12.5, 60).Select(v => v + 0.0001).ToArray();
        var findings = TypicalMistakeDetector.AnalyzeLive(
        [
            new ChannelSnapshot
            {
                Index = 0,
                Name = "CH0",
                Unit = "N",
                Enabled = true,
                RecordEnabled = true,
                Capacity = 5000,
                Value = 12.5,
                Scale = 1,
                RecentSamples = flat
            }
        ],
            timeSinceZero: TimeSpan.FromSeconds(30),
            timeSinceStart: TimeSpan.FromSeconds(30));

        Assert.Contains(findings, f => f.DefectId == "semnal-plat");
        Assert.Contains(findings, f => f.Message.Contains("Semnal plat pe CH0", StringComparison.Ordinal));
    }

    [Fact]
    public void AnalyzeLive_IgnoresFlatDuringWarmup()
    {
        var flat = Enumerable.Repeat(12.5, 60).ToArray();
        var findings = TypicalMistakeDetector.AnalyzeLive(
        [
            new ChannelSnapshot
            {
                Index = 0,
                Name = "CH0",
                Unit = "N",
                Enabled = true,
                RecordEnabled = true,
                Capacity = 5000,
                Value = 12.5,
                Scale = 1,
                RecentSamples = flat
            }
        ],
            timeSinceZero: TimeSpan.FromSeconds(1),
            timeSinceStart: TimeSpan.FromSeconds(1));

        Assert.DoesNotContain(findings, f => f.DefectId == "semnal-plat");
    }

    [Fact]
    public void AnalyzeLive_IgnoresIdleZeroWhenAllChannelsQuiet()
    {
        var zeroFlat = Enumerable.Repeat(0.0, 60).ToArray();
        var findings = TypicalMistakeDetector.AnalyzeLive(
        [
            new ChannelSnapshot
            {
                Index = 0,
                Name = "CH0",
                Unit = "N",
                Enabled = true,
                RecordEnabled = true,
                Capacity = 5000,
                Value = 0,
                Scale = 1,
                RecentSamples = zeroFlat
            }
        ],
            timeSinceZero: TimeSpan.FromSeconds(30),
            timeSinceStart: TimeSpan.FromSeconds(30));

        Assert.DoesNotContain(findings, f => f.DefectId == "semnal-plat");
    }

    [Fact]
    public void AnalyzeOffline_DetectsPolarityAndFlatChannel()
    {
        var n = 80;
        var force = Enumerable.Range(0, n).Select(i => -i * 2.0).ToArray();
        var dead = Enumerable.Repeat(0.0, n).ToArray();
        var session = new OfflineSession
        {
            ChannelNames = ["CH0 [N]", "CH1 [N]"],
            Timestamps = Enumerable.Range(0, n).Select(i => DateTime.Now.AddSeconds(i * 0.02)).ToList(),
            Sequences = Enumerable.Range(0, n).Select(i => (long)i).ToList(),
            Columns = [force, dead]
        };

        var findings = TypicalMistakeDetector.AnalyzeOffline(session, []);
        Assert.Contains(findings, f => f.DefectId == "polaritate-inversa");
        Assert.Contains(findings, f => f.DefectId is "semnal-plat" or "canal-gresit");
    }

    [Fact]
    public void Catalog_BuildsExampleSignals()
    {
        foreach (var d in LabDefectCatalog.All)
        {
            var y = LabDefectCatalog.BuildExampleSignal(d.Id, 80);
            Assert.Equal(80, y.Length);
            Assert.Contains(y, v => !double.IsNaN(v));
        }
    }
}
