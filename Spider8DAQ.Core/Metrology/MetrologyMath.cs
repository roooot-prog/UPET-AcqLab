namespace Spider8DAQ.Core.Metrology;

/// <summary>Physical constants and shared thresholds for lab metrology.</summary>
public static class MetrologyConstants
{
    /// <summary>Standard gravity (m/s²) for m·g reference force.</summary>
    public const double StandardGravity = 9.80665;

    /// <summary>Flag ε(F) linearity when R² falls below this.</summary>
    public const double LinearityR2Threshold = 0.999;

    /// <summary>|F| above Capacity×factor raises „Scale suspect” (no clamping).</summary>
    public const double ScaleSuspectCapacityFactor = 1.2;

    /// <summary>Default official step window ≈ 0.75 s at typical rates.</summary>
    public const double DefaultStepWindowSeconds = 0.75;

    /// <summary>Excitation mismatch warn threshold (V).</summary>
    public const double ExcitationMismatchTolV = 0.35;
}

public enum StepAggregateKind
{
    Median = 0,
    Mean = 1
}

/// <summary>Rolling buffer for official step readings (median / mean).</summary>
public sealed class RollingSampleWindow
{
    private readonly Queue<double> _q = new();
    private readonly int _capacity;

    public RollingSampleWindow(int capacity)
    {
        _capacity = Math.Max(3, capacity);
    }

    public int Count => _q.Count;

    public void Clear() => _q.Clear();

    public void Push(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return;
        _q.Enqueue(value);
        while (_q.Count > _capacity)
            _q.Dequeue();
    }

    public double Aggregate(StepAggregateKind kind)
    {
        if (_q.Count == 0) return double.NaN;
        var arr = _q.ToArray();
        return kind == StepAggregateKind.Mean ? Mean(arr) : Median(arr);
    }

    public static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0) return double.NaN;
        var a = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).OrderBy(v => v).ToArray();
        if (a.Length == 0) return double.NaN;
        var mid = a.Length / 2;
        return a.Length % 2 == 1 ? a[mid] : 0.5 * (a[mid - 1] + a[mid]);
    }

    public static double Mean(IReadOnlyList<double> values)
    {
        var a = values.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToArray();
        if (a.Length == 0) return double.NaN;
        return a.Average();
    }
}

/// <summary>One load-step official reading (filtered window aggregate).</summary>
public sealed class LoadStepReading
{
    public int StepIndex { get; set; }
    public DateTime Utc { get; set; } = DateTime.UtcNow;
    public long Sequence { get; set; }
    public string Label { get; set; } = "";
    /// <summary>Mass in kg when reference is m·g; 0 if unused.</summary>
    public double MassKg { get; set; }
    /// <summary>Reference force (N) — from m·g or manual / Matest / etalon.</summary>
    public double ReferenceForceN { get; set; }
    /// <summary>Per-channel official values (same index as DAQ channels).</summary>
    public double[] ChannelValues { get; set; } = Array.Empty<double>();
    public string AggregateNote { get; set; } = "";
}

public sealed class ChannelErrorVsReference
{
    public int StepIndex { get; init; }
    public string ChannelName { get; init; } = "";
    public double Measured { get; init; }
    public double Reference { get; init; }
    public double Delta { get; init; }
    public double DeltaPercent { get; init; }
    public string Note { get; init; } = "";
}

public sealed class LinearityReport
{
    public double Slope { get; init; }
    public double Intercept { get; init; }
    public double RSquared { get; init; }
    public int Count { get; init; }
    public bool PassesThreshold { get; init; }
    public double Threshold { get; init; } = MetrologyConstants.LinearityR2Threshold;
    public string Summary { get; init; } = "";
}

