using System.Globalization;
using Spider8DAQ.Core.Devices;

namespace Spider8DAQ.Core.Sensors;

/// <summary>
/// Catman Easy-style channel check / shunt: expected equivalent strain from Timbru metadata + Rsh,
/// compared to the electrical step (SH1) converted with the same Scale 4/(k·B) relation.
/// Autorange domain (Capacity) is not the expected step.
/// </summary>
/// <remarks>
/// Formula (HBM-style, µm/m):
///   ε_shunt ≈ 1e6 × (Rg / (Rsh_Ω × GF)) × B_factor
/// B_factor = 1 / B_w with Scale = 4000 / (GF · B_w) — same physics as Timbru Scale:
///   Quarter (one gauge + pin 120/350/700):  B_w=1, B_factor=1 — ASS is in the bridge
///   Half activ+pasiv T° (two external gauges, BF=1): Scale 4000/GF, but internal ASS
///     sits on unused completion (pin 3/10/9). Do not wire pin 120/350/700 — shunt is
///     NOT in the measuring bridge. Channel-check is Info/PASS (Half+dummy), not 1893.
///   Half Simplu:                            B_w=2, B_factor=1/2 (Scale 2000/GF)
///   Half Poisson:                           B_w=2(1+ν), B_factor=1/(2(1+ν))
///   Half Încovoiere / Full:                 B_w=4, B_factor=1/4 (Scale 1000/GF)
/// Electrical mV/V → µm/m: ε = (mV/V) × 4000 / (GF · B_w) = (mV/V) × Scale_Timbru.
/// </remarks>
public static class ShuntCheck
{
    /// <summary>catman Easy A05566 §4.20 typical allowed difference (percent of expected unbalance).</summary>
    public const double DefaultTolerancePercent = 0.5;
    /// <summary>catman Easy selectable minimum.</summary>
    public const double MinAllowedDifferencePercent = 0.5;
    /// <summary>catman Easy selectable maximum.</summary>
    public const double MaxAllowedDifferencePercent = 5.0;
    /// <summary>PASS-adjacent window for optional Scale-from-shunt apply (not a silent cal).</summary>
    public const double ApplyScaleAdjacentPercent = 20.0;
    /// <summary>OMB/ASA values are |mV/V| ≤ this; larger magnitudes are treated as already µm/m.</summary>
    public const double ElectricalSpanMvPerV = 50.0;
    /// <summary>|ε| above this is garbage (not a shunt step).</summary>
    public const double GarbageStrainUe = 100_000.0;
    /// <summary>Rsh below 1 kΩ is not a shunt resistor (often Rcomp 120/350/700 Ω stored by mistake).</summary>
    public const double MinShuntKohm = 1.0;
    /// <summary>
    /// Half+dummy (two external gauges): ASS is off-bridge. |mV/V| below this is a residual, not 1 mV/V quarter.
    /// </summary>
    public const double HalfDummyResidualMvPerV = 0.6;

    public const string MissingRsh = "lipsește Rsh — completează coloana Rsh kΩ";
    public const string MissingTimbru = "lipsește Timbru — rulează Asistent Timbru / Aplică pe canal";
    public const string NotBridge = "sărit — nu e punte tensometrică — Asistent Timbru / Aplică pe canal pentru tensometrie";
    public const string FailWiring = "FAIL — cablaj / Rsh / punte";
    public const string NoOmb = "FAIL — fără citire OMB (Start măsurare + canal On)";
    public const string SimulatorLabel = "simulator — nu e shunt real";

