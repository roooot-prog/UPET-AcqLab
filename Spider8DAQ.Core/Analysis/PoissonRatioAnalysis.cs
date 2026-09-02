namespace Spider8DAQ.Core.Analysis;

/// <summary>Apparent Poisson ratio ν = −dε_t/dε_l from linear regression on a window.</summary>
public sealed class PoissonRatioResult
{
    public double Nu { get; init; } = double.NaN;
    public double Slope { get; init; } = double.NaN;
    public double Intercept { get; init; } = double.NaN;
    public double RSquared { get; init; } = double.NaN;
    public int Count { get; init; }
    public int WindowStart { get; init; }
    public int WindowEndExclusive { get; init; }
    public bool IsValid => Count >= 2 && !double.IsNaN(Nu) && !double.IsInfinity(Nu);

    public string ToDisplayString(string? culture = null)
    {
        if (!IsValid)
            return Count < 2 ? "ν: — (puncte insuficiente pe fereastră)" : "ν: —";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"ν ≈ {Nu.ToString("0.####", inv)}  (R²={RSquared.ToString("0.####", inv)}, n={Count}, idx {WindowStart}…{Math.Max(WindowStart, WindowEndExclusive - 1)})";
    }

    public string ToSummary()
    {
        if (!IsValid) return "";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        return $"ν_ap≈{Nu.ToString("0.####", inv)} · R²={RSquared.ToString("0.####", inv)} · n={Count} · fereastră [{WindowStart},{WindowEndExclusive})";
    }
}

public static class PoissonRatioAnalysis
{
    /// <summary>First portion of a series (default 35%), at least <paramref name="minPoints"/> samples when possible.</summary>
    public static (int Start, int EndExclusive) AutoEarlyWindow(int count, double fraction = 0.35, int minPoints = 12)
    {
        if (count <= 0) return (0, 0);
        if (count < 2) return (0, count);
        var n = Math.Max(minPoints, (int)Math.Ceiling(count * Math.Clamp(fraction, 0.05, 1.0)));
        n = Math.Min(count, Math.Max(2, n));
        return (0, n);
    }

    /// <summary>
    /// ν = −slope of ε_t vs ε_l on [start, endExclusive).
    /// Uses ordinary least-squares (same as <see cref="SignalAnalysis.LinearFit"/>).
    /// </summary>
    public static PoissonRatioResult Compute(
        IReadOnlyList<double> epsLongitudinal,
        IReadOnlyList<double> epsTransverse,
        int start = 0,
        int endExclusive = -1)
    {
        var n = Math.Min(epsLongitudinal.Count, epsTransverse.Count);
        if (n < 2)
            return new PoissonRatioResult { Count = n };

        if (endExclusive < 0) endExclusive = n;
        start = Math.Clamp(start, 0, n);
        endExclusive = Math.Clamp(endExclusive, start, n);
        var len = endExclusive - start;
        if (len < 2)
            return new PoissonRatioResult { Count = len, WindowStart = start, WindowEndExclusive = endExclusive };

        var xs = new double[len];
        var ys = new double[len];
        for (var i = 0; i < len; i++)
        {
            xs[i] = epsLongitudinal[start + i];
            ys[i] = epsTransverse[start + i];
        }

        var fit = SignalAnalysis.LinearFit(xs, ys);
        if (fit.Count < 2)
            return new PoissonRatioResult
            {
                Count = fit.Count,
                WindowStart = start,
                WindowEndExclusive = endExclusive
            };

        return new PoissonRatioResult
        {
            Slope = fit.Slope,
            Intercept = fit.Intercept,
            RSquared = fit.RSquared,
            Count = fit.Count,
            Nu = -fit.Slope,
            WindowStart = start,
            WindowEndExclusive = endExclusive
        };
    }

    /// <summary>Live buffer: regress on auto early portion of the accumulated XY points.</summary>
    public static PoissonRatioResult ComputeFromBuffers(
        IReadOnlyList<double> epsLBuffer,
        IReadOnlyList<double> epsTBuffer,
        double earlyFraction = 0.35)
    {
        var n = Math.Min(epsLBuffer.Count, epsTBuffer.Count);
        var (a, b) = AutoEarlyWindow(n, earlyFraction);
        return Compute(epsLBuffer, epsTBuffer, a, b);
    }
}
