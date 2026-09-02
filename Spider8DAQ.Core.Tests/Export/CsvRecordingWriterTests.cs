using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Replay;
using Xunit;

namespace Spider8DAQ.Core.Tests.Export;

public class CsvRecordingWriterTests
{
    [Fact]
    public async Task WriteComment_MidRecording_IsSkippedByReplayFilter()
    {
        var path = Path.Combine(Path.GetTempPath(), "upet_mark_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            await using (var w = new CsvRecordingWriter(path))
            {
                w.WriteMetaComments(new[] { "Operator=test" });
                w.WriteHeader(new[] { "CH0" });
                w.WriteComment("peak-load");
            }

            var lines = File.ReadAllLines(path);
            Assert.Contains(lines, l => l.StartsWith("# MARK ", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.StartsWith("# Operator", StringComparison.Ordinal));
            var data = lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#')).ToList();
            Assert.Single(data); // header only
            Assert.StartsWith("Timestamp,Sequence,t_s,CH0", data[0]);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task WriteSample_IncludesRelativeTimeAndEffectiveFooter()
    {
        var path = Path.Combine(Path.GetTempPath(), "upet_ts_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            var t0 = new DateTime(2026, 8, 10, 13, 0, 0, DateTimeKind.Utc);
            await using (var w = new CsvRecordingWriter(path))
            {
                w.SetRateHzSet(50);
                w.WriteMetaComments(new[] { "RateHzSet=50" });
                w.WriteHeader(new[] { "CH0" });
                for (var i = 0; i < 5; i++)
                {
                    w.WriteSample(
                        new SampleFrame
                        {
                            Timestamp = t0.AddMilliseconds(i * 32),
                            Sequence = i,
                            Values = Array.Empty<double>()
                        },
                        new[] { 1.0 * i });
                }
                w.WriteEffectiveRateFooter();
            }

            var text = File.ReadAllText(path);
            Assert.Contains("t_s", text, StringComparison.Ordinal);
            Assert.Contains("RateHzEffective=", text, StringComparison.Ordinal);
            Assert.Contains("DtMeanSec=", text, StringComparison.Ordinal);

            var replay = CsvReplaySession.Load(path);
            Assert.Equal(new[] { "CH0" }, replay.Headers);
            Assert.Equal(5, replay.Frames.Count);
            Assert.Equal(0.0, replay.Values[0][0], 5);
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
