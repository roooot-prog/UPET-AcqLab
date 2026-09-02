using Spider8DAQ.Core.DataViewer;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Replay;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class CsvSharedIOTests
{
    [Fact]
    public async Task ProbeAndLoad_WhileWriterOpen_Succeeds()
    {
        var path = Path.Combine(Path.GetTempPath(), "upet_share_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            await using var w = new CsvRecordingWriter(path);
            w.WriteMetaComments(new[] { "UPET AcqLab test", "Storage=Full" });
            w.WriteHeader(new[] { "HBM P15RVA/1/200B [bar]", "CH2 [N]" });
            var t0 = DateTime.Parse("2026-08-05T06:53:25.8223975Z", null,
                System.Globalization.DateTimeStyles.RoundtripKind);
            for (var i = 0; i < 5; i++)
            {
                w.WriteSample(
                    new SampleFrame { Timestamp = t0.AddMilliseconds(i * 20), Sequence = 100 + i },
                    new[] { -0.008, 11.4 + i });
            }

            // Still holding write handle — old File.ReadLines would throw.
            var entry = RecordingIndex.Probe(path);
            Assert.Equal(5, entry.SampleCount);
            Assert.Equal(2, entry.ChannelCount);
            Assert.Contains("HBM P15RVA", entry.ChannelSummary, StringComparison.Ordinal);
            Assert.True(entry.Duration > TimeSpan.Zero);

            var session = CsvReplaySession.Load(path);
            Assert.Equal(5, session.Frames.Count);
            Assert.Equal(2, session.Headers.Count);
            Assert.False(double.IsNaN(session.Values[0][0]));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void IsCommentOrMeta_SkipsHashAndBom()
    {
        Assert.True(CsvSharedIO.IsCommentOrMeta("# MARK note"));
        Assert.True(CsvSharedIO.IsCommentOrMeta("\uFEFF# UPET"));
        Assert.False(CsvSharedIO.IsCommentOrMeta("Timestamp,Sequence,CH0"));
    }
}
