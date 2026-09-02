namespace Spider8DAQ.Core.Sensors;

public static class SensorCategories
{
    public const string Force = "Forță / Celule de sarcină";
    public const string Torque = "Cuplu";
    public const string Pressure = "Presiune";
    public const string Displacement = "Deplasare / LVDT";
    public const string Strain = "Mărci tensometrice";
    public const string Acceleration = "Accelerație";
    public const string Temperature = "Temperatură";
    public const string Voltage = "Tensiune / DC";
    public const string Potentiometric = "Potențiometric";
    public const string MultiAxis = "Forță multi-axă";
    public const string Humidity = "Umiditate / climă";
    public const string Frequency = "Frecvență / impulsuri";
    public const string Mining = "Minerit / geotehnică UPET";
    public const string Generic = "Punte generică";
    public const string Custom = "Personalizat";

    public static IReadOnlyList<string> All { get; } =
    [
        Force, Torque, Pressure, Displacement, Strain, Acceleration, Temperature, Voltage,
        Potentiometric, MultiAxis, Humidity, Frequency, Mining, Generic, Custom
    ];

    public static string CodePrefix(string category) => category switch
    {
        Force => "FOR",
        Torque => "CUP",
        Pressure => "PRE",
        Displacement => "DEP",
        Strain => "TEN",
        Acceleration => "ACC",
        Temperature => "TMP",
        Voltage => "VOL",
        Potentiometric => "POT",
        MultiAxis => "MAX",
        Humidity => "UMI",
        Frequency => "FRE",
        Mining => "MIN",
        Generic => "GEN",
        _ => "CUS"
    };
}

public static class SensorCatalog
{
    public const int CatalogVersion = 8;

