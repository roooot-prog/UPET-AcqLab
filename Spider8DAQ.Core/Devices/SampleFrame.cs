namespace Spider8DAQ.Core.Devices;

public sealed class SampleFrame
{
    public DateTime Timestamp { get; init; }
    public double[] Values { get; init; } = Array.Empty<double>();
    public long Sequence { get; init; }
}