    public const string SuggestNoOmb =
        "Apăsați Start (nu Stop) și așteptați să se miște Citirea; Catman închis; LIVE fără EST=10003. Dacă backend-ul e Spider32.dll, OMB ASCII e pe DEST/USB — comutați HBM USB (DEST SoftSetup) pentru ASS + OMB?0, sau folosiți Citirea live după shunt, nu Rec.";
    public const string SuggestNoOmbDll =
        "Spider32.dll nu interoghează OMB după shunt (API S8_MeasOneVal, nu ASCII). Comutați HBM USB (DEST SoftSetup) pentru ASS + OMB?0, sau lăsați Start până se mișcă Citirea și reluați Shunt.";
    public const string SuggestEstError =
        "EST=10003 e eroare hard pe LED-ul Spider8 (coduri 10001–10020, nu ACK 10000). Power-cycle, reconectare USB, verificați cablajul; reluați Shunt când bannerul roșu a dispărut.";
    public const string SuggestMissingRsh =
        "Lipsește Rsh pe canal. Connect pe Spider8-30 ca să se umple coloana, sau tastați Rsh kΩ (29.9 = pin 120).";
    public const string SuggestMissingTimbru =
        "Lipsește Timbru pe canal. Asistent Timbru → Aplică pe canal (GF, R Ω, tip punte) înainte de Shunt.";
    public const string SuggestSimulator = "nu e shunt real.";
    public const string SuggestStepFailBody =
        "verifică cablarea 3 fire / pin 120 vs 350 (Rsh 29.9 = pin 120); Arată cablare; dummy pe martor; fără sarcină; nu Aplică Scale din shunt pe FAIL.";
    public const string SuggestStepLow =
        "Treapta e sub așteptat (~25% jos e tipic pin 350 sau 2 fire). Verificați cablarea 3 fire / pin 120 vs 350 (Rsh 29.9 = pin 120); Arată cablare; dummy pe martor; fără sarcină; nu Aplică Scale din shunt pe FAIL.";
    public const string SuggestStepHigh =
        "Treapta e peste așteptat — GF, R Ω sau tipul de punte nu coincid (Half+dummy T° vs Simplu). Rsh pin 120 (29.9 kΩ) vs pin 350 (≈87 kΩ) schimbă treapta; nu Aplică Scale din shunt pe FAIL.";
    public const string SuggestScaleAbsurd =
        "Scale invalid pe canal Timbru — nu e 4000/GF. Aplică Timbru (Scale≈4000/GF, Half+dummy T° ≈1887 la GF=2.12). Autorange/Capacity/domeniul 2000…20000 nu se scrie în Scale.";
    public const string SuggestPass =
        "Treaptă OK față de Timbru+Rsh. Faceți Zero CH pe liber înainte de Rec.";
    public const string PassHalfDummyLabel = "PASS (Half+dummy)";
    public const string InfoHalfDummySkipLabel = "Info (Half+dummy) — SKIP verificare quarter";
    public const string HalfDummySkipBody =
        "shunt intern NU e în punte (2 timbre externe; pin 120 nefolosit). Verificare: apăsare pe activ. Nu e FAIL de cablaj 1893.";
    public const string SuggestHalfDummySkip =
        "Shunt intern ASS stă pe completarea nefolosită (pin 120/350/700). Cu 2 timbre externe nu legați pinul de completare — ASS nu e în punte. Verificați apăsând pe timbrul activ. Nu e FAIL de cablaj 1893; nu Aplică Scale din shunt.";
    public const string ScaleInvalidLabel = "Scale invalid — Aplică Timbru";

    public static double WheatstoneB(BridgeType bridge, string? halfConfig, double poissonRatio)
    {
        var scaleAtGf1 = StrainScale.FromGaugeFactor(bridge, 1.0, halfConfig, poissonRatio);
        if (scaleAtGf1 <= 1e-12) return 1.0;
        return 4000.0 / scaleAtGf1;
    }

    /// <summary>B_factor in ε_sh ≈ 1e6 × (Rg / (Rsh_Ω × GF)) × B_factor (= 1/B_w).</summary>
    public static double BFactor(BridgeType bridge, string? halfConfig, double poissonRatio)
        => 1.0 / WheatstoneB(bridge, halfConfig, poissonRatio);

    public static double ExpectedStrainUe(
        double gaugeOhm,
        double shuntKohm,
        double gaugeFactor,
        BridgeType bridge,
        string? halfConfig,
        double poissonRatio)
    {
        if (gaugeOhm <= 1e-12 || shuntKohm <= 1e-12 || gaugeFactor <= 1e-12)
            return double.NaN;
        var rshOhm = shuntKohm * 1000.0;
        var bf = BFactor(bridge, halfConfig, poissonRatio);
        return 1e6 * (gaugeOhm / (rshOhm * gaugeFactor)) * bf;
    }

    public static double ElectricalToStrainUe(
        double readingMvPerV,
        double gaugeFactor,
        BridgeType bridge,
        string? halfConfig,
        double poissonRatio)
        => readingMvPerV * StrainScale.FromGaugeFactor(bridge, gaugeFactor, halfConfig, poissonRatio);

