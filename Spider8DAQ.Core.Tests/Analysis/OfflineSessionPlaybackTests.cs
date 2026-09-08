using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Xunit;

namespace Spider8DAQ.Core.Tests.Analysis;

public class OfflineSessionPlaybackTests
{
    [Fact]
    public void IndexAtTimeSeconds_MatchesWallClock()
    {
        var session = new OfflineSession();
        var t0 = new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 11; i++)
            session.Timestamps.Add(t0.AddSeconds(i * 0.2));

        Assert.Equal(0, session.IndexAtTimeSeconds(0));
        Assert.Equal(5, session.IndexAtTimeSeconds(1.0));
        Assert.Equal(10, session.IndexAtTimeSeconds(99));
        Assert.Equal(2.0, session.DurationSeconds(), 6);
        Assert.Equal(1.0, session.TimeSecondsAt(5), 6);
    }

    [Fact]
    public void Find_PrefersSiblingVideoMp4()
    {
        var dir = Path.Combine(Path.GetTempPath(), "upet_vidloc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var csv = Path.Combine(dir, "proba_20260908_090000.csv");
        var mp4 = Path.Combine(dir, "proba_20260908_090000_video.mp4");
        File.WriteAllText(csv, "t\n0");
        File.WriteAllBytes(mp4, [0, 0, 0, 1]);
        try
        {
            var found = ExperimentVideoLocator.Find(csv, new ProjectMeta());
            Assert.Equal(mp4, found);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }
}
