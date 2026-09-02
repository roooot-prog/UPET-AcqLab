using System.Text.Json;
using System.Text.Json.Serialization;
using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Sensors;

namespace Spider8DAQ.Core.Calibration;

public sealed class CalibrationPoint
{
    public double Raw { get; set; }
    public double Reference { get; set; }
}

public sealed class CalibrationResult
{
    public double Scale { get; set; } = 1;
    public double Offset { get; set; }
    public double RSquared { get; set; }
    public string Notes { get; set; } = "";
}

public static class CalibrationWizard
{
    public static CalibrationResult FitLinear(IReadOnlyList<CalibrationPoint> points)
    {
        if (points.Count < 2)
            return new CalibrationResult { Notes = "Need at least 2 points." };

        var n = points.Count;
        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        foreach (var p in points)
        {
            sumX += p.Raw;
            sumY += p.Reference;
            sumXX += p.Raw * p.Raw;
            sumXY += p.Raw * p.Reference;
        }

        var denom = n * sumXX - sumX * sumX;
        if (Math.Abs(denom) < 1e-12)
            return new CalibrationResult { Notes = "Degenerate fit." };

        var scale = (n * sumXY - sumX * sumY) / denom;
        var offset = (sumY - scale * sumX) / n;

        // R^2
        var meanY = sumY / n;
        double ssTot = 0, ssRes = 0;
        foreach (var p in points)
        {
            var pred = scale * p.Raw + offset;
            ssTot += (p.Reference - meanY) * (p.Reference - meanY);
            ssRes += (p.Reference - pred) * (p.Reference - pred);
        }

        var r2 = ssTot < 1e-12 ? 1 : 1 - ssRes / ssTot;
        return new CalibrationResult
        {
            Scale = scale,
            Offset = offset,
            RSquared = r2,
            Notes = $"Linear fit on {n} points"
        };
    }

    public static void ApplyToChannel(ChannelConfig channel, CalibrationResult result)
    {
        channel.Scale = result.Scale;
        channel.Offset = result.Offset;
    }
}

public static class WiringDiagrams
{
    public static string Describe(BridgeType bridge) => Describe(bridge, null);

    public static string Describe(BridgeType bridge, string? halfConfig) => bridge switch
    {
        BridgeType.Quarter =>
            "Quarter bridge: un timbru activ + rezistențe de completare (120/350/700Ω). Sense recomandat.",
        BridgeType.Half when StrainScale.IsActivDummyT(halfConfig) =>
            "Activ + timbru pasiv (compensare T°) — echivalent NI Quarter Bridge Type II — pe ACELAȘI canal:\r\n" +
            "• Gactiv — pe grindă / piesă (înregistrează deformarea)\r\n" +
            "• Gpasiv — pe placă nesolicitată lângă piesă (doar temperatură)\r\n" +
            "Hardware: Half bridge pe Spider8. Scale BF=1 (4000/GF).\r\n" +
            "Nu legați pin 120/350/700 (completare internă). Șuntul intern ASS nu e în punte — residual mic ≠ FAIL 1893.",
        BridgeType.Half =>
            "Half bridge: 2 timbre pe același canal (Poisson pe piesă, sau încovoiere sus/jos). Preferabil 5/6 fire.",
        BridgeType.Full =>
            "Full bridge: 4 timbre active. Compensare temperatură maximă. Excitație ± sense.",
        BridgeType.DcVoltage =>
            "Intrare tensiune DC: senzor amplificat / ieșire scalată.",
        BridgeType.Potentiometric =>
            "Potentiometric: senzor ratiometric pe excitație.",
        _ => "Fără punte / canal generic."
    };

    public static string AsciiArt(BridgeType bridge) => AsciiArt(bridge, null);

    public static string AsciiArt(BridgeType bridge, string? halfConfig) => bridge switch
    {
        BridgeType.Full =>
            "+Ex --[G1]--+--[G2]-- -Ex\n" +
            "            |\n" +
            "           Sig+\n" +
            "            |\n" +
            "+Ex --[G4]--+--[G3]-- -Ex\n" +
            "           Sig-",
        BridgeType.Half when StrainScale.IsActivDummyT(halfConfig) =>
            "+Ex --[Gactiv pe grindă]--+--[Gpasiv compensare T° pe placă]-- -Ex\n" +
            "                          |\n" +
            "                         Sig   (1 canal DAQ)",
        BridgeType.Half =>
            "+Ex --[G1]--+--[G2]-- -Ex\n" +
            "            |\n" +
            "           Sig",
        BridgeType.Quarter =>
            "+Ex --[Gactive]--+--[Rcomp]-- -Ex\n" +
            "                 |\n" +
            "                Sig",
        _ => "(generic)"
    };
}
