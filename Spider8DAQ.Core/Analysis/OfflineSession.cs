using System.Globalization;
using System.Text;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Replay;

namespace Spider8DAQ.Core.Analysis;

public sealed class OfflineSession
{
    public string SourcePath { get; set; } = "";
    public List<string> ChannelNames { get; set; } = new();
    public List<DateTime> Timestamps { get; set; } = new();
    public List<long> Sequences { get; set; } = new();
    public List<double[]> Columns { get; set; } = new();
    public List<ChannelStats> Stats { get; set; } = new();
    public int CursorA { get; set; }
    public int CursorB { get; set; }
    /// <summary>PNG graphs embedded in .upet (extracted on load): role + absolute path.</summary>
    public List<(string Role, string Name, string Path)> AttachedGraphs { get; set; } = new();
    /// <summary>Project meta from .upet envelope (includes montage paths remapped to extracted files).</summary>
    public ProjectMeta? AttachedMeta { get; set; }

    public static OfflineSession FromCsv(string path)
    {
        var replay = CsvReplaySession.Load(path);
        var session = new OfflineSession { SourcePath = path };
        session.ChannelNames = replay.Headers.ToList();
        session.Timestamps = replay.Frames.Select(f => f.Timestamp).ToList();
        session.Sequences = replay.Frames.Select(f => f.Sequence).ToList();

        var cols = new List<double[]>();
        for (var c = 0; c < session.ChannelNames.Count; c++)
        {
            var col = new double[replay.Values.Count];
            for (var r = 0; r < replay.Values.Count; r++)
                col[r] = c < replay.Values[r].Length ? replay.Values[r][c] : double.NaN;
            cols.Add(col);
        }

        session.Columns = cols;
        session.CursorB = Math.Max(0, session.Timestamps.Count - 1);
        session.RecomputeStats();
        session.AttachedMeta = TryReadMetaFromCsvComments(path);
        return session;
    }

