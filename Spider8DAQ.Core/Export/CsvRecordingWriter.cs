using System.Globalization;
using System.Text;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Time;

namespace Spider8DAQ.Core.Export;

public sealed class CsvRecordingWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _sync = new();
    private bool _headerWritten;
    private long _rows;
    private DateTime? _t0;
    private DateTime? _tLast;
    private int _rateSetHz;

    public CsvRecordingWriter(string path, bool append = false)
    {
        var full = System.IO.Path.GetFullPath(path);
        var dir = System.IO.Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var fileExists = File.Exists(full) && new FileInfo(full).Length > 0;
        // FileShare.Read so journal/replay can Probe/Load while recording is still open.
        var stream = new FileStream(
            full,
            append ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.Read);
        _writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
        FilePath = full;
        _headerWritten = append && fileExists;
    }

    public string FilePath { get; }
    public long RowsWritten => _rows;
    public DateTime? FirstTimestamp => _t0;
    public DateTime? LastTimestamp => _tLast;

    /// <summary>Configured (UI) sample rate — used for RateHzEffective footer.</summary>
    public void SetRateHzSet(int hz) => _rateSetHz = Math.Max(0, hz);

    public void WriteHeader(IEnumerable<string> channelNames)
    {
        lock (_sync)
        {
            if (_headerWritten) return;
            _writer.Write("Timestamp,Sequence,");
            _writer.Write(SamplingRateInfo.RelativeTimeHeader);
            foreach (var name in channelNames)
            {
                _writer.Write(',');
                _writer.Write(Escape(name));
            }
            _writer.WriteLine();
            _headerWritten = true;
        }
    }

    /// <summary>Optional # comment lines before the header (Operator / Sample / Storage).</summary>
    public void WriteMetaComments(IEnumerable<string> lines)
    {
        lock (_sync)
        {
            if (_headerWritten) return;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var safe = line.Replace('\r', ' ').Replace('\n', ' ');
                _writer.Write("# ");
                _writer.WriteLine(safe);
            }
        }
    }

    public void WriteSample(SampleFrame frame, IReadOnlyList<double> values)
    {
        lock (_sync)
        {
            if (!_headerWritten)
                throw new InvalidOperationException("CSV header not written.");

            var ts = frame.Timestamp;
            if (_t0 is null) _t0 = ts;
            _tLast = ts;
            var tRel = SamplingRateInfo.RelativeSeconds(ts, _t0.Value);

            _writer.Write(AppClock.FormatIso(ts));
            _writer.Write(',');
            _writer.Write(frame.Sequence.ToString(CultureInfo.InvariantCulture));
            _writer.Write(',');
            _writer.Write(tRel.ToString("G17", CultureInfo.InvariantCulture));
            foreach (var v in values)
            {
                _writer.Write(',');
                _writer.Write(double.IsNaN(v) ? "" : v.ToString("G17", CultureInfo.InvariantCulture));
            }
            _writer.WriteLine();
            _rows++;
        }
    }

    /// <summary>Mid-recording marker as a # comment (ignored by CSV replay).</summary>
    public void WriteComment(string text)
    {
        lock (_sync)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var safe = text.Replace('\r', ' ').Replace('\n', ' ');
            _writer.Write("# MARK ");
            _writer.Write(AppClock.FormatIso(AppClock.Now));
            _writer.Write(' ');
            _writer.WriteLine(safe);
        }
    }

    /// <summary>Append effective fs summary before close (for lab / professor checks).</summary>
    public void WriteEffectiveRateFooter()
    {
        lock (_sync)
        {
            if (_rows < 2 || _t0 is null || _tLast is null) return;
            var fsEf = SamplingRateInfo.EstimateEffectiveHz(_t0.Value, _tLast.Value, _rows, _rateSetHz > 0 ? _rateSetHz : 50);
            var dtMean = (_tLast.Value - _t0.Value).TotalSeconds / Math.Max(1, _rows - 1);
            _writer.Write("# ");
            _writer.WriteLine("ExperimentTime=" + SamplingRateInfo.FormatExperimentTime(new[] { _t0.Value, _tLast.Value }));
            foreach (var line in SamplingRateInfo.MetaLines(_rateSetHz, fsEf, dtMean))
            {
                _writer.Write("# ");
                _writer.WriteLine(line);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.FlushAsync();
        await _writer.DisposeAsync();
    }

    private static string Escape(string value)
        => value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
