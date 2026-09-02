using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.App.ViewModels;

public sealed class SensorVerificationResult
{
    public bool IsActive { get; init; }
    public bool ConfigOk { get; init; }
    public bool DataOk { get; init; }
    public bool SignalChanging { get; init; }
    public bool RestZero { get; init; }
    public string Verdict { get; init; } = "";
    public string Details { get; init; } = "";
}

/// <summary>Evaluates whether a DAQ channel has an applied, live sensor (UPET AcqLab).</summary>
public static class SensorVerification
{
    private const double NoiseFloorAbs = 1e-5;
    private const double VarianceFloor = 1e-10;
    private const int MinSamples = 4;

    public static bool IsDcOrProcess(ChannelRow ch)
    {
        if (string.Equals(ch.Bridge, nameof(BridgeType.DcVoltage), StringComparison.OrdinalIgnoreCase))
            return true;
        var u = ch.Unit?.Trim() ?? "";
        return u is "V" or "bar" or "mA" or "MPa" or "kPa"
               || u.Contains("bar", StringComparison.OrdinalIgnoreCase);
    }

    public static SensorVerificationResult Evaluate(
        ChannelRow channel,
        IReadOnlyList<double> recentSamples,
        bool isStreaming,
        bool isConnected)
    {
        var lines = new List<string>();
        var chNum = channel.Index + 1;

        var enabled = channel.Enabled;
        var hasSensor = !string.IsNullOrWhiteSpace(channel.SensorName);
        var configOk = enabled && hasSensor;

        lines.Add($"=== Verificare CH{chNum} ===");
        lines.Add($"Nume canal: {channel.Name}");
        lines.Add($"On: {(enabled ? "DA" : "NU")}");
        lines.Add($"Senzor: {(hasSensor ? channel.SensorName : "— (Apply din bibliotecă)")}");
        lines.Add($"Unitate / Scale: {channel.Unit} / {channel.Scale:G6}");
        lines.Add($"Punte: {channel.Bridge}");
        lines.Add($"Graf On: {(channel.ShowOnPlot ? "DA" : "NU")}");

        if (!enabled)
            lines.Add("→ Activați canalul (colona On).");
        if (!hasSensor)
            lines.Add("→ Selectați senzor din bibliotecă și Apply → CH.");

        var valid = recentSamples.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
        var dataOk = false;
        var signalChanging = false;
        var restZero = false;

        if (!isConnected)
        {
            lines.Add("");
            lines.Add("Conectare: NU — conectați Spider8 / USBHBM.");
        }
        else if (!isStreaming)
        {
            lines.Add("");
            lines.Add("Streaming: NU — Start măsurare (F5) pentru citiri live.");
        }
        else if (valid.Count < MinSamples
                 && channel.LiveReading is "—" or "" or null or "off")
        {
            lines.Add("");
            lines.Add("Date: FĂRĂ DATE — citire blocată (—).");
            lines.Add("→ Verificați: On + Graf, senzor Apply, ASA DcVoltage (Stop→Start după Apply), catman închis.");
        }
        else if (valid.Count < MinSamples)
        {
            lines.Add("");
            lines.Add($"Date: aștept cadre… ({valid.Count}/{MinSamples} eșantioane).");
            lines.Add($"Citire grid: {channel.LiveReading} {channel.Unit}");
        }
        else
        {
            dataOk = true;
            var mean = valid.Average();
            var variance = valid.Count > 1
                ? valid.Select(v => (v - mean) * (v - mean)).Average()
                : 0;
            var range = valid.Max() - valid.Min();
            var absMax = valid.Max(v => Math.Abs(v));
            signalChanging = absMax > NoiseFloorAbs || variance > VarianceFloor || range > NoiseFloorAbs;

            lines.Add("");
            lines.Add($"Date live: DA ({valid.Count} eșantioane recente)");
            lines.Add($"Citire: {valid[^1]:G6} {channel.Unit}");
            lines.Add($"Variație (range): {range:G6} {channel.Unit}");

            if (signalChanging)
                lines.Add("Semnal: variație sau valoare detectabilă.");
            else if (IsDcOrProcess(channel))
            {
                restZero = true;
                lines.Add("Semnal: ≈0 (repaus) — activ electric (cadre primite).");
                lines.Add("Sugestie: aplicați presiune / verificați cu multimetru dacă așteptați tensiune.");
            }
            else
            {
                restZero = true;
                lines.Add("Semnal: plat la repaus — flux date OK (punte/cablaj).");
                lines.Add("Sugestie: încărcați senzorul sau shunt pentru verificare mecanică.");
            }
        }

        var isActive = configOk && dataOk;
        string verdict;
        if (!configOk)
            verdict = "Senzor activ: NU (config incompletă)";
        else if (!isConnected || !isStreaming)
            verdict = "Senzor activ: NU (fără măsurare live)";
        else if (!dataOk)
            verdict = "Senzor activ: NU (fără date)";
        else if (restZero && !signalChanging)
            verdict = "Senzor activ: DA (repaus / semnal ≈0)";
        else if (signalChanging)
            verdict = "Senzor activ: DA (semnal live)";
        else
            verdict = "Senzor activ: DA";

        lines.Add("");
        lines.Add(verdict);

        return new SensorVerificationResult
        {
            IsActive = isActive,
            ConfigOk = configOk,
            DataOk = dataOk,
            SignalChanging = signalChanging,
            RestZero = restZero,
            Verdict = verdict,
            Details = string.Join(Environment.NewLine, lines)
        };
    }
}