    public static SensorLibrary BuildLargeLibrary()
    {
        var lib = new SensorLibrary { CatalogVersion = CatalogVersion };
        var id = 1;

        SensorDefinition Add(
            string category,
            string name,
            string unit,
            double scale,
            string bridge,
            double capacity = 0,
            double sensitivity = 2,
            double excitation = 2.5,
            double filterHz = 10,
            double range = 2,
            string type = "",
            string notes = "")
        {
            var n = id++;
            var prefix = SensorCategories.CodePrefix(category);
            var s = new SensorDefinition
            {
                Id = $"s{n:D4}",
                Code = $"{prefix}-{n:D4}",
                Category = category,
                Name = name,
                Unit = unit,
                Scale = scale,
                Bridge = bridge,
                Capacity = capacity,
                Sensitivity = sensitivity,
                ExcitationV = excitation,
                FilterHz = filterHz,
                RangeMvPerV = range,
                TransducerType = string.IsNullOrEmpty(type) ? category : type,
                Notes = notes
            };
            lib.Sensors.Add(s);
            return s;
        }

        // Forță
        double[] forcesN = [100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000, 100000, 200000, 500000, 1000000];
        foreach (var c in forcesN)
        {
            var label = c >= 1000 ? $"{c / 1000:0.###} kN" : $"{c:0} N";
            Add(SensorCategories.Force, $"Celulă sarcină {label}", "N", c / 2.0, "Full", c, 2, 2.5, 10, 2, "LoadCell",
                "Scară exemplu ~2 mV/V FS — calibrați pe sit.");
            Add(SensorCategories.Force, $"Tip S {label}", "N", c / 2.0, "Full", c, 2, 5, 10, 2, "LoadCell");
            Add(SensorCategories.Force, $"Grindă forfecare {label}", "N", c / 2.0, "Full", c, 2, 10, 5, 2, "LoadCell");
        }
        Add(SensorCategories.Force, "Platformă 300 kg", "kg", 150, "Full", 300, 2, 10, 5, 2, "LoadCell");
        Add(SensorCategories.Force, "Platformă 600 kg", "kg", 300, "Full", 600, 2, 10, 5, 2, "LoadCell");
        Add(SensorCategories.Force, "Platformă 1.5 t", "kg", 750, "Full", 1500, 2, 10, 5, 2, "LoadCell");
        Add(SensorCategories.Force, "Cântar auto 30 t", "kg", 15000, "Full", 30000, 2, 10, 2, 2, "LoadCell");
        Add(SensorCategories.Force, "Cântar auto 60 t", "kg", 30000, "Full", 60000, 2, 10, 2, 2, "LoadCell");

        // Cuplu
        double[] torques = [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000];
        foreach (var t in torques)
        {
            var label = t >= 1000 ? $"{t / 1000:0.###} kN·m" : $"{t:0.###} N·m";
            Add(SensorCategories.Torque, $"Flanșă cuplu {label}", "N·m", t / 2.0, "Full", t, 2, 5, 20, 2, "Torque");
            Add(SensorCategories.Torque, $"Cuplu reacție {label}", "N·m", t / 2.0, "Full", t, 1.5, 10, 10, 2, "Torque");
        }

        // Presiune
        double[] bars = [0.1, 0.25, 0.6, 1, 1.6, 2.5, 4, 6, 10, 16, 25, 40, 60, 100, 160, 250, 400, 600];
        foreach (var p in bars)
        {
            Add(SensorCategories.Pressure, $"Presiune {p:0.###} bar abs", "bar", p / 2.0, "Full", p, 2, 10, 20, 2, "Pressure");
            Add(SensorCategories.Pressure, $"Presiune {p:0.###} bar rel", "bar", p / 2.0, "Full", p, 2, 10, 20, 2, "Pressure");
        }
        Add(SensorCategories.Pressure, "Diferențial 50 mbar", "mbar", 25, "Full", 50, 2, 5, 10, 2, "Pressure");
        Add(SensorCategories.Pressure, "Diferențial 100 mbar", "mbar", 50, "Full", 100, 2, 5, 10, 2, "Pressure");
        Add(SensorCategories.Pressure, "Vid -1…0 bar", "bar", 0.5, "Full", 1, 2, 10, 10, 2, "Pressure");

        // Deplasare / LVDT
        double[] mm = [1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000];
        foreach (var d in mm)
        {
            Add(SensorCategories.Displacement, $"LVDT ±{d:0} mm", "mm", d / 5.0, "Half", d, 1, 3, 20, 5, "LVDT",
                "Scară aproximativă pentru ±FS — calibrați.");
            Add(SensorCategories.Displacement, $"Potențiometru {d:0} mm", "mm", d / 10.0, "Potentiometric", d, 1, 5, 50, 5, "Pot");
        }
        Add(SensorCategories.Displacement, "Comparator 10 mm / 0.01", "mm", 1, "Half", 10, 1, 2.5, 20, 2, "LVDT");
        Add(SensorCategories.Displacement, "Encoder 360°", "deg", 36, "Potentiometric", 360, 1, 5, 100, 5, "Angle");

        // Mărci tensometrice
        string[] gauges = ["120 Ω", "350 Ω", "700 Ω", "1000 Ω"];
        foreach (var g in gauges)
        {
            Add(SensorCategories.Strain, $"MT 1/4 punte {g}", "µm/m", 1, "Quarter", 0, 2, 2.5, 10, 2, "Strain",
                "Sfert de punte Wheatstone; completare în Spider8.");
            Add(SensorCategories.Strain, $"MT 1/2 punte {g}", "µm/m", 1, "Half", 0, 2, 2.5, 10, 2, "Strain");
            Add(SensorCategories.Strain, $"MT punte completă {g}", "µm/m", 1, "Full", 0, 2, 2.5, 10, 2, "Strain");
        }
        Add(SensorCategories.Strain, "Rozetă 0/45/90 350 Ω", "µm/m", 1, "Quarter", 0, 2, 2.5, 10, 2, "Strain", "3 canale + math rozetă.");
        Add(SensorCategories.Strain, "Rozetă 0/60/120 350 Ω", "µm/m", 1, "Quarter", 0, 2, 2.5, 10, 2, "Strain");
        Add(SensorCategories.Strain, "MT încastrată beton", "µm/m", 1, "Quarter", 0, 2, 2.5, 5, 2, "Strain");
        Add(SensorCategories.Strain, "MT sudabilă", "µm/m", 1, "Quarter", 0, 2, 5, 10, 2, "Strain");

        // Accelerație
        double[] gRange = [1, 2, 5, 10, 20, 50, 100, 200, 500];
        foreach (var g in gRange)
        {
            Add(SensorCategories.Acceleration, $"Accel IEPE ±{g:0} g", "g", g / 10.0, "DcVoltage", g, 1, 0, 100, 5, "Accel",
                "Dacă IEPE e conditionat în tensiune pe modul DC.");
            Add(SensorCategories.Acceleration, $"Accel MEMS ±{g:0} g", "g", g / 5.0, "DcVoltage", g, 1, 3.3, 50, 5, "Accel");
        }
        Add(SensorCategories.Acceleration, "Seismic 0.5 g", "g", 0.1, "DcVoltage", 0.5, 1, 0, 20, 2, "Accel");

        // Temperatură
        Add(SensorCategories.Temperature, "PT100 (-50…150 °C)", "°C", 1, "None", 200, 1, 1, 2, 1, "RTD");
        Add(SensorCategories.Temperature, "PT100 (-200…600 °C)", "°C", 1, "None", 800, 1, 1, 2, 1, "RTD");
        Add(SensorCategories.Temperature, "PT1000", "°C", 1, "None", 200, 1, 1, 2, 1, "RTD");
        Add(SensorCategories.Temperature, "NTC 10k", "°C", 1, "None", 120, 1, 2.5, 2, 1, "Thermistor");
        Add(SensorCategories.Temperature, "Termocuplu K (conditionat)", "°C", 100, "DcVoltage", 1200, 1, 0, 5, 5, "TC");
        Add(SensorCategories.Temperature, "Termocuplu J (conditionat)", "°C", 80, "DcVoltage", 750, 1, 0, 5, 5, "TC");
        Add(SensorCategories.Temperature, "Termocuplu T (conditionat)", "°C", 40, "DcVoltage", 350, 1, 0, 5, 5, "TC");
        Add(SensorCategories.Temperature, "IR 0…500 °C (0–10 V)", "°C", 50, "DcVoltage", 500, 1, 0, 5, 5, "IR");

        // Tensiune / DC
        double[] volts = [0.1, 1, 5, 10, 20, 30, 60];
        foreach (var v in volts)
        {
            Add(SensorCategories.Voltage, $"DC ±{v:0.###} V", "V", v / 5.0, "DcVoltage", v, 1, 0, 50, 5, "Voltage");
            Add(SensorCategories.Voltage, $"DC 0…{v:0.###} V", "V", v / 10.0, "DcVoltage", v, 1, 0, 50, 5, "Voltage");
        }
        Add(SensorCategories.Voltage, "4–20 mA via 250 Ω → 1–5 V", "mA", 4, "DcVoltage", 20, 1, 0, 20, 5, "Current", "Șunt 250Ω; scară în mA.");
        Add(SensorCategories.Voltage, "0–20 mA via 500 Ω", "mA", 4, "DcVoltage", 20, 1, 0, 20, 5, "Current");
        Add(SensorCategories.Voltage, "±10 V proces", "V", 2, "DcVoltage", 10, 1, 0, 100, 5, "Voltage");

        // Potențiometric
        Add(SensorCategories.Potentiometric, "Pot rotativ 1 tură", "deg", 36, "Potentiometric", 360, 1, 5, 50, 5, "Pot");
        Add(SensorCategories.Potentiometric, "Pot rotativ 10 ture", "deg", 360, "Potentiometric", 3600, 1, 5, 50, 5, "Pot");
        Add(SensorCategories.Potentiometric, "Axă joystick", "%", 10, "Potentiometric", 100, 1, 5, 50, 5, "Pot");
        Add(SensorCategories.Potentiometric, "Poziție accelerație", "%", 10, "Potentiometric", 100, 1, 5, 20, 5, "Pot");

        // Multi-axă
        double[] fx = [500, 1000, 2000, 5000, 10000, 20000, 50000];
        foreach (var c in fx)
        {
            var label = c >= 1000 ? $"{c / 1000:0.###} kN" : $"{c:0} N";
            Add(SensorCategories.MultiAxis, $"3-axe Fx {label}", "N", c / 2.0, "Full", c, 2, 5, 20, 2, "MultiAxis");
            Add(SensorCategories.MultiAxis, $"3-axe Fy {label}", "N", c / 2.0, "Full", c, 2, 5, 20, 2, "MultiAxis");
            Add(SensorCategories.MultiAxis, $"3-axe Fz {label}", "N", c / 2.0, "Full", c, 2, 5, 20, 2, "MultiAxis");
            Add(SensorCategories.MultiAxis, $"6-DoF Mx {label}", "N·m", c / 20.0, "Full", c / 10.0, 2, 5, 20, 2, "MultiAxis");
            Add(SensorCategories.MultiAxis, $"6-DoF My {label}", "N·m", c / 20.0, "Full", c / 10.0, 2, 5, 20, 2, "MultiAxis");
            Add(SensorCategories.MultiAxis, $"6-DoF Mz {label}", "N·m", c / 20.0, "Full", c / 10.0, 2, 5, 20, 2, "MultiAxis");
        }

        // Umiditate
        Add(SensorCategories.Humidity, "UR 0–100% (0–10 V)", "%RH", 10, "DcVoltage", 100, 1, 0, 5, 5, "RH");
        Add(SensorCategories.Humidity, "UR 0–100% (0–5 V)", "%RH", 20, "DcVoltage", 100, 1, 0, 5, 5, "RH");
        Add(SensorCategories.Humidity, "UR + T canal T", "°C", 10, "DcVoltage", 80, 1, 0, 5, 5, "Climate");
        Add(SensorCategories.Humidity, "Punct rouă -40…60 °C", "°C", 10, "DcVoltage", 100, 1, 0, 5, 5, "Climate");

        // Frecvență
        double[] hz = [10, 50, 100, 500, 1000, 5000, 10000];
        foreach (var f in hz)
            Add(SensorCategories.Frequency, $"F→V 0…{f:0} Hz", "Hz", f / 10.0, "DcVoltage", f, 1, 0, 50, 5, "Freq");
        Add(SensorCategories.Frequency, "RPM 0…6000 tahometru", "rpm", 600, "DcVoltage", 6000, 1, 0, 50, 5, "RPM");
        Add(SensorCategories.Frequency, "Contor impulsuri", "count", 1, "DcVoltage", 1e6, 1, 0, 100, 5, "Pulse");

        foreach (var kg in new double[] { 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 })
        {
            Add(SensorCategories.Force, $"Punct unic {kg:0} kg", "kg", kg / 2.0, "Full", kg, 2, 5, 5, 2, "LoadCell");
            Add(SensorCategories.Force, $"Canistră {kg:0} kg", "kg", kg / 2.0, "Full", kg, 2, 10, 5, 2, "LoadCell");
        }

        // Minerit / geotehnică — catalog didactic UPET (scări tip, calibrare pe sit)
        Add(SensorCategories.Mining, "Ancoră / bolt load 100 kN", "kN", 50, "Full", 100, 2, 5, 5, 2, "RockBolt",
            "Laborator rezistența rocilor — calibrați pe sit.");
        Add(SensorCategories.Mining, "Ancoră / bolt load 250 kN", "kN", 125, "Full", 250, 2, 5, 5, 2, "RockBolt");
        Add(SensorCategories.Mining, "Ancoră / bolt load 500 kN", "kN", 250, "Full", 500, 2, 10, 2, 2, "RockBolt");
        Add(SensorCategories.Mining, "Presiune stâlp / prop 40 MPa", "MPa", 20, "Full", 40, 2, 5, 5, 2, "Prop");
        Add(SensorCategories.Mining, "Presiune stâlp / prop 60 MPa", "MPa", 30, "Full", 60, 2, 5, 5, 2, "Prop");
        Add(SensorCategories.Mining, "Convergență galerie ±50 mm", "mm", 10, "Half", 50, 1, 5, 10, 5, "Convergence");
        Add(SensorCategories.Mining, "Convergență galerie ±100 mm", "mm", 20, "Half", 100, 1, 5, 10, 5, "Convergence");
        Add(SensorCategories.Mining, "Extensometru foraj 1 m", "mm", 1, "Half", 10, 1, 5, 5, 2, "Extensometer");
        Add(SensorCategories.Mining, "Extensometru foraj 3 m", "mm", 1, "Half", 30, 1, 5, 5, 2, "Extensometer");
        Add(SensorCategories.Mining, "Celulă presiune pământ 500 kPa", "kPa", 250, "Full", 500, 2, 5, 5, 2, "EarthPressure");
        Add(SensorCategories.Mining, "Celulă presiune pământ 2 MPa", "MPa", 1, "Full", 2, 2, 5, 5, 2, "EarthPressure");
        Add(SensorCategories.Mining, "Inclinometru uniaxial (0–5 V)", "deg", 5, "DcVoltage", 30, 1, 0, 10, 5, "Inclino");
        Add(SensorCategories.Mining, "Vibrație utilaj ±50 g", "g", 5, "DcVoltage", 50, 1, 0, 100, 5, "Vibe");
        Add(SensorCategories.Mining, "Debit aer mină (0–10 V)", "m³/s", 1, "DcVoltage", 10, 1, 0, 5, 5, "Airflow");
        Add(SensorCategories.Mining, "CH₄ / gaz (conditionat 4–20 mA)", "ppm", 4, "DcVoltage", 10000, 1, 0, 2, 5, "Gas",
            "Șunt pe canal DC; nu e senzor gaz nativ Spider8.");
        Add(SensorCategories.Mining, "Presiune hidraulică 250 bar", "bar", 125, "Full", 250, 2, 10, 20, 2, "Hydraulic");
        Add(SensorCategories.Mining, "Forță pe cablu / winch 50 kN", "kN", 25, "Full", 50, 2, 5, 10, 2, "Winch");
        Add(SensorCategories.Mining, "Deplasare piston ±200 mm", "mm", 40, "Potentiometric", 200, 1, 5, 20, 5, "Cylinder");

        // Punte generică
        Add(SensorCategories.Generic, "Generic mV/V ±2", "mV/V", 1, "Full", 2, 1, 2.5, 10, 2, "Bridge");
        Add(SensorCategories.Generic, "Generic mV/V ±5", "mV/V", 1, "Full", 5, 1, 2.5, 10, 5, "Bridge");
        Add(SensorCategories.Generic, "Generic mV/V ±10", "mV/V", 1, "Full", 10, 1, 5, 10, 10, "Bridge");
        Add(SensorCategories.Generic, "Jumătate de punte generică", "mV/V", 1, "Half", 2, 1, 2.5, 10, 2, "Bridge");
        Add(SensorCategories.Generic, "Sfert de punte 350 Ω", "mV/V", 1, "Quarter", 2, 1, 2.5, 10, 2, "Bridge");
        Add(SensorCategories.Generic, "Punte 6 fire", "mV/V", 1, "Full", 2, 1, 5, 10, 2, "Bridge", "Linii sense pentru cablu lung.");
        Add(SensorCategories.Generic, "Punte cu shunt-cal", "mV/V", 1, "Full", 2, 1, 2.5, 10, 2, "Bridge", "Folosiți Shunt Cal pe Measure.");

        // HBM P15RVA1 — ieșire amplificată 0…10 V (nu mV/V / strain). 200B = 0…200 bar.
        var p15 = Add(
            SensorCategories.Pressure,
            "HBM P15RVA/1/200B",
            "bar",
            20,
            "DcVoltage",
            200,
            1,
            0,
            20,
            10,
            "Pressure",
            "Transductor presiune HBM P15RVA1, ieșire 0…10 V → 0…200 bar. Scale=20 (bar/V). Canal Spider8: DcVoltage (DC), nu jumătate de punte. Aplică pe canal doar când alegeți Apply.");
        p15.Code = "P15RVA/1/200B";

        // HBM U2B — catman Easy V5 Sensor DB group "U2B" + datasheet B00482.
        // Easy two-point (U2B 5kN screenshot): −0.00864 mV/V→0 N, 2.000 mV/V→5000 N; BE 5 Hz / 50 Hz; R-Shunt 100 kΩ.
        // Nominal catalog: Point1 electrical=0, Scale=Fnom/2 (Easy 0→0 & 2 mV/V→Fnom). Unit N ≤5 kN, kN ≥10 kN.
        // Excitation Easy=5 V; Spider8 SoftSetup typically 2.5 V (datasheet 0.5…12 V) — scale unchanged.
        (string Name, string OrderCode, string ForCode, string Tid, string Unit, double Capacity)[] u2b =
        [
            ("U2B 0.5kN", "1-U2B/500N", "FOR-U2B-500N", "HBM_U2B_500N", "N", 500),
            ("U2B 1kN", "1-U2B/1KN", "FOR-U2B-1KN", "HBM_U2B_1kN", "N", 1000),
            ("U2B 2kN", "1-U2B/2KN", "FOR-U2B-2KN", "HBM_U2B_2kN", "N", 2000),
            ("U2B 5kN", "1-U2B/5KN", "FOR-U2B-5KN", "HBM_U2B_5kN", "N", 5000),
            ("U2B 10kN", "1-U2B/10KN", "FOR-U2B-10KN", "HBM_U2B_10kN", "kN", 10),
            ("U2B 20kN", "1-U2B/20KN", "FOR-U2B-20KN", "HBM_U2B_20kN", "kN", 20),
            ("U2B 50kN", "1-U2B/50KN", "FOR-U2B-50KN", "HBM_U2B_50kN", "kN", 50),
            ("U2B 100kN", "1-U2B/100KN", "FOR-U2B-100KN", "HBM_U2B_100kN", "kN", 100),
            ("U2B 200kN", "1-U2B/200KN", "FOR-U2B-200KN", "HBM_U2B_200kN", "kN", 200),
        ];
        foreach (var v in u2b)
        {
            var s = Add(
                SensorCategories.Force,
                v.Name,
                v.Unit,
                v.Capacity / 2.0,
                "Full",
                v.Capacity,
                2,
                2.5,
                5,
                2,
                "Force",
                $"HBM U2B (catman Easy „{v.Name}”, TID {v.Tid}): Full bridge 350 Ω / 6 wire, 2 mV/V @ Fnom → {v.Capacity} {v.Unit}. " +
                $"Order {v.OrderCode} / {v.ForCode}. Easy Sample/Filter 50 Hz / BE 5 Hz, R-Shunt 100 kΩ, physical ±{v.Capacity} {v.Unit}. " +
                $"Excitație Easy 5 V / Spider8 2,5 V. Apply + Zero (tare) după cablare.");
            s.Code = v.OrderCode;
            s.Id = "u2b-" + v.OrderCode.Replace("1-U2B/", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
            s.ChannelSampleRateHz = 50;
            s.ShuntKohm = 100;
            s.ZeroElectricalMvPerV = 0;
        }

        return lib;
    }
}
