namespace Spider8DAQ.Core.Analysis;

public sealed class ChannelStats
{
    public string Name { get; init; } = "";
    public int Count { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double Mean { get; init; }
    public double StdDev { get; init; }
    public double Rms { get; init; }
    public double PeakToPeak => Max - Min;
    public double CrestFactor => Rms > 1e-15 && !double.IsNaN(Rms) ? Math.Max(Math.Abs(Max), Math.Abs(Min)) / Rms : double.NaN;
    public int MinIndex { get; init; }
    public int MaxIndex { get; init; }
}

public sealed class LinearFitResult
{
    public double Slope { get; init; }
    public double Intercept { get; init; }
    public double RSquared { get; init; }
    public int Count { get; init; }
    public double[] FittedY { get; init; } = Array.Empty<double>();
    public override string ToString() =>
        Count < 2 ? "Fit: insuficiente puncte"
        : $"y = {Slope:G6}·x + {Intercept:G6}   R² = {RSquared:0.####}   (n={Count})";
}

public static class SignalAnalysis
{
    public static ChannelStats ComputeStats(string name, IReadOnlyList<double> data)
    {
        var valid = data
            .Select((v, i) => (v, i))
            .Where(x => !double.IsNaN(x.v) && !double.IsInfinity(x.v))
            .ToList();

        if (valid.Count == 0)
        {
            return new ChannelStats
            {
                Name = name,
                Count = 0,
                Min = double.NaN,
                Max = double.NaN,
                Mean = double.NaN,
                StdDev = double.NaN,
                Rms = double.NaN,
                MinIndex = -1,
                MaxIndex = -1
            };
        }

        var min = valid[0];
        var max = valid[0];
        double sum = 0;
        double sumSq = 0;
        foreach (var x in valid)
        {
            sum += x.v;
            sumSq += x.v * x.v;
            if (x.v < min.v) min = x;
            if (x.v > max.v) max = x;
        }

        var mean = sum / valid.Count;
        var variance = valid.Count > 1
            ? valid.Sum(x => (x.v - mean) * (x.v - mean)) / (valid.Count - 1)
            : 0;

        return new ChannelStats
        {
            Name = name,
            Count = valid.Count,
            Min = min.v,
            Max = max.v,
            Mean = mean,
            StdDev = Math.Sqrt(variance),
            Rms = Math.Sqrt(sumSq / valid.Count),
            MinIndex = min.i,
            MaxIndex = max.i
        };
    }

    public static double[] MovingAverage(IReadOnlyList<double> data, int window)
    {
        window = Math.Max(1, window);
        var result = new double[data.Count];
        double sum = 0;
        var q = new Queue<double>();
        for (var i = 0; i < data.Count; i++)
        {
            var v = data[i];
            if (double.IsNaN(v))
            {
                result[i] = double.NaN;
                continue;
            }

            q.Enqueue(v);
            sum += v;
            if (q.Count > window)
                sum -= q.Dequeue();
            result[i] = sum / q.Count;
        }

        return result;
    }

    public static (double[][] Values, int Start, int End) Cut(
        IReadOnlyList<double[]> channels,
        int startIndex,
        int endIndexExclusive)
    {
        if (channels.Count == 0)
            return (Array.Empty<double[]>(), 0, 0);

        var len = channels[0].Length;
        startIndex = Math.Clamp(startIndex, 0, len);
        endIndexExclusive = Math.Clamp(endIndexExclusive, startIndex, len);
        var count = endIndexExclusive - startIndex;
        var result = new double[channels.Count][];
        for (var c = 0; c < channels.Count; c++)
        {
            result[c] = new double[count];
            Array.Copy(channels[c], startIndex, result[c], 0, count);
        }

        return (result, startIndex, endIndexExclusive);
    }

    public static List<int> FindPeaks(IReadOnlyList<double> data, double minProminence = 0)
    {
        var peaks = new List<int>();
        for (var i = 1; i < data.Count - 1; i++)
        {
            var v = data[i];
            if (double.IsNaN(v)) continue;
            if (v >= data[i - 1] && v > data[i + 1])
            {
                if (minProminence <= 0 || v - Math.Min(data[i - 1], data[i + 1]) >= minProminence)
                    peaks.Add(i);
            }
        }

        return peaks;
    }

