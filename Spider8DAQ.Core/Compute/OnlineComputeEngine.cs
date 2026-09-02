namespace Spider8DAQ.Core.Compute;

public enum ComputeOp
{
    Passthrough,
    MovingAverage,
    Rms,
    PeakHold,
    Deadband,
    RateOfChange,
    LowPass1
}

public sealed class ComputeDefinition
{
    public string Name { get; set; } = "Compute";
    public ComputeOp Operation { get; set; } = ComputeOp.MovingAverage;
    public int SourceChannel { get; set; }
    public int Window { get; set; } = 25;
    public double Param { get; set; } = 0.1; // deadband / alpha / etc
    public bool Enabled { get; set; } = true;
}

public sealed class OnlineComputeEngine
{
    private readonly Dictionary<int, Queue<double>> _windows = new();
    private readonly Dictionary<int, double> _peaks = new();
    private readonly Dictionary<int, double> _prev = new();
    private readonly Dictionary<int, double> _lpf = new();

    public double[] Evaluate(IReadOnlyList<ComputeDefinition> defs, double[] physical, double dtSeconds)
    {
        var result = new double[defs.Count];
        for (var i = 0; i < defs.Count; i++)
        {
            var d = defs[i];
            if (!d.Enabled || d.SourceChannel < 0 || d.SourceChannel >= physical.Length)
            {
                result[i] = double.NaN;
                continue;
            }

            var x = physical[d.SourceChannel];
            result[i] = d.Operation switch
            {
                ComputeOp.Passthrough => x,
                ComputeOp.MovingAverage => WindowAvg(i, x, d.Window),
                ComputeOp.Rms => WindowRms(i, x, d.Window),
                ComputeOp.PeakHold => Peak(i, x),
                ComputeOp.Deadband => Math.Abs(x) < d.Param ? 0 : x,
                ComputeOp.RateOfChange => Rate(i, x, dtSeconds),
                ComputeOp.LowPass1 => LowPass(i, x, d.Param),
                _ => x
            };
        }
        return result;
    }

    private double WindowAvg(int id, double x, int n)
    {
        var q = GetQ(id);
        q.Enqueue(x);
        while (q.Count > Math.Max(1, n)) q.Dequeue();
        return q.Average();
    }

    private double WindowRms(int id, double x, int n)
    {
        var q = GetQ(id);
        q.Enqueue(x);
        while (q.Count > Math.Max(1, n)) q.Dequeue();
        return Math.Sqrt(q.Average(v => v * v));
    }

    private double Peak(int id, double x)
    {
        if (!_peaks.TryGetValue(id, out var p) || Math.Abs(x) > Math.Abs(p))
            _peaks[id] = x;
        return _peaks[id];
    }

    private double Rate(int id, double x, double dt)
    {
        if (!_prev.TryGetValue(id, out var p) || dt <= 0)
        {
            _prev[id] = x;
            return 0;
        }
        var r = (x - p) / dt;
        _prev[id] = x;
        return r;
    }

    private double LowPass(int id, double x, double alpha)
    {
        alpha = Math.Clamp(alpha, 0.001, 1);
        if (!_lpf.TryGetValue(id, out var y)) y = x;
        y = y + alpha * (x - y);
        _lpf[id] = y;
        return y;
    }

    private Queue<double> GetQ(int id)
    {
        if (!_windows.TryGetValue(id, out var q))
        {
            q = new Queue<double>();
            _windows[id] = q;
        }
        return q;
    }

    public void Reset()
    {
        _windows.Clear();
        _peaks.Clear();
        _prev.Clear();
        _lpf.Clear();
    }
}
