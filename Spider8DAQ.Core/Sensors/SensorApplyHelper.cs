using Spider8DAQ.Core.Devices;



namespace Spider8DAQ.Core.Sensors;



/// <summary>Maps catalog sensors → channel rows (any CH0–CH7).</summary>

public static class SensorApplyHelper

{

    public sealed class ApplyResult

    {

        public double Scale { get; init; }

        public BridgeType Bridge { get; init; }

        public double ElectricalRange { get; init; }

    }



    public static ApplyResult Apply(ChannelConfig ch, SensorDefinition sensor, bool renameForMultiApply = false)

    {

        ch.Name = renameForMultiApply ? $"{sensor.Name}_{ch.Index + 1}" : sensor.Name;

        ch.Unit = string.IsNullOrWhiteSpace(sensor.Unit) ? "mV/V" : sensor.Unit;

        var bridge = Spider8MeasuringRange.ParseBridge(sensor.Bridge);

        ch.Bridge = bridge;



        // Easy two-point: Scale = Δphys / (e2 − e1) with e2=2 mV/V @ Fnom, e1=ZeroElectrical.

        // Nominal ZeroElectrical=0 → Scale = Fnom/2 (matches Easy 0→0 & 2 mV/V→Fnom).

        var scale = ResolveEngineeringScale(sensor);

        ch.Scale = scale;

        ch.Offset = sensor.Offset;

        // Point1 electrical → tare so (raw − tare)×scale ≈ 0 at zero load (Soft Zero still fine-tunes).

        ch.TareValue = sensor.ZeroElectricalMvPerV;

        ch.SensorId = sensor.Id;

        ch.SensorName = sensor.Name;

        ch.Capacity = sensor.Capacity;

        ch.SensorCategory = sensor.Category;

        // DcVoltage / P15: catalog ExcitationV=0 (amplified 0…10 V) — do NOT force 2.5 V bridge EXC.
        // Bridge strain: missing excitation defaults to 2.5 V Spider8 SoftSetup.
        ch.ExcitationV = bridge is BridgeType.DcVoltage or BridgeType.None or BridgeType.Potentiometric
            ? Math.Max(0, sensor.ExcitationV)
            : (sensor.ExcitationV > 0 ? sensor.ExcitationV : 2.5);

        ch.FilterHz = ResolveFilterHz(sensor, bridge);

        if (sensor.ChannelSampleRateHz > 0)

            ch.ChannelSampleRateHz = sensor.ChannelSampleRateHz;

        ch.RangeMvPerV = Spider8MeasuringRange.ResolveElectricalFullScale(

            bridge, sensor.RangeMvPerV, sensor.Capacity, sensor.Unit);



        if (sensor.Capacity > 0)

        {

            ch.AlarmEnabled = true;

            ch.AlarmLow = IsRelativePressure(sensor) ? 0 : -Math.Abs(sensor.Capacity) * 1.05;

            ch.AlarmHigh = Math.Abs(sensor.Capacity) * 1.05;

        }



        return new ApplyResult { Scale = scale, Bridge = bridge, ElectricalRange = ch.RangeMvPerV };

    }



    /// <summary>

    /// Engineering scale from catalog / Easy adaptation.

    /// When Capacity&gt;0 and sensitivity is classic 2 mV/V F.S., prefer Fnom/(2−e0).

    /// </summary>

    public static double ResolveEngineeringScale(SensorDefinition sensor)

    {

        var libraryScale = StrainScale.Resolve(sensor.Unit, sensor.Bridge, sensor.Scale, sensor.Sensitivity);

        // Capacity is Autorange domain / Fnom — never a Timbru conversion Scale.
        if (StrainScale.IsStrainUnit(sensor.Unit))
            return libraryScale;

        if (sensor.Capacity > 0 && Math.Abs(sensor.Sensitivity - 2.0) < 1e-6)

        {

            var denom = 2.0 - sensor.ZeroElectricalMvPerV;

            if (Math.Abs(denom) > 1e-9)

            {

                var fromTwoPoint = sensor.Capacity / denom;

                // Prefer two-point when library scale is missing/placeholder or matches Fnom/2 nominal.

                if (Math.Abs(libraryScale) < 1e-9

                    || Math.Abs(libraryScale - 1.0) < 1e-6

                    || Math.Abs(libraryScale - sensor.Capacity / 2.0) < 1e-3)

                    return fromTwoPoint;

            }

        }

        return libraryScale;

    }



    public static IReadOnlyList<string> AuditLibrary(SensorLibrary library)

    {

        var issues = new List<string>();

        foreach (var s in library.Sensors)

        {

            if (string.IsNullOrWhiteSpace(s.Name))

                issues.Add($"{s.Code}: nume lipsă");

            if (string.IsNullOrWhiteSpace(s.Unit))

                issues.Add($"{s.Code}: unitate lipsă");

            var bridge = Spider8MeasuringRange.ParseBridge(s.Bridge);

            if (bridge == BridgeType.None && s.Category != SensorCategories.Temperature)

                issues.Add($"{s.Code}: bridge None — verificați cablaj");

            if (s.Scale <= 0 && !StrainScale.IsStrainUnit(s.Unit))

                issues.Add($"{s.Code}: scale={s.Scale} — verificați calibrare");

            _ = Spider8MeasuringRange.ResolveElectricalFullScale(

                bridge, s.RangeMvPerV, s.Capacity, s.Unit);

        }

        return issues;

    }



    private static double ResolveFilterHz(SensorDefinition sensor, BridgeType bridge)

    {

        var hz = sensor.FilterHz > 0 ? sensor.FilterHz : bridge switch

        {

            BridgeType.DcVoltage => 20,

            BridgeType.None => 2,

            _ => 10

        };

        return Math.Clamp(hz, 0.5, 200);

    }



    private static bool IsRelativePressure(SensorDefinition s) =>

        s.Category == SensorCategories.Pressure

        && s.Name.Contains("rel", StringComparison.OrdinalIgnoreCase);

}