    /// <summary>Trapezoidal integral vs time (seconds). Returns cumulative integral series.</summary>
    public static double[] Integrate(IReadOnlyList<double> y, IReadOnlyList<DateTime> timestamps)
    {
        var n = Math.Min(y.Count, timestamps.Count);
        var result = new double[n];
        if (n == 0) return result;
        result[0] = 0;
        double acc = 0;
        for (var i = 1; i < n; i++)
        {
            var dt = (timestamps[i] - timestamps[i - 1]).TotalSeconds;
            if (dt <= 0 || double.IsNaN(y[i]) || double.IsNaN(y[i - 1]))
            {
                result[i] = acc;
                continue;
            }
            acc += 0.5 * (y[i] + y[i - 1]) * dt;
            result[i] = acc;
        }
        return result;
    }

    public static double[] Differentiate(IReadOnlyList<double> y, IReadOnlyList<DateTime> timestamps)
    {
        var n = Math.Min(y.Count, timestamps.Count);
        var result = new double[n];
        if (n == 0) return result;
        result[0] = double.NaN;
        for (var i = 1; i < n; i++)
        {
            var dt = (timestamps[i] - timestamps[i - 1]).TotalSeconds;
            if (dt <= 0 || double.IsNaN(y[i]) || double.IsNaN(y[i - 1]))
            {
                result[i] = double.NaN;
                continue;
            }
            result[i] = (y[i] - y[i - 1]) / dt;
        }
        return result;
    }

    public static double[] ScaleOffset(IReadOnlyList<double> data, double scale, double offset)
    {
        var result = new double[data.Count];
        for (var i = 0; i < data.Count; i++)
            result[i] = double.IsNaN(data[i]) ? double.NaN : data[i] * scale + offset;
        return result;
    }

    /// <summary>Înlocuiește valorile în afara mean±k·σ cu NaN.</summary>
    public static double[] RemoveOutliers(IReadOnlyList<double> data, double sigmaK = 3)
    {
        var stats = ComputeStats("tmp", data);
        if (stats.Count < 3 || double.IsNaN(stats.StdDev) || stats.StdDev <= 0)
            return data.ToArray();

        var lo = stats.Mean - sigmaK * stats.StdDev;
        var hi = stats.Mean + sigmaK * stats.StdDev;
        var result = new double[data.Count];
        for (var i = 0; i < data.Count; i++)
        {
            var v = data[i];
            result[i] = !double.IsNaN(v) && (v < lo || v > hi) ? double.NaN : v;
        }
        return result;
    }

    public static LinearFitResult LinearFit(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var n = Math.Min(x.Count, y.Count);
        var pairs = new List<(double x, double y)>(n);
        for (var i = 0; i < n; i++)
        {
            if (double.IsNaN(x[i]) || double.IsNaN(y[i]) || double.IsInfinity(x[i]) || double.IsInfinity(y[i]))
                continue;
            pairs.Add((x[i], y[i]));
        }

        if (pairs.Count < 2)
            return new LinearFitResult { Count = pairs.Count, FittedY = new double[n] };

        double sx = 0, sy = 0, sxx = 0, sxy = 0;
        foreach (var p in pairs)
        {
            sx += p.x;
            sy += p.y;
            sxx += p.x * p.x;
            sxy += p.x * p.y;
        }

        var denom = pairs.Count * sxx - sx * sx;
        var slope = Math.Abs(denom) < 1e-15 ? 0 : (pairs.Count * sxy - sx * sy) / denom;
        var intercept = (sy - slope * sx) / pairs.Count;

        double ssTot = 0, ssRes = 0;
        var meanY = sy / pairs.Count;
        foreach (var p in pairs)
        {
            var fit = slope * p.x + intercept;
            ssTot += (p.y - meanY) * (p.y - meanY);
            ssRes += (p.y - fit) * (p.y - fit);
        }
        var r2 = ssTot <= 1e-15 ? 1 : 1 - ssRes / ssTot;

        var fitted = new double[n];
        for (var i = 0; i < n; i++)
            fitted[i] = double.IsNaN(x[i]) ? double.NaN : slope * x[i] + intercept;

        return new LinearFitResult
        {
            Slope = slope,
            Intercept = intercept,
            RSquared = r2,
            Count = pairs.Count,
            FittedY = fitted
        };
    }

