namespace Spider8DAQ.Core.Devices;

public enum BridgeType
{
    None,
    Quarter,
    Half,
    Full,
    DcVoltage,
    Potentiometric
}

public sealed class ChannelConfig
{
    public int Index { get; set; }
    public int DeviceIndex { get; set; }
    public string Name { get; set; } = "CH";
    public string Unit { get; set; } = "mV/V";
    public bool Enabled { get; set; } = true;
    /// <summary>When false, channel is live/visible but excluded from CSV recording.</summary>
    public bool RecordEnabled { get; set; } = true;
    public double Scale { get; set; } = 1.0;
    public double Offset { get; set; }
    public double TareValue { get; set; }
    public bool IsMathChannel { get; set; }
    public string? MathExpression { get; set; }
    public string? SensorId { get; set; }
    public string? SensorName { get; set; }

    public BridgeType Bridge { get; set; } = BridgeType.None;
    /// <summary>Must match ASA measuring range actually programmed (3 or 12 for Spider8).</summary>
    public double RangeMvPerV { get; set; } = 3.0;
    public double FilterHz { get; set; } = 5;
    public int ChannelSampleRateHz { get; set; } = 50;
    public double ExcitationV { get; set; } = 2.5;
    /// <summary>Nominal shunt resistance (kΩ) for docs/preflight; Spider8 SH does not program R.</summary>
    public double ShuntKohm { get; set; }
    /// <summary>Gauge factor from Asistent Timbru (0 = Timbru never applied).</summary>
    public double GaugeFactor { get; set; }
    /// <summary>Gauge resistance (Ω) from Asistent Timbru.</summary>
    public double GaugeOhm { get; set; }
    /// <summary>Half-bridge Timbru config (dummy T / Simplu / Poisson / Încovoiere).</summary>
    public string? HalfConfig { get; set; }
    /// <summary>Poisson ratio from Timbru (used for Half Poisson shunt/Scale).</summary>
    public double PoissonRatio { get; set; }
    /// <summary>Lab BF from Asistent Timbru (0 = derive from Bridge/HalfConfig/ν).</summary>
    public double BridgeFactor { get; set; }
    public bool ShuntEnabled { get; set; }
    // Prefer finite defaults so JSON serialization never crashes without special NumberHandling.
    public double LastShuntReading { get; set; }
    public bool AlarmEnabled { get; set; }
    public double AlarmLow { get; set; } = -1e9;
    public double AlarmHigh { get; set; } = 1e9;
    /// <summary>Sensor full-scale (engineering units) for gauge/alarm.</summary>
    public double Capacity { get; set; }
    public string? SensorCategory { get; set; }

    public double Apply(double raw) => (raw - TareValue) * Scale + Offset;

    public bool IsInAlarm(double physical)
    {
        if (!AlarmEnabled || double.IsNaN(physical)) return false;
        return physical < AlarmLow || physical > AlarmHigh;
    }
}