    public static double ToMeasuredStrainUe(
        double reading,
        double gaugeFactor,
        BridgeType bridge,
        string? halfConfig,
        double poissonRatio)
    {
        if (double.IsNaN(reading) || double.IsInfinity(reading))
            return double.NaN;
        if (Math.Abs(reading) <= ElectricalSpanMvPerV)
            return ElectricalToStrainUe(reading, gaugeFactor, bridge, halfConfig, poissonRatio);
        return reading;
    }

    public static bool InTolerance(double measured, double expected, double tolerancePercent)
    {
        if (!IsFinite(measured) || !IsFinite(expected) || Math.Abs(expected) < 1e-12)
            return false;
        var tol = tolerancePercent > 0 ? tolerancePercent : DefaultTolerancePercent;
        return Math.Abs(measured - expected) <= Math.Abs(expected) * (tol / 100.0);
    }

    /// <summary>Clamp to catman Easy 0.5%–5%. Empty/invalid → 0.5%.</summary>
    public static double ClampAllowedDifferencePercent(double percent)
    {
        if (percent <= 0 || double.IsNaN(percent) || double.IsInfinity(percent))
            return DefaultTolerancePercent;
        return Math.Clamp(percent, MinAllowedDifferencePercent, MaxAllowedDifferencePercent);
    }

    public static bool HasTimbruMetadata(ShuntCheckRequest req)
    {
        if (req.GaugeFactor <= 1e-9 || req.GaugeOhm <= 1e-9)
            return false;
        var b = ParseBridge(req.Bridge);
        if (b is not (BridgeType.Quarter or BridgeType.Half or BridgeType.Full))
            return false;
        if (b == BridgeType.Half && string.IsNullOrWhiteSpace(req.HalfConfig))
            return false;
        return true;
    }

    /// <summary>Half + Activ + timbru pasiv T°: two external gauges, BF=1 dummy (NI Quarter II wiring).</summary>
    public static bool IsHalfDummyTwoGauge(BridgeType bridge, string? halfConfig)
        => bridge == BridgeType.Half && StrainScale.IsActivDummyT(halfConfig);

    public static bool IsHalfDummyTwoGauge(string? bridge, string? halfConfig)
        => IsHalfDummyTwoGauge(ParseBridge(bridge), halfConfig);

    /// <summary>
    /// True when |electrical shunt| is a small residual (not the 1 mV/V quarter step).
    /// |mV/V| &lt; ~0.6, or |ε| much smaller than the unused quarter 1893 figure.
    /// </summary>
    public static bool IsSmallHalfDummyResidual(
        double reading,
        double measuredUe,
        double gaugeFactor,
        string? halfConfig,
        double quarterExpectedUe = double.NaN)
    {
        double absMv;
        if (Math.Abs(reading) <= ElectricalSpanMvPerV)
            absMv = Math.Abs(reading);
        else
        {
            var scale = StrainScale.FromGaugeFactor(BridgeType.Half, gaugeFactor, halfConfig, 0.3);
            if (scale <= 1e-12 || !IsFinite(measuredUe))
                return false;
            absMv = Math.Abs(measuredUe) / scale;
        }

        if (absMv < HalfDummyResidualMvPerV)
            return true;
        if (IsFinite(quarterExpectedUe) && Math.Abs(quarterExpectedUe) > 1e-12
            && IsFinite(measuredUe)
            && Math.Abs(measuredUe) < 0.35 * Math.Abs(quarterExpectedUe))
            return true;
        return false;
    }

    public static string TimbruShortLabel(BridgeType bridge, string? halfConfig)
    {
        return bridge switch
        {
            BridgeType.Quarter => "Quarter",
            BridgeType.Full => "Full",
            BridgeType.Half when StrainScale.IsActivDummyT(halfConfig) => "Half+dummy T°",
            BridgeType.Half when halfConfig is not null
                && halfConfig.Contains("Poiss", StringComparison.OrdinalIgnoreCase) => "Half Poisson",
            BridgeType.Half when halfConfig is not null
                && (halfConfig.Contains("ncovoi", StringComparison.OrdinalIgnoreCase)
                    || halfConfig.Equals("Incovoiere", StringComparison.OrdinalIgnoreCase)) => "Half încovoiere",
            BridgeType.Half => "Half Simplu",
            _ => bridge.ToString()
        };
    }

