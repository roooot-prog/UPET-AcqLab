namespace Spider8DAQ.Core.Analysis;

/// <summary>
/// Elastic recovery / permanent strain from unload markers on a strain channel.
/// ε_permanent ≈ ε_end − ε_0; elastic recovery ≈ ε_max − ε_end.
/// </summary>
public sealed class ElasticRecoveryResult
{
    public double Epsilon0 { get; init; } = double.NaN;
    public double EpsilonMax { get; init; } = double.NaN;
    public double EpsilonEnd { get; init; } = double.NaN;
    public double PermanentStrain { get; init; } = double.NaN;
    public double ElasticRecovery { get; init; } = double.NaN;
    public int UnloadStartIndex { get; init; } = -1;
    public int UnloadEndIndex { get; init; } = -1;
    public int MaxIndex { get; init; } = -1;
    public int ZeroIndex { get; init; }
    public bool IsValid =>
        UnloadStartIndex >= 0
        && UnloadEndIndex >= 0
        && MaxIndex >= 0
        && !double.IsNaN(EpsilonMax)
        && !double.IsNaN(EpsilonEnd);

    public string ToDisplayString()
    {
        if (!IsValid)
            return "Recuperare elastică: — (setați marcaje Desc. start / Desc. sfârșit)";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return
            $"ε₀={Fmt(Epsilon0, inv)}  ε_max={Fmt(EpsilonMax, inv)} (idx {MaxIndex})  ε_end={Fmt(EpsilonEnd, inv)} (idx {UnloadEndIndex})  ·  " +
            $"ε_perm≈{Fmt(PermanentStrain, inv)}  recup. elastică≈{Fmt(ElasticRecovery, inv)}";
    }

    public string ToSummary()
    {
        if (!IsValid) return "";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return
            $"ε₀={Fmt(Epsilon0, inv)} · ε_max={Fmt(EpsilonMax, inv)}@{MaxIndex} · ε_end={Fmt(EpsilonEnd, inv)}@{UnloadEndIndex} · " +
            $"ε_perm≈{Fmt(PermanentStrain, inv)} · recup≈{Fmt(ElasticRecovery, inv)} · descărcare [{UnloadStartIndex}→{UnloadEndIndex}]";
    }

    private static string Fmt(double v, System.Globalization.CultureInfo inv)
        => double.IsNaN(v) ? "—" : v.ToString("0.####", inv);
}

public static class ElasticRecoveryAnalysis
{
    /// <summary>
    /// ε_0 = mean of first few valid samples (or index 0); ε_max = peak on [0 … unloadStart] (with small look-ahead);
    /// ε_end at unloadEnd; permanent = ε_end−ε_0; recovery = ε_max−ε_end.
    /// </summary>
    public static ElasticRecoveryResult Compute(
        IReadOnlyList<double> strain,
        int unloadStartIndex,
        int unloadEndIndex,
        int zeroWindow = 5,
        int peakLookAhead = 8)
    {
        if (strain is null || strain.Count < 2)
            return new ElasticRecoveryResult();

        if (unloadStartIndex < 0 || unloadEndIndex < 0)
            return new ElasticRecoveryResult
            {
                UnloadStartIndex = unloadStartIndex,
                UnloadEndIndex = unloadEndIndex
            };

        var n = strain.Count;
        unloadStartIndex = Math.Clamp(unloadStartIndex, 0, n - 1);
        unloadEndIndex = Math.Clamp(unloadEndIndex, 0, n - 1);

        var (eps0, zeroIdx) = ResolveEpsilon0(strain, zeroWindow);
        var peakEnd = Math.Clamp(unloadStartIndex + Math.Max(0, peakLookAhead), 0, n - 1);
        var (epsMax, maxIdx) = FindPeak(strain, 0, peakEnd);
        var epsEnd = strain[unloadEndIndex];
        if (double.IsNaN(epsEnd) || double.IsInfinity(epsEnd))
            epsEnd = NearestValid(strain, unloadEndIndex);

        return new ElasticRecoveryResult
        {
            Epsilon0 = eps0,
            EpsilonMax = epsMax,
            EpsilonEnd = epsEnd,
            PermanentStrain = epsEnd - eps0,
            ElasticRecovery = epsMax - epsEnd,
            UnloadStartIndex = unloadStartIndex,
            UnloadEndIndex = unloadEndIndex,
            MaxIndex = maxIdx,
            ZeroIndex = zeroIdx
        };
    }

    private static (double Value, int Index) ResolveEpsilon0(IReadOnlyList<double> strain, int zeroWindow)
    {
        // Prefer first valid sample (post-Zero baseline). Optional short average for noise.
        zeroWindow = Math.Max(1, zeroWindow);
        double sum = 0;
        var count = 0;
        var firstIdx = 0;
        for (var i = 0; i < strain.Count && count < zeroWindow; i++)
        {
            var v = strain[i];
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            if (count == 0) firstIdx = i;
            // Only average early points that stay near the first reading (quiet baseline).
            if (count > 0 && Math.Abs(v - sum / count) > Math.Max(1.0, Math.Abs(sum / count) * 0.05 + 1e-9))
                break;
            sum += v;
            count++;
        }

        if (count == 0)
            return (0, 0);
        return (sum / count, firstIdx);
    }

    private static (double Value, int Index) FindPeak(IReadOnlyList<double> strain, int start, int endInclusive)
    {
        start = Math.Clamp(start, 0, strain.Count - 1);
        endInclusive = Math.Clamp(endInclusive, start, strain.Count - 1);
        var best = double.NegativeInfinity;
        var bestIdx = start;
        var found = false;
        for (var i = start; i <= endInclusive; i++)
        {
            var v = strain[i];
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            if (!found || v > best)
            {
                best = v;
                bestIdx = i;
                found = true;
            }
        }

        if (!found)
            return (double.NaN, -1);
        return (best, bestIdx);
    }

    private static double NearestValid(IReadOnlyList<double> data, int index)
    {
        for (var d = 0; d < data.Count; d++)
        {
            var lo = index - d;
            var hi = index + d;
            if (lo >= 0 && !double.IsNaN(data[lo]) && !double.IsInfinity(data[lo])) return data[lo];
            if (hi < data.Count && !double.IsNaN(data[hi]) && !double.IsInfinity(data[hi])) return data[hi];
        }
        return double.NaN;
    }
}
