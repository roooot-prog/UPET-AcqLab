namespace Spider8DAQ.Core.Sensors;

/// <summary>
/// Spider8-30 / SR30 internal shunt resistances from HBM command help (ASS, 2004).
/// Firmware does not report ohm — ASS only switches the resistor on/off (1 mV/V ±1%).
/// Pin map (parallel to the completion resistor):
///   120 Ω pin 3 → 29.9 kΩ ±0.1%; 350 Ω pin 10 → 87.35 kΩ ±0.1%; 700 Ω pin 9 → 175 kΩ ±0.1%.
/// </summary>
public static class Spider8InternalShunt
{
    public const double For120OhmKohm = 29.9;
    public const double For350OhmKohm = 87.35;
    public const double For700OhmKohm = 175.0;
    /// <summary>Default when gauge R is unknown — 350 Ω completion pin (most common lab wiring).</summary>
    public const double DefaultKohm = For350OhmKohm;
    public const string ModelSpider830 = "Spider8-30";

    public static bool IsSimulatorFirmware(string? firmwareOrIdn)
    {
        if (string.IsNullOrWhiteSpace(firmwareOrIdn)) return false;
        return firmwareOrIdn.Contains("SIM", StringComparison.OrdinalIgnoreCase)
               || firmwareOrIdn.Contains("simulator", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when IDN/firmware names Spider8-30 or SR30 (internal shunt exists).
    /// Generic Spider8 / Spider8-55 / Spider8-01 do not get a mapped Rsh.
    /// </summary>
    public static bool IsSpider830(string? firmwareOrIdn)
    {
        if (string.IsNullOrWhiteSpace(firmwareOrIdn) || IsSimulatorFirmware(firmwareOrIdn))
            return false;

        var s = firmwareOrIdn.Trim().Trim('"');
        if (s.Contains("Spider8-30", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Spider8_30", StringComparison.OrdinalIgnoreCase))
            return true;
        if (s.Contains("SR30", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (var part in s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Trim().Trim('"');
            if (p.Equals(ModelSpider830, StringComparison.OrdinalIgnoreCase))
                return true;
            if (p.StartsWith("Spider8-30", StringComparison.OrdinalIgnoreCase))
                return true;
            if (p is "5057" or "5058")
                return true;
        }

        return false;
    }

    /// <summary>Documented internal Rsh (kΩ) for the completion pin matching gauge resistance.</summary>
    public static double KohmForGaugeOhm(double gaugeOhm)
    {
        if (gaugeOhm < 1.0)
            return DefaultKohm;

        var d120 = Math.Abs(gaugeOhm - 120.0);
        var d350 = Math.Abs(gaugeOhm - 350.0);
        var d700 = Math.Abs(gaugeOhm - 700.0);
        if (d120 <= d350 && d120 <= d700)
            return For120OhmKohm;
        if (d700 <= d350)
            return For700OhmKohm;
        return For350OhmKohm;
    }

    public static Spider8InternalShuntResolution Resolve(string? firmwareOrIdn, double gaugeOhm)
    {
        if (IsSimulatorFirmware(firmwareOrIdn))
        {
            return new Spider8InternalShuntResolution
            {
                Kind = Spider8InternalShuntKind.Simulator,
                ModelLabel = "simulator",
                Source = "simulator",
                StatusLabel = "simulator — Rsh intern nu se completează"
            };
        }

        if (!IsSpider830(firmwareOrIdn))
        {
            return new Spider8InternalShuntResolution
            {
                Kind = Spider8InternalShuntKind.Unknown,
                Source = "unknown",
                StatusLabel = "Rsh: firmware fără Spider8-30 — completați Rsh kΩ"
            };
        }

        var kohm = KohmForGaugeOhm(gaugeOhm);
        var hadIdn = !string.IsNullOrWhiteSpace(firmwareOrIdn);
        return new Spider8InternalShuntResolution
        {
            Kind = Spider8InternalShuntKind.Spider830,
            Kohm = kohm,
            ModelLabel = ModelSpider830,
            FromLiveIdn = hadIdn,
            Source = hadIdn ? "live-idn+model-table" : "model-table",
            StatusLabel = "Rsh = " + kohm.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                          + " kΩ (intern " + ModelSpider830 + ")"
        };
    }
}

public enum Spider8InternalShuntKind
{
    Unknown,
    Simulator,
    Spider830
}

public sealed class Spider8InternalShuntResolution
{
    public Spider8InternalShuntKind Kind { get; init; }
    public double Kohm { get; init; }
    public string ModelLabel { get; init; } = "";
    /// <summary>True when a firmware/IDN string was supplied (live query). The ohm value is always from the model table.</summary>
    public bool FromLiveIdn { get; init; }
    public string Source { get; init; } = "unknown";
    public string StatusLabel { get; init; } = "";
    public bool CanAutoFill => Kind == Spider8InternalShuntKind.Spider830 && Kohm >= ShuntCheck.MinShuntKohm;
}