    public static ShuntCheckResult Evaluate(ShuntCheckRequest req)
    {
        var name = string.IsNullOrWhiteSpace(req.ChannelName) ? "CH?" : req.ChannelName.Trim();
        var prefix = "Shunt " + name + ": ";
        var inv = CultureInfo.InvariantCulture;
        var tol = ClampAllowedDifferencePercent(req.TolerancePercent);

        if (req.Simulator)
        {
            var simVal = IsFinite(req.Reading)
                ? req.Reading.ToString("0.000", inv)
                : "n/a";
            return Finish(new ShuntCheckResult
            {
                Kind = ShuntCheckKind.Simulator,
                Passed = false,
                StatusLine = prefix + simVal + " (" + SimulatorLabel + ")",
                ElectricalReading = req.Reading
            }, req);
        }

        var bridge = ParseBridge(req.Bridge);
        if (!HasTimbruMetadata(req))
        {
            if (StrainScale.IsStrainUnit(req.Unit))
            {
                return Finish(new ShuntCheckResult
                {
                    Kind = ShuntCheckKind.Incomplete,
                    Passed = false,
                    StatusLine = prefix + MissingTimbru,
                    ElectricalReading = req.Reading
                }, req);
            }

            if (bridge is BridgeType.DcVoltage or BridgeType.None or BridgeType.Potentiometric)
            {
                return new ShuntCheckResult
                {
                    Kind = ShuntCheckKind.Skipped,
                    Passed = false,
                    StatusLine = prefix + NotBridge,
                    ElectricalReading = req.Reading
                };
            }

            var unit = string.IsNullOrWhiteSpace(req.Unit) ? "?" : req.Unit.Trim();
            return new ShuntCheckResult
            {
                Kind = ShuntCheckKind.Skipped,
                Passed = false,
                StatusLine = prefix + "sărit — nu e canal Timbru (" + unit
                             + "); Asistent Timbru / Aplică pe canal pentru tensometrie",
                ElectricalReading = req.Reading
            };
        }

        var halfDummy = IsHalfDummyTwoGauge(bridge, req.HalfConfig);
        if (!halfDummy && req.ShuntKohm < MinShuntKohm)
        {
            return Finish(new ShuntCheckResult
            {
                Kind = ShuntCheckKind.Incomplete,
                Passed = false,
                StatusLine = prefix + MissingRsh,
                ElectricalReading = req.Reading
            }, req);
        }

        if (!IsFinite(req.Reading))
        {
            return Finish(new ShuntCheckResult
            {
                Kind = ShuntCheckKind.NoReading,
                Passed = false,
                StatusLine = prefix + NoOmb,
                ElectricalReading = req.Reading
            }, req);
        }

        var measured = ToMeasuredStrainUe(
            req.Reading, req.GaugeFactor, bridge, req.HalfConfig, req.PoissonRatio);
        var meta = FormatTimbruMeta(bridge, req, inv);

        if (halfDummy)
            return Finish(EvaluateHalfDummy(prefix, req, measured, meta, inv), req);

        var expected = ExpectedStrainUe(
            req.GaugeOhm, req.ShuntKohm, req.GaugeFactor, bridge, req.HalfConfig, req.PoissonRatio);

        if (!IsFinite(expected) || !IsFinite(measured)
            || Math.Abs(measured) > GarbageStrainUe
            || Math.Abs(req.Reading) > GarbageStrainUe)
        {
            return Finish(new ShuntCheckResult
            {
                Kind = ShuntCheckKind.Fail,
                Passed = false,
                MeasuredUe = measured,
                ExpectedUe = expected,
                DeviationPercent = DeviationPercent(measured, expected),
                ElectricalReading = req.Reading,
                StatusLine = prefix + FailWiring + FormatDelta(measured, expected) + meta
            }, req);
        }

        var pass = InTolerance(measured, expected, tol);
        var adjacent = InTolerance(measured, expected, ApplyScaleAdjacentPercent);
        var tolTxt = FormatAllowedPercent(tol);
        var band = " (așteptat " + expected.ToString("0", inv) + " ±" + tolTxt + "%)  ";
        var verdict = pass ? "PASS (±" + tolTxt + "%)" : FailWiring + FormatDelta(measured, expected);
        var strainUnit = StrainScale.IsStrainUnit(req.Unit);

        return Finish(new ShuntCheckResult
        {
            Kind = pass ? ShuntCheckKind.Pass : ShuntCheckKind.Fail,
            Passed = pass,
            CanApplyScale = adjacent && strainUnit,
            MeasuredUe = measured,
            ExpectedUe = expected,
            DeviationPercent = DeviationPercent(measured, expected),
            ElectricalReading = req.Reading,
            StatusLine = prefix + measured.ToString("0", inv) + " µm/m " + band + verdict + meta
        }, req);
    }

