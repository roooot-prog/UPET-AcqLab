using System.Globalization;
using Spider8DAQ.Core.Time;

namespace Spider8DAQ.Core.Analysis;

/// <summary>
/// Sampling-rate helpers for lab exports: set vs effective fs from wall-clock timestamps.
/// </summary>
public static class SamplingRateInfo
{
    public const string RelativeTimeHeader = "t_s";
    public const string RelativeTimeHeaderExcel = "t [s]";

    public static bool IsRelativeTimeHeader(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        return n.Equals("t_s", StringComparison.OrdinalIgnoreCase)
               || n.Equals("t[s]", StringComparison.OrdinalIgnoreCase)
               || n.Equals("t [s]", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Effective sample rate from first→last timestamp span: (N−1) / duration.
    /// </summary>
    public static double EstimateEffectiveHz(IReadOnlyList<DateTime> timestamps, double fallbackHz = 50)
    {
        if (timestamps.Count >= 3)
        {
            var span = (timestamps[^1] - timestamps[0]).TotalSeconds;
            if (span > 1e-6)
            {
                var hz = (timestamps.Count - 1) / span;
                if (hz > 0.1 && hz < 200_000) return hz;
            }
        }
        return Math.Max(1, fallbackHz);
    }

    public static double EstimateEffectiveHz(DateTime first, DateTime last, long sampleCount, double fallbackHz = 50)
    {
        if (sampleCount < 3) return Math.Max(1, fallbackHz);
        var span = (last - first).TotalSeconds;
        if (span <= 1e-6) return Math.Max(1, fallbackHz);
        var hz = (sampleCount - 1) / span;
        return hz is > 0.1 and < 200_000 ? hz : Math.Max(1, fallbackHz);
    }

    public static double MeanDeltaSeconds(IReadOnlyList<DateTime> timestamps)
    {
        if (timestamps.Count < 2) return double.NaN;
        var sum = 0.0;
        var n = 0;
        for (var i = 1; i < timestamps.Count; i++)
        {
            var dt = (timestamps[i] - timestamps[i - 1]).TotalSeconds;
            if (dt <= 0 || double.IsNaN(dt) || double.IsInfinity(dt)) continue;
            sum += dt;
            n++;
        }
        return n == 0 ? double.NaN : sum / n;
    }

    /// <summary>Experiment wall duration (last − first timestamp), seconds. NaN if &lt; 2 stamps.</summary>
    public static double ExperimentDurationSeconds(IReadOnlyList<DateTime> timestamps)
    {
        if (timestamps.Count < 2) return double.NaN;
        var span = (timestamps[^1] - timestamps[0]).TotalSeconds;
        return span >= 0 && !double.IsNaN(span) && !double.IsInfinity(span) ? span : double.NaN;
    }

    public static string FormatDuration(double totalSeconds)
    {
        if (double.IsNaN(totalSeconds) || double.IsInfinity(totalSeconds) || totalSeconds < 0)
            return "—";
        if (totalSeconds < 60)
            return totalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " s";
        var ts = TimeSpan.FromSeconds(totalSeconds);
        if (ts.TotalHours < 1)
            return $"{ts.Minutes} min {ts.Seconds} s ({totalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s)";
        return $"{(int)ts.TotalHours} h {ts.Minutes} min {ts.Seconds} s ({totalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s)";
    }

    /// <summary>
    /// First-page lab line: start / stop (PC local) + duration.
    /// Example: Start 2026-08-10 13:29:36 · Stop 13:45:02 · Durată 15 min 26 s (926.1 s)
    /// </summary>
    public static string FormatExperimentTime(IReadOnlyList<DateTime> timestamps)
    {
        if (timestamps.Count == 0) return "— (fără eșantioane)";
        var start = AppClock.ToPcLocal(timestamps[0]);
        if (timestamps.Count == 1)
            return $"Start {AppClock.FormatUi(start)} · Durată — (1 eșantion)";

        var stop = AppClock.ToPcLocal(timestamps[^1]);
        var dur = ExperimentDurationSeconds(timestamps);
        var sameDay = start.Date == stop.Date;
        var startTxt = AppClock.FormatUi(start);
        var stopTxt = sameDay ? stop.ToString("HH:mm:ss") : AppClock.FormatUi(stop);
        return $"Start {startTxt} · Stop {stopTxt} · Durată {FormatDuration(dur)}";
    }

    /// <summary>Seconds since first sample (wall clock). Empty → empty.</summary>
    public static double[] RelativeSeconds(IReadOnlyList<DateTime> timestamps)
    {
        if (timestamps.Count == 0) return Array.Empty<double>();
        var t0 = timestamps[0];
        var t = new double[timestamps.Count];
        for (var i = 0; i < timestamps.Count; i++)
            t[i] = (timestamps[i] - t0).TotalSeconds;
        return t;
    }

    public static double RelativeSeconds(DateTime timestamp, DateTime t0) =>
        (timestamp - t0).TotalSeconds;

    public static string FormatHz(double hz) =>
        hz.ToString("0.###", CultureInfo.InvariantCulture);

    public static string FormatDt(double dtSec) =>
        double.IsNaN(dtSec)
            ? "—"
            : dtSec.ToString("0.######", CultureInfo.InvariantCulture);

    public static IEnumerable<string> MetaLines(int rateSetHz, double rateEffectiveHz, double meanDtSec)
    {
        yield return $"RateHzSet={rateSetHz}";
        yield return $"RateHzEffective={FormatHz(rateEffectiveHz)}";
        yield return $"DtMeanSec={FormatDt(meanDtSec)}";
        yield return "Note=t_s is seconds from first sample (wall clock); fs_ef=(N-1)/duration";
    }
}
