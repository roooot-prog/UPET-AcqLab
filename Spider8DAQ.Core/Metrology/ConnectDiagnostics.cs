using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Core.Metrology;

/// <summary>Connect / Preflight checks for excitation, gain expectations, and half-bridge thermal tips.</summary>
public static class ConnectDiagnostics
{
    public sealed record ExcitationWarning(
        int ChannelIndex,
        string ChannelName,
        double ConfiguredV,
        double ExpectedV,
        string Message);

    public sealed record ThermalTip(string ChannelName, string Message);

    /// <summary>
    /// Warn when Exc V on bridge channels differs from sensor-library expectation enough to skew Scale.
    /// </summary>
    public static IReadOnlyList<ExcitationWarning> CheckExcitation(
        IReadOnlyList<ChannelConfig> channels,
        Func<string?, double?>? expectedExcitationBySensorId = null)
    {
        var list = new List<ExcitationWarning>();
        foreach (var ch in channels)
        {
            if (!ch.Enabled) continue;
            if (ch.Bridge is BridgeType.None or BridgeType.DcVoltage) continue;

            double? expected = null;
            if (expectedExcitationBySensorId is not null && !string.IsNullOrWhiteSpace(ch.SensorId))
                expected = expectedExcitationBySensorId(ch.SensorId);

            // Catalog / Easy often 5 V; Spider8 SoftSetup typically 2.5 V — only warn on odd values.
            var cfg = ch.ExcitationV;
            if (cfg <= 0 || cfg > 12.5)
            {
                list.Add(new ExcitationWarning(
                    ch.Index, ch.Name, cfg, expected ?? 2.5,
                    $"Excitație suspectă pe {ch.Name}: {cfg:0.##} V (așteptat 0.5…12.5 V; tipic 2.5 V pe Spider8)."));
                continue;
            }

            if (expected is double exp && Math.Abs(cfg - exp) > MetrologyConstants.ExcitationMismatchTolV
                && !(Math.Abs(exp - 5) < 0.1 && Math.Abs(cfg - 2.5) < 0.1))
            {
                // 5 V Easy vs 2.5 V Spider8 is documented — soft tip, not hard fail.
                list.Add(new ExcitationWarning(
                    ch.Index, ch.Name, cfg, exp,
                    $"Exc V {ch.Name}: configurat {cfg:0.##} V vs catalog {exp:0.##} V — verificați Scale (gain)."));
            }
            else if (expected is double exp5 && Math.Abs(exp5 - 5) < 0.1 && Math.Abs(cfg - 2.5) < 0.1)
            {
                list.Add(new ExcitationWarning(
                    ch.Index, ch.Name, cfg, exp5,
                    $"Info {ch.Name}: catalog Easy 5 V, Spider8 SoftSetup 2.5 V — Scale rămâne valid dacă a fost aplicat din bibliotecă."));
            }
        }

        return list;
    }

    /// <summary>Half-bridge strain without noted thermal compensation → advisor tip.</summary>
    public static IReadOnlyList<ThermalTip> CheckHalfBridgeThermal(IReadOnlyList<ChannelConfig> channels)
    {
        var tips = new List<ThermalTip>();
        foreach (var ch in channels)
        {
            if (!ch.Enabled) continue;
            if (ch.Bridge != BridgeType.Half) continue;
            var unit = ch.Unit ?? "";
            var isStrain = unit.Contains("µm/m", StringComparison.OrdinalIgnoreCase)
                           || unit.Contains("um/m", StringComparison.OrdinalIgnoreCase)
                           || unit.Contains("με", StringComparison.OrdinalIgnoreCase)
                           || (ch.SensorCategory?.Contains("tensom", StringComparison.OrdinalIgnoreCase) ?? false)
                           || (ch.SensorName?.Contains("timbru", StringComparison.OrdinalIgnoreCase) ?? false);
            if (!isStrain && ch.Bridge != BridgeType.Half) continue;

            tips.Add(new ThermalTip(
                ch.Name,
                $"Compensare temperatură: {ch.Name} e Half bridge — verificați compensarea activă (timbru compensat / ½ punte Poisson) înainte de etalonare."));
        }

        return tips;
    }

    public static IReadOnlyList<string> BuildConnectStatusLines(
        IReadOnlyList<ChannelConfig> channels,
        Func<string?, double?>? expectedExcitationBySensorId = null)
    {
        var lines = new List<string>();
        foreach (var w in CheckExcitation(channels, expectedExcitationBySensorId))
            lines.Add(w.Message);
        foreach (var t in CheckHalfBridgeThermal(channels))
            lines.Add(t.Message);
        return lines;
    }
}