    /// <summary>
    /// Two external gauges: ASS is on unused completion. Do not score vs 1e6×Rg/(Rsh×GF).
    /// Small residual → PASS (Half+dummy); larger but not garbage → Info / SKIP quarter-check.
    /// </summary>
    private static ShuntCheckResult EvaluateHalfDummy(
        string prefix, ShuntCheckRequest req, double measured, string meta, CultureInfo inv)
    {
        var quarterWouldBe = req.ShuntKohm >= MinShuntKohm
            ? ExpectedStrainUe(
                req.GaugeOhm, req.ShuntKohm, req.GaugeFactor, BridgeType.Quarter, null, req.PoissonRatio)
            : double.NaN;

        if (!IsFinite(measured)
            || Math.Abs(measured) > GarbageStrainUe
            || Math.Abs(req.Reading) > GarbageStrainUe)
        {
            return new ShuntCheckResult
            {
                Kind = ShuntCheckKind.Fail,
                Passed = false,
                MeasuredUe = measured,
                ExpectedUe = double.NaN,
                ElectricalReading = req.Reading,
                StatusLine = prefix + FailWiring + meta
            };
        }

        var residual = IsSmallHalfDummyResidual(
            req.Reading, measured, req.GaugeFactor, req.HalfConfig, quarterWouldBe);
        var verdict = residual ? PassHalfDummyLabel : InfoHalfDummySkipLabel;
        return new ShuntCheckResult
        {
            Kind = ShuntCheckKind.Info,
            Passed = true,
            CanApplyScale = false,
            MeasuredUe = measured,
            ExpectedUe = double.NaN,
            ElectricalReading = req.Reading,
            StatusLine = prefix + measured.ToString("0", inv) + " µm/m — "
                         + verdict + " — " + HalfDummySkipBody + meta
        };
    }

    private static string FormatTimbruMeta(BridgeType bridge, ShuntCheckRequest req, CultureInfo inv)
    {
        var line = "\nTimbru " + TimbruShortLabel(bridge, req.HalfConfig)
                   + " GF=" + req.GaugeFactor.ToString("0.##", inv)
                   + " R=" + req.GaugeOhm.ToString("0.#", inv);
        if (req.ShuntKohm >= MinShuntKohm)
            line += "  Rsh=" + req.ShuntKohm.ToString("0.##", inv) + " kΩ";
        return line;
    }