    /// <summary>Best-effort ContourConfig / ExperimentType / Ø from CSV # comment header.</summary>
    public static ProjectMeta? TryReadMetaFromCsvComments(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            var meta = new ProjectMeta();
            var any = false;
            foreach (var line in File.ReadLines(path))
            {
                var t = line.TrimStart();
                if (!t.StartsWith('#')) continue;
                t = t[1..].TrimStart();
                var eq = t.IndexOf('=');
                if (eq <= 0) continue;
                var key = t[..eq].Trim();
                var val = t[(eq + 1)..].Trim();
                switch (key)
                {
                    case "Operator":
                        meta.Operator = val; any = true; break;
                    case "Sample":
                        meta.SampleId = val; any = true; break;
                    case "Backend":
                        meta.Backend = val; any = true; break;
                    case "Comment":
                        meta.Comment = val; any = true; break;
                    case "ExperimentType":
                        meta.ExperimentType = val; any = true; break;
                    case "SampleDiameterMm" when double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var d):
                        meta.SampleDiameterMm = d; any = true; break;
                    case "RateHzSet" when int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hz):
                        meta.SampleRateHz = hz; any = true; break;
                    case "ContourConfig":
                        var cfg = CylinderContourExport.TryParseCsvCommentValue(val);
                        if (cfg is not null)
                        {
                            meta.CylinderContour = cfg;
                            any = true;
                        }
                        break;
                    default:
                        if (Spider8DAQ.Core.Specimens.SpecimenIdentification.TryApplyCsvKey(meta, key, val))
                            any = true;
                        break;
                }
            }
            return any ? meta : null;
        }
        catch
        {
            return null;
        }
    }

    public void RecomputeStats()
    {
        Stats = ChannelNames
            .Select((n, i) => SignalAnalysis.ComputeStats(n, Columns[i]))
            .ToList();
    }

    public double DeltaTSeconds()
    {
        if (Timestamps.Count == 0) return double.NaN;
        var a = Math.Clamp(CursorA, 0, Timestamps.Count - 1);
        var b = Math.Clamp(CursorB, 0, Timestamps.Count - 1);
        return (Timestamps[b] - Timestamps[a]).TotalSeconds;
    }

    public double DeltaY(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= Columns.Count || Columns[channelIndex].Length == 0)
            return double.NaN;
        var a = Math.Clamp(CursorA, 0, Columns[channelIndex].Length - 1);
        var b = Math.Clamp(CursorB, 0, Columns[channelIndex].Length - 1);
        return Columns[channelIndex][b] - Columns[channelIndex][a];
    }

    public OfflineSession ExportRegion(int start, int endExclusive)
    {
        var cut = SignalAnalysis.Cut(Columns, start, endExclusive);
        var s = new OfflineSession
        {
            SourcePath = SourcePath + $"#[{cut.Start},{cut.End})",
            ChannelNames = ChannelNames.ToList(),
            Timestamps = Timestamps.Skip(cut.Start).Take(cut.End - cut.Start).ToList(),
            Sequences = Sequences.Skip(cut.Start).Take(cut.End - cut.Start).ToList(),
            Columns = cut.Values.ToList(),
            CursorA = 0,
            CursorB = Math.Max(0, cut.End - cut.Start - 1)
        };
        s.RecomputeStats();
        return s;
    }

    public void SmoothChannel(int channelIndex, int window)
    {
        if (channelIndex < 0 || channelIndex >= Columns.Count) return;
        Columns[channelIndex] = SignalAnalysis.MovingAverage(Columns[channelIndex], window);
        RecomputeStats();
    }

    public void SmoothAll(int window)
    {
        for (var i = 0; i < Columns.Count; i++)
            Columns[i] = SignalAnalysis.MovingAverage(Columns[i], window);
        RecomputeStats();
    }

    public void Cut(int start, int endExclusive)
    {
        var cut = SignalAnalysis.Cut(Columns, start, endExclusive);
        Columns = cut.Values.ToList();
        Timestamps = Timestamps.Skip(cut.Start).Take(cut.End - cut.Start).ToList();
        Sequences = Sequences.Skip(cut.Start).Take(cut.End - cut.Start).ToList();
        CursorA = 0;
        CursorB = Math.Max(0, Timestamps.Count - 1);
        RecomputeStats();
    }

    public List<int> Peaks(int channelIndex, double prominence = 0)
    {
        if (channelIndex < 0 || channelIndex >= Columns.Count) return new List<int>();
        return SignalAnalysis.FindPeaks(Columns[channelIndex], prominence);
    }

    public void SaveCsv(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var w = new StreamWriter(path, false, Encoding.UTF8);
        w.Write("Timestamp,Sequence,");
        w.Write(SamplingRateInfo.RelativeTimeHeader);
        foreach (var n in ChannelNames)
        {
            w.Write(',');
            w.Write(n);
        }
        w.WriteLine();
        var tRel = SamplingRateInfo.RelativeSeconds(Timestamps);
        for (var i = 0; i < Timestamps.Count; i++)
        {
            w.Write(Timestamps[i].ToString("O", CultureInfo.InvariantCulture));
            w.Write(',');
            w.Write(Sequences[i].ToString(CultureInfo.InvariantCulture));
            w.Write(',');
            w.Write((i < tRel.Length ? tRel[i] : 0).ToString("G17", CultureInfo.InvariantCulture));
            for (var c = 0; c < Columns.Count; c++)
            {
                w.Write(',');
                var v = Columns[c][i];
                w.Write(double.IsNaN(v) ? "" : v.ToString("G17", CultureInfo.InvariantCulture));
            }
            w.WriteLine();
        }
    }

    public void ExportTxt(string path) =>
        TxtExporter.Export(path, ChannelNames, Timestamps, Sequences, Columns);

    public void ExportMat(string path) =>
        MatExporter.Export(path, ChannelNames, Columns);

    public void IntegrateChannel(int channelIndex)
    {
        IntegrateChannel(channelIndex, regionOnly: false);
    }

    /// <summary>
    /// Integrate full channel, or only CursorA–CursorB when <paramref name="regionOnly"/> is true
    /// (outside the region the cumulative integral is held flat).
    /// </summary>
    public void IntegrateChannel(int channelIndex, bool regionOnly)
    {
        if (channelIndex < 0 || channelIndex >= Columns.Count) return;
        var full = SignalAnalysis.Integrate(Columns[channelIndex], Timestamps);
        if (!regionOnly || Timestamps.Count == 0)
        {
            Columns[channelIndex] = full;
        }
        else
        {
            var a = Math.Clamp(Math.Min(CursorA, CursorB), 0, full.Length - 1);
            var b = Math.Clamp(Math.Max(CursorA, CursorB), a, full.Length - 1);
            var result = new double[full.Length];
            var baseVal = a > 0 ? full[a] : 0;
            for (var i = 0; i < full.Length; i++)
            {
                if (i < a) result[i] = 0;
                else if (i <= b) result[i] = full[i] - baseVal;
                else result[i] = full[b] - baseVal;
            }
            Columns[channelIndex] = result;
        }
        ChannelNames[channelIndex] = ChannelNames[channelIndex] + (regionOnly ? "\u222B[A-B]" : "\u222B");
        RecomputeStats();
    }

    public void DifferentiateChannel(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= Columns.Count) return;
        Columns[channelIndex] = SignalAnalysis.Differentiate(Columns[channelIndex], Timestamps);
        ChannelNames[channelIndex] = ChannelNames[channelIndex] + "'";
        RecomputeStats();
    }

    public void ScaleOffsetChannel(int channelIndex, double scale, double offset)
    {
        if (channelIndex < 0 || channelIndex >= Columns.Count) return;
        Columns[channelIndex] = SignalAnalysis.ScaleOffset(Columns[channelIndex], scale, offset);
        RecomputeStats();
    }

    public void RemoveOutliers(int channelIndex, double sigmaK = 3)
    {
        if (channelIndex < 0 || channelIndex >= Columns.Count) return;
        Columns[channelIndex] = SignalAnalysis.RemoveOutliers(Columns[channelIndex], sigmaK);
        RecomputeStats();
    }

    public void LowPassChannel(int channelIndex, double alpha = 0.2)
    {
        if (channelIndex < 0 || channelIndex >= Columns.Count) return;
        Columns[channelIndex] = SignalAnalysis.LowPass1(Columns[channelIndex], alpha);
        RecomputeStats();
    }

    public LinearFitResult FitXy(int xChannel, int yChannel)
    {
        if (xChannel < 0 || yChannel < 0 || xChannel >= Columns.Count || yChannel >= Columns.Count)
            return new LinearFitResult();
        return SignalAnalysis.LinearFit(Columns[xChannel], Columns[yChannel]);
    }

    public SignalAnalysis.PolyFitResult FitPolyXy(int xChannel, int yChannel, int degree = 2)
    {
        if (xChannel < 0 || yChannel < 0 || xChannel >= Columns.Count || yChannel >= Columns.Count)
            return new SignalAnalysis.PolyFitResult();
        return SignalAnalysis.PolynomialFit(Columns[xChannel], Columns[yChannel], degree);
    }

    public void AddDerivedChannel(string name, double[] values)
    {
        if (values.Length != Timestamps.Count) return;
        ChannelNames.Add(name);
        Columns.Add(values);
        RecomputeStats();
    }
}