    public sealed class PolyFitResult
    {
        public int Degree { get; init; }
        public double[] Coeffs { get; init; } = Array.Empty<double>(); // a0 + a1 x + a2 x^2 …
        public double RSquared { get; init; }
        public int Count { get; init; }
        public double[] FittedY { get; init; } = Array.Empty<double>();
        public override string ToString()
        {
            if (Count < Degree + 1) return $"Fit poly{Degree}: insuficiente puncte";
            var terms = new List<string>();
            for (var i = 0; i < Coeffs.Length; i++)
            {
                var c = Coeffs[i];
                if (i == 0) terms.Add($"{c:G6}");
                else if (i == 1) terms.Add($"{c:G6}·x");
                else terms.Add($"{c:G6}·x^{i}");
            }
            return $"y = {string.Join(" + ", terms)}   R² = {RSquared:0.####}   (n={Count}, deg={Degree})";
        }
    }

    /// <summary>Least-squares polynomial fit (degree 1–3) via normal equations.</summary>
    public static PolyFitResult PolynomialFit(IReadOnlyList<double> x, IReadOnlyList<double> y, int degree = 2)
    {
        degree = Math.Clamp(degree, 1, 3);
        var n = Math.Min(x.Count, y.Count);
        var pairs = new List<(double x, double y)>(n);
        for (var i = 0; i < n; i++)
        {
            if (double.IsNaN(x[i]) || double.IsNaN(y[i]) || double.IsInfinity(x[i]) || double.IsInfinity(y[i]))
                continue;
            pairs.Add((x[i], y[i]));
        }

        if (pairs.Count < degree + 1)
            return new PolyFitResult { Degree = degree, Count = pairs.Count, FittedY = new double[n] };

        var m = degree + 1;
        var ata = new double[m, m];
        var atb = new double[m];
        foreach (var p in pairs)
        {
            var powers = new double[m];
            powers[0] = 1;
            for (var k = 1; k < m; k++)
                powers[k] = powers[k - 1] * p.x;
            for (var r = 0; r < m; r++)
            {
                atb[r] += powers[r] * p.y;
                for (var c = 0; c < m; c++)
                    ata[r, c] += powers[r] * powers[c];
            }
        }

        var coeffs = SolveLinearSystem(ata, atb);
        if (coeffs is null)
            return new PolyFitResult { Degree = degree, Count = pairs.Count, FittedY = new double[n] };

        double ssTot = 0, ssRes = 0, meanY = pairs.Average(p => p.y);
        foreach (var p in pairs)
        {
            var fit = EvalPoly(coeffs, p.x);
            ssTot += (p.y - meanY) * (p.y - meanY);
            ssRes += (p.y - fit) * (p.y - fit);
        }
        var r2 = ssTot <= 1e-15 ? 1 : 1 - ssRes / ssTot;

        var fitted = new double[n];
        for (var i = 0; i < n; i++)
            fitted[i] = double.IsNaN(x[i]) ? double.NaN : EvalPoly(coeffs, x[i]);

        return new PolyFitResult
        {
            Degree = degree,
            Coeffs = coeffs,
            RSquared = r2,
            Count = pairs.Count,
            FittedY = fitted
        };
    }

    private static double EvalPoly(double[] coeffs, double x)
    {
        double y = 0, xp = 1;
        foreach (var c in coeffs)
        {
            y += c * xp;
            xp *= x;
        }
        return y;
    }

    private static double[]? SolveLinearSystem(double[,] a, double[] b)
    {
        var n = b.Length;
        var m = new double[n, n + 1];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
                m[i, j] = a[i, j];
            m[i, n] = b[i];
        }

        for (var col = 0; col < n; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < n; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < 1e-15) return null;
            if (pivot != col)
            {
                for (var j = 0; j <= n; j++)
                    (m[col, j], m[pivot, j]) = (m[pivot, j], m[col, j]);
            }
            var div = m[col, col];
            for (var j = col; j <= n; j++)
                m[col, j] /= div;
            for (var r = 0; r < n; r++)
            {
                if (r == col) continue;
                var f = m[r, col];
                for (var j = col; j <= n; j++)
                    m[r, j] -= f * m[col, j];
            }
        }

        var x = new double[n];
        for (var i = 0; i < n; i++)
            x[i] = m[i, n];
        return x;
    }

    /// <summary>Simple 1st-order low-pass (approx. Butterworth-ish for lab use).</summary>
    public static double[] LowPass1(IReadOnlyList<double> data, double alpha)
    {
        alpha = Math.Clamp(alpha, 0.01, 1.0);
        var result = new double[data.Count];
        double? prev = null;
        for (var i = 0; i < data.Count; i++)
        {
            if (double.IsNaN(data[i]))
            {
                result[i] = double.NaN;
                continue;
            }
            prev = prev is null ? data[i] : prev.Value + alpha * (data[i] - prev.Value);
            result[i] = prev.Value;
        }
        return result;
    }
}