    /// <summary>True when the red EST/LED banner is a hard device error (not EST=10000 reset ACK).</summary>
    public static bool LooksLikeEstLedError(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Contains("LED ERROR", StringComparison.OrdinalIgnoreCase)) return true;
        if (text.Contains("Power-cycle", StringComparison.OrdinalIgnoreCase)
            && text.Contains("EST", StringComparison.OrdinalIgnoreCase))
            return true;
        // EST=10001–10020 (10000 = reset ACK).
        return System.Text.RegularExpressions.Regex.IsMatch(
            text, @"EST\s*=\s*100(?:0[1-9]|[1-9]\d)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    public static string FormatSuggestionLine(string suggestion) =>
        string.IsNullOrWhiteSpace(suggestion) ? "" : "Sugestie: " + suggestion.Trim();

    public static string SuggestStepFail(double measuredUe, double expectedUe)
    {
        var delta = FormatDelta(measuredUe, expectedUe).Trim();
        var body = PickStepBody(measuredUe, expectedUe);
        if (delta.Length == 0)
            return body;
        return delta.Trim(' ', '(', ')') + " față de așteptat. " + body;
    }

    public static string PickStepBody(double measuredUe, double expectedUe)
    {
        if (IsFinite(measuredUe) && IsFinite(expectedUe) && Math.Abs(expectedUe) > 1e-12)
        {
            var ratio = measuredUe / expectedUe;
            if (ratio < 0.88)
                return SuggestStepLow;
            if (ratio > 1.12)
                return SuggestStepHigh;
        }

        return SuggestStepFailBody;
    }

    /// <summary>True when the grid Scale is not the GF conversion (91885, domain 10k, ASA…).</summary>
    public static bool ChannelScaleLooksAbsurd(ShuntCheckRequest req)
    {
        if (!StrainScale.IsStrainUnit(req.Unit))
            return false;
        if (!IsFinite(req.ChannelScale))
            return false;
        var formula = StrainScale.FromGaugeFactor(
            ParseBridge(req.Bridge), req.GaugeFactor, req.HalfConfig, req.PoissonRatio);
        return StrainScale.LooksAbsurdTimbruScale(req.ChannelScale, formula);
    }

    public static bool LooksLikeSpider32Dll(string? backendHint)
    {
        if (string.IsNullOrWhiteSpace(backendHint)) return false;
        return backendHint.Contains("Spider32", StringComparison.OrdinalIgnoreCase)
               || backendHint.Contains("S8_Meas", StringComparison.OrdinalIgnoreCase);
    }

    private static ShuntCheckResult Finish(ShuntCheckResult r, ShuntCheckRequest req)
    {
        var sug = PickSuggestion(r, req);
        var line = r.StatusLine ?? "";
        if (ChannelScaleLooksAbsurd(req)
            && !line.Contains(ScaleInvalidLabel, StringComparison.Ordinal))
        {
            var formula = StrainScale.FromGaugeFactor(
                ParseBridge(req.Bridge), req.GaugeFactor, req.HalfConfig, req.PoissonRatio);
            line = line + "\n⚠ " + ScaleInvalidLabel
                   + " (Scale=" + req.ChannelScale.ToString("0", CultureInfo.InvariantCulture)
                   + " → " + StrainScale.SuggestedTimbruScaleLabel(formula) + ")";
        }

        if (string.IsNullOrWhiteSpace(sug))
            return r.StatusLine == line
                ? r
                : Copy(r, line, r.Suggestion);
        // Simulator already says «nu e shunt real» in the parenthetical — don't duplicate.
        if (r.Kind != ShuntCheckKind.Simulator
            && !line.Contains("Sugestie:", StringComparison.Ordinal))
            line = line + "\n" + FormatSuggestionLine(sug);
        return Copy(r, line, sug);
    }

    private static ShuntCheckResult Copy(ShuntCheckResult r, string line, string suggestion) => new()
    {
        Kind = r.Kind,
        Passed = r.Passed,
        CanApplyScale = r.CanApplyScale,
        MeasuredUe = r.MeasuredUe,
        ExpectedUe = r.ExpectedUe,
        DeviationPercent = r.DeviationPercent,
        ElectricalReading = r.ElectricalReading,
        StatusLine = line,
        Suggestion = suggestion
    };

    private static string? PickSuggestion(ShuntCheckResult r, ShuntCheckRequest req)
    {
        if (r.Kind == ShuntCheckKind.Simulator)
            return SuggestSimulator;
        if (r.Kind == ShuntCheckKind.Skipped)
            return null;
        if (r.Kind == ShuntCheckKind.Info)
            return JoinSuggestions(SuggestHalfDummySkip, ChannelScaleLooksAbsurd(req) ? SuggestScaleAbsurd : null);

        var scaleNote = ChannelScaleLooksAbsurd(req) ? SuggestScaleAbsurd : null;

        if (r.Passed)
            return JoinSuggestions(SuggestPass, scaleNote);

        // EST/LED during measurement: prefer recovery over OMB/treaptă.
        if (req.DeviceInError && r.Kind is ShuntCheckKind.NoReading or ShuntCheckKind.Fail)
            return JoinSuggestions(SuggestEstError, scaleNote);

        var primary = r.Kind switch
        {
            ShuntCheckKind.NoReading when LooksLikeSpider32Dll(req.BackendHint) => SuggestNoOmbDll,
            ShuntCheckKind.NoReading => SuggestNoOmb,
            ShuntCheckKind.Fail => SuggestStepFail(r.MeasuredUe, r.ExpectedUe),
            ShuntCheckKind.Incomplete when (r.StatusLine ?? "").Contains(MissingRsh, StringComparison.Ordinal)
                => SuggestMissingRsh,
            ShuntCheckKind.Incomplete => SuggestMissingTimbru,
            _ => null
        };
        return JoinSuggestions(primary, scaleNote);
    }

    private static string? JoinSuggestions(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return string.IsNullOrWhiteSpace(b) ? null : b;
        if (string.IsNullOrWhiteSpace(b) || string.Equals(a, b, StringComparison.Ordinal))
            return a;
        return a + " " + b;
    }

    private static double DeviationPercent(double measured, double expected)
    {
        if (!IsFinite(measured) || !IsFinite(expected) || Math.Abs(expected) < 1e-12)
            return double.NaN;
        return 100.0 * Math.Abs(measured - expected) / Math.Abs(expected);
    }

    private static string FormatDelta(double measured, double expected)
    {
        var pct = DeviationPercent(measured, expected);
        if (!IsFinite(pct)) return "";
        return " (Δ=" + pct.ToString("0.#", CultureInfo.InvariantCulture) + "%)";
    }

    public static double ScaleAfterShunt(double currentScale, double expectedUe, double measuredUe)
        => ScaleAfterShunt(currentScale, expectedUe, measuredUe, formulaScale: double.NaN);

    public static double ScaleAfterShunt(
        double currentScale, double expectedUe, double measuredUe, double formulaScale)
    {
        var basis = currentScale;
        if (IsFinite(formulaScale) && Math.Abs(formulaScale) >= 10
            && StrainScale.LooksAbsurdTimbruScale(currentScale, formulaScale))
            basis = formulaScale;
        if (!IsFinite(basis) || !IsFinite(expectedUe) || !IsFinite(measuredUe) || Math.Abs(measuredUe) < 1e-12)
            return basis;
        var next = basis * (expectedUe / measuredUe);
        if (IsFinite(formulaScale) && StrainScale.LooksAbsurdTimbruScale(next, formulaScale))
            return formulaScale;
        return next;
    }

    private static string FormatAllowedPercent(double percent) =>
        percent.ToString("0.##", CultureInfo.InvariantCulture);

    private static BridgeType ParseBridge(string? bridge)
        => Enum.TryParse<BridgeType>(bridge, true, out var parsed) ? parsed : BridgeType.None;

    private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
}

public enum ShuntCheckKind
{
    Pass,
    Fail,
    Incomplete,
    NoReading,
    Simulator,
    Skipped,
    /// <summary>Half+dummy two-gauge: skip quarter 1893 check (ASS not in bridge).</summary>
    Info
}

public sealed class ShuntCheckRequest
{
    public string ChannelName { get; init; } = "CH0";
    public double Reading { get; init; } = double.NaN;
    public double ShuntKohm { get; init; }
    public double GaugeFactor { get; init; }
    public double GaugeOhm { get; init; }
    public string? Bridge { get; init; }
    public string? HalfConfig { get; init; }
    public double PoissonRatio { get; init; } = 0.3;
    public string? Unit { get; init; }
    public bool Simulator { get; init; }
    public double TolerancePercent { get; init; } = ShuntCheck.DefaultTolerancePercent;
    /// <summary>True when EST/LED Error is latched (red banner) during this shunt.</summary>
    public bool DeviceInError { get; init; }
    /// <summary>Grid Scale (µm/m per mV/V). Shunt ε uses GF formula, not this — warn if absurd.</summary>
    public double ChannelScale { get; init; } = double.NaN;
    /// <summary>Adapter / backend name (Spider32.dll vs DEST) for OMB recovery text.</summary>
    public string? BackendHint { get; init; }
}

public sealed class ShuntCheckResult
{
    public ShuntCheckKind Kind { get; init; }
    public bool Passed { get; init; }
    public bool CanApplyScale { get; init; }
    public double MeasuredUe { get; init; } = double.NaN;
    public double ExpectedUe { get; init; } = double.NaN;
    public double DeviationPercent { get; init; } = double.NaN;
    public double ElectricalReading { get; init; } = double.NaN;
    public string StatusLine { get; init; } = "";
    /// <summary>Recovery text without the «Sugestie:» prefix. Empty on PASS / skip.</summary>
    public string Suggestion { get; init; } = "";
}
