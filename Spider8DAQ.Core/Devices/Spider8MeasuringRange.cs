namespace Spider8DAQ.Core.Devices;

/// <summary>Spider8 ASA measuring-range codes (S32_dll.inc) and electrical full-scale helpers.</summary>
public static class Spider8MeasuringRange
{
    public const int AsaFullBridge = 350;
    public const int AsaHalfBridge = 351;
    public const int AsaQuarterBridge = 352;
    public const int AsaDcVoltage = 420;

    public const int Asa3mV = 700;
    public const int Asa12mV = 701;
    public const int Asa0V1 = 710;
    public const int Asa1V0 = 711;
    public const int Asa10V0 = 712;

    public static BridgeType ParseBridge(string? bridge)
    {
        if (string.IsNullOrWhiteSpace(bridge)) return BridgeType.Half;
        return Enum.TryParse<BridgeType>(bridge, true, out var b) ? b : BridgeType.Half;
    }

    public static int BridgeToAsaType(BridgeType bridge) => bridge switch
    {
        BridgeType.Quarter => AsaQuarterBridge,
        BridgeType.Half => AsaHalfBridge,
        BridgeType.Full => AsaFullBridge,
        BridgeType.DcVoltage => AsaDcVoltage,
        BridgeType.Potentiometric => AsaHalfBridge,
        _ => AsaHalfBridge
    };

    /// <summary>Electrical FS used in OMB: mV/V for bridges, volts for DC.</summary>
    public static double ResolveElectricalFullScale(BridgeType bridge, double rangeHint, double capacity, string? unit)
    {
        if (bridge == BridgeType.DcVoltage)
        {
            // Capacity is engineering (bar/N/…) — never treat it as volts.
            if (rangeHint is >= 8 and <= 12) return 10.0;
            if (rangeHint is >= 0.8 and <= 1.5) return 1.0;
            if (rangeHint is > 0 and <= 0.25) return 0.1;
            // P15RVA / amplified 0…10 V process transducers (bar, mA, …)
            _ = capacity;
            return 10.0;
        }

        if (rangeHint > 3.5) return 12.0;
        if (rangeHint > 0) return 3.0;
        return 3.0;
    }

    public static int ToAsaRangeCode(BridgeType bridge, double electricalFs) => bridge switch
    {
        BridgeType.DcVoltage when electricalFs <= 0.15 => Asa0V1,
        BridgeType.DcVoltage when electricalFs <= 1.5 => Asa1V0,
        BridgeType.DcVoltage => Asa10V0,
        _ when electricalFs <= 3.5 => Asa3mV,
        _ => Asa12mV
    };

    /// <summary>OMB decode FS must match the ASA range code actually sent (not catalog sensitivity 2).</summary>
    public static double ProgrammedFsFromAsaCode(int asaRangeCode, double fallback)
        => asaRangeCode switch
        {
            Asa0V1 => 0.1,
            Asa1V0 => 1.0,
            Asa10V0 => 10.0,
            Asa3mV => 3.0,
            Asa12mV => 12.0,
            _ => fallback > 0 ? fallback : 3.0
        };

    /// <summary>
    /// Amplified pressure (P15RVA 0…10 V) must use DC amp — recover if Bridge was left on Full/Half.
    /// Do NOT force every bar channel to DcVoltage: many HBM pressure cells are true mV/V bridges.
    /// </summary>
    public static BridgeType ResolveAcquisitionBridge(ChannelConfig ch)
    {
        if (ch.Bridge == BridgeType.DcVoltage) return BridgeType.DcVoltage;

        var name = ch.SensorName ?? "";
        var id = ch.SensorId ?? "";
        var codeHint = name.Contains("P15", StringComparison.OrdinalIgnoreCase)
                       || id.Contains("P15", StringComparison.OrdinalIgnoreCase);
        if (codeHint) return BridgeType.DcVoltage;

        var unit = ch.Unit ?? "";
        var isPressureUnit = unit.Contains("bar", StringComparison.OrdinalIgnoreCase)
                             || unit.Equals("mbar", StringComparison.OrdinalIgnoreCase)
                             || unit.Equals("MPa", StringComparison.OrdinalIgnoreCase)
                             || unit.Equals("kPa", StringComparison.OrdinalIgnoreCase);
        if (!isPressureUnit) return ch.Bridge;

        // Amplified process transducers: catalog ExcitationV=0 and/or electrical FS already in volts (≥5).
        if (ch.ExcitationV <= 0 || ch.RangeMvPerV >= 5.0)
            return BridgeType.DcVoltage;

        return ch.Bridge;
    }

    public static double ResolveProgrammedElectricalFs(ChannelConfig ch, BridgeType bridge)
    {
        if (bridge == BridgeType.DcVoltage)
            return ResolveElectricalFullScale(bridge, ch.RangeMvPerV, ch.Capacity, ch.Unit);

        // Strain bridges: ASA is only 3 or 12 mV/V — never use catalog sensitivity (2) as OMB FS.
        var hint = ch.RangeMvPerV > 0 ? ch.RangeMvPerV : 3.0;
        return hint > 3.5 ? 12.0 : 3.0;
    }
}
