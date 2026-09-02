using System.Globalization;

namespace Spider8DAQ.Core.Simulation;

/// <summary>
/// Simulator-only assignment for «Compresiune cilindru – contur» with 8 circumferential sensors:
/// 4 → 8 mm, 1 → 1 mm, remaining 3 → 3 mm. Ramp 0→target over 10 s, then hold.
/// Drawn randomly on each Start. Sensor indices are 0-based (S1 = 0). Does not change specimen L0/Ø.
/// </summary>
public sealed class ContourSimAssignment
{
    public const int RequiredSensorCount = 8;
    public const int HighCount = 4;
    public const double DurationSeconds = 10.0;
    public const double HighMm = 8.0;
    public const double LowMm = 1.0;
    public const double MidMm = 3.0;

    public IReadOnlyList<int> HighSensorIndices { get; }
    public int LowSensorIndex { get; }

    public ContourSimAssignment(IReadOnlyList<int> highSensorIndices, int lowSensorIndex)
    {
        HighSensorIndices = highSensorIndices;
        LowSensorIndex = lowSensorIndex;
    }

    /// <summary>Fixed pattern for unit tests: S1–S4 = 8 mm, S5 = 1 mm, S6–S8 = 3 mm.</summary>
    public static ContourSimAssignment Default() =>
        new(new[] { 0, 1, 2, 3 }, 4);

    /// <summary>
    /// New random draw: 4 distinct sensors → 8 mm, 1 of the remaining 4 → 1 mm, last 3 → 3 mm.
    /// Pass a seeded <see cref="Random"/> only in tests; production uses <see cref="Random.Shared"/>.
    /// </summary>
    public static ContourSimAssignment RandomDraw(Random? rng = null)
    {
        rng ??= Random.Shared;
        var order = Enumerable.Range(0, RequiredSensorCount).ToArray();
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var high = order.Take(HighCount).OrderBy(i => i).ToArray();
        var low = order[HighCount];
        return new ContourSimAssignment(high, low);
    }

    public bool IsValid => TryValidate(HighSensorIndices, LowSensorIndex, out _);

    public static bool TryValidate(
        IReadOnlyList<int>? highSensorIndices,
        int lowSensorIndex,
        out string errorRo)
    {
        errorRo = "";
        if (highSensorIndices is null)
        {
            errorRo = "Trebuie 4 senzori distincți pentru 8 mm.";
            return false;
        }

        var high = new HashSet<int>();
        foreach (var i in highSensorIndices)
        {
            if (i < 0 || i >= RequiredSensorCount)
            {
                errorRo = "Indicii senzorilor trebuie să fie S1…S8.";
                return false;
            }
            if (!high.Add(i))
            {
                errorRo = "Un senzor nu poate apărea de două ori la 8 mm.";
                return false;
            }
        }

        if (high.Count != HighCount)
        {
            errorRo = $"Trebuie exact {HighCount} senzori pentru 8 mm (acum {high.Count}).";
            return false;
        }

        if (lowSensorIndex < 0 || lowSensorIndex >= RequiredSensorCount)
        {
            errorRo = "Trebuie exact 1 senzor pentru 1 mm.";
            return false;
        }

        if (high.Contains(lowSensorIndex))
        {
            errorRo = $"S{lowSensorIndex + 1} nu poate fi și 8 mm, și 1 mm.";
            return false;
        }

        return true;
    }

    public static bool TryCreate(
        IEnumerable<int>? highSensorIndices,
        int lowSensorIndex,
        out ContourSimAssignment assignment,
        out string errorRo)
    {
        var high = (highSensorIndices ?? Array.Empty<int>()).ToList();
        if (!TryValidate(high, lowSensorIndex, out errorRo))
        {
            assignment = Default();
            return false;
        }

        high.Sort();
        assignment = new ContourSimAssignment(high, lowSensorIndex);
        return true;
    }

    /// <summary>Per-sensor target u [mm] in S1…S8 order (same units as the Contur plot).</summary>
    public double[] TargetsMm()
    {
        var t = new double[RequiredSensorCount];
        Array.Fill(t, MidMm);
        foreach (var i in HighSensorIndices)
        {
            if (i >= 0 && i < t.Length)
                t[i] = HighMm;
        }

        if (LowSensorIndex >= 0 && LowSensorIndex < t.Length)
            t[LowSensorIndex] = LowMm;
        return t;
    }

    public double TargetMm(int sensorIndex)
    {
        if (sensorIndex < 0 || sensorIndex >= RequiredSensorCount) return MidMm;
        if (LowSensorIndex == sensorIndex) return LowMm;
        for (var i = 0; i < HighSensorIndices.Count; i++)
        {
            if (HighSensorIndices[i] == sensorIndex) return HighMm;
        }

        return MidMm;
    }

    public string ToSummaryRo()
    {
        var highSet = new HashSet<int>(HighSensorIndices);
        var mid = new List<int>(3);
        for (var i = 0; i < RequiredSensorCount; i++)
        {
            if (highSet.Contains(i) || i == LowSensorIndex) continue;
            mid.Add(i);
        }

        var inv = CultureInfo.InvariantCulture;
        return
            $"{FormatSensorList(HighSensorIndices.OrderBy(i => i))} = {HighMm.ToString("0", inv)} mm · " +
            $"S{LowSensorIndex + 1} = {LowMm.ToString("0", inv)} mm · " +
            $"{FormatSensorList(mid)} = {MidMm.ToString("0", inv)} mm · " +
            $"rampă 0→țintă în {DurationSeconds.ToString("0", inv)} s, apoi stop";
    }

    private static string FormatSensorList(IEnumerable<int> indices)
    {
        var list = indices.ToList();
        if (list.Count == 0) return "—";
        return string.Join(", ", list.Select(i => $"S{i + 1}"));
    }
}