public sealed class ZeroHysteresisResult
{
    public double ZeroBefore { get; init; }
    public double ResidualAfterUnload { get; init; }
    public double ResidualPeakAbs { get; init; }
    public double Drift { get; init; }
    public string ChannelName { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class ScaleSuspectAlert
{
    public int ChannelIndex { get; init; }
    public string ChannelName { get; init; } = "";
    public double Value { get; init; }
    public double Capacity { get; init; }
    public double Limit { get; init; }
    public string Message { get; init; } = "";
}

public static class MetrologyMath
{
    /// <summary>Reference force from known mass: F = m · g (N).</summary>
    public static double ForceFromMassKg(double massKg, double g = MetrologyConstants.StandardGravity)
        => massKg * g;

    public static double DeltaPercent(double measured, double reference)
    {
        if (double.IsNaN(measured) || double.IsNaN(reference)) return double.NaN;
        if (Math.Abs(reference) < 1e-12) return double.NaN;
        return 100.0 * (measured - reference) / reference;
    }

    public static IReadOnlyList<ChannelErrorVsReference> ErrorsVsReference(
        LoadStepReading step,
        IReadOnlyList<string> channelNames,
        int forceChannelIndex)
    {
        var list = new List<ChannelErrorVsReference>();
        if (step.ReferenceForceN == 0 || forceChannelIndex < 0 || forceChannelIndex >= step.ChannelValues.Length)
            return list;

        var measured = step.ChannelValues[forceChannelIndex];
        var name = forceChannelIndex < channelNames.Count ? channelNames[forceChannelIndex] : $"CH{forceChannelIndex}";
        var d = measured - step.ReferenceForceN;
        var pct = DeltaPercent(measured, step.ReferenceForceN);
        list.Add(new ChannelErrorVsReference
        {
            StepIndex = step.StepIndex,
            ChannelName = name,
            Measured = measured,
            Reference = step.ReferenceForceN,
            Delta = d,
            DeltaPercent = pct,
            Note = step.MassKg > 0
                ? $"m={step.MassKg:0.###} kg · g={MetrologyConstants.StandardGravity}"
                : "referință forță"
        });
        return list;
    }

    /// <summary>
    /// Linear ε(F) from synchronized step pairs. X = force, Y = strain.
    /// </summary>
    public static LinearityReport FitStrainVsForce(
        IReadOnlyList<double> force,
        IReadOnlyList<double> strain,
        double r2Threshold = MetrologyConstants.LinearityR2Threshold)
    {
        var n = Math.Min(force.Count, strain.Count);
        var pairs = new List<(double x, double y)>(n);
        for (var i = 0; i < n; i++)
        {
            if (double.IsNaN(force[i]) || double.IsNaN(strain[i])) continue;
            if (double.IsInfinity(force[i]) || double.IsInfinity(strain[i])) continue;
            pairs.Add((force[i], strain[i]));
        }

        if (pairs.Count < 2)
        {
            return new LinearityReport
            {
                Count = pairs.Count,
                Summary = "Liniaritate ε(F): insuficiente trepte (min. 2).",
                Threshold = r2Threshold,
                PassesThreshold = false
            };
        }

        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        foreach (var (x, y) in pairs)
        {
            sumX += x;
            sumY += y;
            sumXX += x * x;
            sumXY += x * y;
        }

        var count = pairs.Count;
        var denom = count * sumXX - sumX * sumX;
        if (Math.Abs(denom) < 1e-18)
        {
            return new LinearityReport
            {
                Count = count,
                Summary = "Liniaritate ε(F): degenerate (forță constantă).",
                Threshold = r2Threshold,
                PassesThreshold = false
            };
        }

        var slope = (count * sumXY - sumX * sumY) / denom;
        var intercept = (sumY - slope * sumX) / count;
        var meanY = sumY / count;
        double ssTot = 0, ssRes = 0;
        foreach (var (x, y) in pairs)
        {
            var pred = slope * x + intercept;
            ssTot += (y - meanY) * (y - meanY);
            ssRes += (y - pred) * (y - pred);
        }

        var r2 = ssTot < 1e-18 ? 1.0 : 1.0 - ssRes / ssTot;
        var pass = r2 >= r2Threshold;
        return new LinearityReport
        {
            Slope = slope,
            Intercept = intercept,
            RSquared = r2,
            Count = count,
            Threshold = r2Threshold,
            PassesThreshold = pass,
            Summary = pass
                ? $"ε(F): ε = {slope:G6}·F + {intercept:G6} · R²={r2:0.######} · OK (≥{r2Threshold})"
                : $"ε(F): ε = {slope:G6}·F + {intercept:G6} · R²={r2:0.######} · SUB PRAG ({r2Threshold})"
        };
    }

    /// <summary>
    /// Pair force &amp; strain by common sequence index (frames already share Timestamp+Sequence).
    /// If lengths differ, align by min length / nearest index — backends deliver multi-CH synchronously in one SampleFrame.
    /// </summary>
    public static IReadOnlyList<(long Sequence, double Force, double Strain)> PairForceStrainBySequence(
        IReadOnlyList<long> sequences,
        IReadOnlyList<double> force,
        IReadOnlyList<double> strain)
    {
        var n = Math.Min(sequences.Count, Math.Min(force.Count, strain.Count));
        var list = new List<(long, double, double)>(n);
        for (var i = 0; i < n; i++)
        {
            if (double.IsNaN(force[i]) || double.IsNaN(strain[i])) continue;
            list.Add((sequences[i], force[i], strain[i]));
        }
        return list;
    }
}
