using System.Globalization;
using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Core.Display;

/// <summary>
/// Operator Citire policy. Does not change Scale / DEST / acquisition physics.
/// Unused Spider8 channels that stay On still digitize open-bridge noise; Scale (~1887)
/// turns that into a convincing µm/m. Hide those numbers; keep a real near-zero
/// (CH1 Half+dummy ≈ 0.4) when the row is configured.
/// </summary>
public static class LiveCitire
{
    public const string Placeholder = "—";

    /// <summary>
    /// True when the row has a sensor / Timbru / process amp — not an empty unused slot.
    /// </summary>
    public static bool HasMeasurementSetup(
        string? sensorName,
        string? sensorId,
        double gaugeFactor,
        string? halfConfig,
        string? bridge,
        double capacity,
        string? unit = null,
        string? channelName = null)
    {
        if (IsDigitalLike(channelName, unit))
            return true;

        if (IsOffLabel(sensorName))
            return false;
        if (!string.IsNullOrWhiteSpace(sensorName))
            return true;
        if (!string.IsNullOrWhiteSpace(sensorId))
            return true;
        if (gaugeFactor > 0)
            return true;
        if (!string.IsNullOrWhiteSpace(halfConfig))
            return true;
        if (capacity > 0)
            return true;

        if (Enum.TryParse<BridgeType>(bridge, ignoreCase: true, out var b)
            && b is BridgeType.DcVoltage or BridgeType.Potentiometric)
            return true;

        return false;
    }

    /// <summary>
    /// Show an engineering Citire only for a live, configured, in-scan channel.
    /// Off, overflow, link lost, unused, or NaN → hide (em dash).
    /// </summary>
    public static bool ShouldShowEngineeringValue(
        bool enabled,
        bool overflow,
        bool linkLost,
        bool inScan,
        bool hasSetup,
        double physical)
    {
        if (!enabled || overflow || linkLost || !inScan || !hasSetup)
            return false;
        return double.IsFinite(physical);
    }

    public static string FormatOrPlaceholder(
        double physical,
        string? unit,
        bool enabled,
        bool overflow,
        bool linkLost,
        bool inScan,
        bool hasSetup,
        CultureInfo? culture = null)
    {
        if (!ShouldShowEngineeringValue(enabled, overflow, linkLost, inScan, hasSetup, physical))
            return Placeholder;
        return EngineeringDisplay.FormatReading(physical, unit, culture);
    }

    public static bool IsPlaceholder(string? text)
        => string.IsNullOrWhiteSpace(text)
           || text is Placeholder or "off" or "-"
           || text.Equals("NaN", StringComparison.OrdinalIgnoreCase);

    private static bool IsOffLabel(string? sensorName)
    {
        if (string.IsNullOrWhiteSpace(sensorName))
            return false;
        var t = sensorName.Trim();
        return t.Equals("off", StringComparison.OrdinalIgnoreCase)
               || t.Equals("—", StringComparison.Ordinal);
    }

    private static bool IsDigitalLike(string? name, string? unit)
    {
        if (!string.IsNullOrWhiteSpace(unit)
            && unit.Contains("bitmask", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(name))
            return false;
        return name.Contains("DI", StringComparison.OrdinalIgnoreCase);
    }
}
