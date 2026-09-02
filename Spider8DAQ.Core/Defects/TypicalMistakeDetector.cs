using Spider8DAQ.Core.Analysis;

namespace Spider8DAQ.Core.Defects;

/// <summary>
/// Heuristic detectors for typical lab mistakes — clear messages for students,
/// separate from threshold alarms.
/// </summary>
public static class TypicalMistakeDetector
{
    /// <summary>Ignore flat-signal checks for this long after Start / Zero.</summary>
    public const double FlatWarmupSeconds = 4.0;

    /// <summary>Minimum recent samples required for live flat detection.</summary>
    public const int FlatMinSamples = 40;

    /// <summary>Peak-to-peak range must be below max(capacity × this, floor).</summary>
    public const double FlatRangeCapacityFrac = 0.002;

    /// <summary>Std-dev must be below max(capacity × this, floor).</summary>
    public const double FlatStdCapacityFrac = 0.0008;

    public const double FlatAbsRangeFloor = 1e-3;
    public const double FlatAbsStdFloor = 5e-4;

    /// <summary>
    /// Flat near-zero is ambiguous (idle after Zero). Warn near-zero flat only when
    /// another enabled channel has energy, or when |mean| exceeds this fraction of capacity
    /// (typical open-bridge / floating DC offset after Scale).
    /// </summary>
    public const double FlatNonZeroMeanCapacityFrac = 0.001;

    public const double FlatNonZeroMeanFloor = 0.05;

    public static IReadOnlyList<TypicalMistakeFinding> AnalyzeLive(
        IReadOnlyList<ChannelSnapshot> channels,
        TimeSpan? timeSinceZero,
        DateTime? nowLocal = null,
        TimeSpan? timeSinceStart = null)
    {
        var findings = new List<TypicalMistakeFinding>();
        var now = nowLocal ?? DateTime.Now;
        var sinceZero = timeSinceZero ?? TimeSpan.FromHours(24);
        var sinceStart = timeSinceStart ?? TimeSpan.FromHours(24);
        var pastWarmup = sinceZero.TotalSeconds >= FlatWarmupSeconds
                         && sinceStart.TotalSeconds >= FlatWarmupSeconds;

        var enabled = channels.Where(c => c.Enabled && !IsDigitalLike(c.Name)).ToList();
        var energetic = enabled.Where(HasEnergy).ToList();

        foreach (var ch in enabled)
        {
            if (double.IsNaN(ch.Value) || double.IsInfinity(ch.Value)) continue;
            var samples = ch.RecentSamples;
            var std = samples is { Length: > 4 } ? StdDev(samples) : double.NaN;
            var mean = samples is { Length: > 4 } ? samples.Average() : ch.Value;
            var abs = Math.Abs(ch.Value);
            var capacity = ch.Capacity > 0 ? ch.Capacity : GuessCapacity(ch);

            // Saturare
            if (capacity > 0 && abs >= capacity * 0.98)
            {
                Add(findings, "saturare", ch, DefectSeverity.Critical, now,
                    $"Saturare: {ch.Name}={ch.Value:G5} {ch.Unit} ≈ Capacity ({capacity:G5}). Reduceți sarcina sau corectați Scale/range.");
            }

            // Zero uitat — large DC, low noise, no recent Zero
            if (sinceZero.TotalSeconds > 45 && capacity > 0 && abs > Math.Max(capacity * 0.08, 1e-3)
                && (double.IsNaN(std) || std < Math.Max(abs * 0.02, capacity * 0.005)))
            {
                Add(findings, "zero-uitat", ch, DefectSeverity.Warning, now,
                    $"Zero uitat?: {ch.Name} stă la {ch.Value:G5} {ch.Unit} pe semnal aproape static. Faceți Zero pe liber, apoi măsurați.");
            }

            // Presiune fără Zero
            if (IsPressureUnit(ch.Unit) && sinceZero.TotalSeconds > 30 && abs > 0.15
                && (double.IsNaN(std) || std < Math.Max(0.02, abs * 0.05)))
            {
                Add(findings, "presiune-fara-zero", ch, DefectSeverity.Warning, now,
                    $"Presiune fără Zero?: {ch.Name}={ch.Value:G4} {ch.Unit} în repaus. Deschideți la atmosferă → Zero.");
            }

            // Polaritate inversă (force-like)
            if (IsForceLike(ch) && ch.Scale > 0 && ch.Value < -Math.Max(capacity * 0.05, 1)
                && (samples is null || samples.Count(v => v < 0) > samples.Length * 0.7))
            {
                Add(findings, "polaritate-inversa", ch, DefectSeverity.Warning, now,
                    $"Polaritate inversă?: {ch.Name} e predominant negativ ({ch.Value:G5} {ch.Unit}). Inversare polaritate → Zero.");
            }

            // Drift
            if (samples is { Length: >= 20 } && sinceZero.TotalSeconds is > 5 and < 600)
            {
                var drift = samples[^1] - samples[0];
                var noise = Math.Max(StdDev(samples), 1e-9);
                if (Math.Abs(drift) > noise * 8 && Math.Abs(drift) > Math.Max(capacity * 0.01, 0.05))
                {
                    Add(findings, "drift", ch, DefectSeverity.Info, now,
                        $"Drift?: {ch.Name} s-a mișcat Δ={drift:G4} {ch.Unit} fără sarcină clară. Așteptați termic / Zero din nou.");
                }
            }

            // Semnal plat — senzor deconectat / cablu (sustained low variance)
            if (pastWarmup
                && samples is { Length: >= FlatMinSamples }
                && capacity > 0
                && abs < capacity * 0.98
                && IsFlatSignal(samples, capacity, out var flatMean, out _))
            {
                var awayFromZero = Math.Abs(flatMean) > Math.Max(capacity * FlatNonZeroMeanCapacityFrac, FlatNonZeroMeanFloor);
                var peerHasEnergy = energetic.Any(e => e.Index != ch.Index);
                if (awayFromZero || peerHasEnergy)
                {
                    var hint = FormatChannelHint(ch);
                    Add(findings, "semnal-plat", ch, DefectSeverity.Warning, now,
                        $"Semnal plat pe {hint} — verificați cablul / senzorul.");
                }
            }
        }

        // Canal greșit: one Rec channel flat, another Enabled has energy
        var rec = enabled.Where(c => c.RecordEnabled).ToList();
        if (rec.Count > 0 && energetic.Count > 0)
        {
            var deadRec = rec.FirstOrDefault(c => !HasEnergy(c));
            var liveOther = energetic.FirstOrDefault(c => deadRec is null || c.Index != deadRec.Index);
            if (deadRec is not null && liveOther is not null && deadRec.Index != liveOther.Index)
            {
                Add(findings, "canal-gresit", deadRec, DefectSeverity.Warning, now,
                    $"Canal greșit?: Rec pe {deadRec.Name} (plat), dar {liveOther.Name} are semnal. Mutați senzorul/Rec pe canalul util.");
            }
        }

        return findings
            .GroupBy(f => f.DefectId + "|" + f.ChannelHint)
            .Select(g => g.First())
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Title)
            .Take(12)
            .ToList();
    }

    public static IReadOnlyList<TypicalMistakeFinding> AnalyzeOffline(
        OfflineSession session,
        IReadOnlyList<ChannelSnapshot> channelMeta,
        TimeSpan? timeSinceZero = null)
    {
        if (session.Timestamps.Count < 8) return Array.Empty<TypicalMistakeFinding>();
        var findings = new List<TypicalMistakeFinding>();
        var now = DateTime.Now;
        var sinceZero = timeSinceZero ?? TimeSpan.FromHours(1);

        for (var c = 0; c < session.Columns.Count; c++)
        {
            var col = session.Columns[c];
            if (col.Length < 8) continue;
            var name = c < session.ChannelNames.Count ? session.ChannelNames[c] : $"CH{c}";
            var meta = channelMeta.FirstOrDefault(m => m.Index == c)
                       ?? channelMeta.FirstOrDefault(m =>
                           name.StartsWith(m.Name, StringComparison.OrdinalIgnoreCase))
                       ?? new ChannelSnapshot { Index = c, Name = name, Enabled = true, RecordEnabled = true };

            var take = Math.Min(col.Length, 4000);
            var slice = col.AsSpan(0, take).ToArray();
            var min = slice.Min();
            var max = slice.Max();
            var mean = slice.Average();
            var std = StdDev(slice);
            var capacity = meta.Capacity > 0 ? meta.Capacity : Math.Max(Math.Abs(max), Math.Abs(min));
            var unit = meta.Unit;
            if (string.IsNullOrWhiteSpace(unit))
                unit = GuessUnitFromName(name);

            if (capacity > 0 && Math.Abs(max) >= capacity * 0.98
                && CountNear(slice, max, capacity * 0.01) > take * 0.08)
            {
                findings.Add(Finding("saturare", name, DefectSeverity.Critical, now,
                    $"Saturare în înregistrare: {name} plafonează la {max:G5}. Scale/Capacity/sarcină de verificat."));
            }

            if (IsForceLikeUnit(unit) && min < -Math.Abs(max) * 2 && min < -1)
            {
                findings.Add(Finding("polaritate-inversa", name, DefectSeverity.Warning, now,
                    $"Polaritate inversă în CSV?: {name} min={min:G5}, max={max:G5}. Încărcarea pare pe semn −."));
            }

            if (Math.Abs(mean) > Math.Max(capacity * 0.1, 0.5) && std < Math.Abs(mean) * 0.05
                && sinceZero.TotalSeconds > 30)
            {
                findings.Add(Finding("zero-uitat", name, DefectSeverity.Warning, now,
                    $"Offset mare în CSV: {name} medie={mean:G5} (σ mic). Probabil Zero uitat înainte de Record."));
            }

            if (IsPressureUnit(unit) && Math.Abs(mean) > 0.2 && std < Math.Max(0.05, Math.Abs(mean) * 0.08))
            {
                findings.Add(Finding("presiune-fara-zero", name, DefectSeverity.Warning, now,
                    $"Presiune cu offset: {name} medie={mean:G4}. Zero la atmosferă înainte de măsurare."));
            }

            // Shunt-like step: large jump mid-record, then plateau
            if (DetectShuntStep(slice, out var stepAt, out var stepAmp))
            {
                findings.Add(Finding("shunt-gresit", name, DefectSeverity.Warning, now,
                    $"Treaptă tip șunt?: {name} salt ≈{stepAmp:G4} lângă eșantion {stepAt}. Nu confundați Shunt cu sarcină."));
            }

            // Drift: strong linear trend
            if (Math.Abs(slice[^1] - slice[0]) > Math.Max(std * 6, capacity * 0.02))
            {
                var d = slice[^1] - slice[0];
                if (std > 1e-9 && Math.Abs(d) / (take) * take > std * 3)
                {
                    findings.Add(Finding("drift", name, DefectSeverity.Info, now,
                        $"Drift în CSV: {name} Δ={d:G4} pe înregistrare. Verificați termic / Zero."));
                }
            }

            // Semnal plat offline (use mid/late window to skip Start/Zero settling)
            if (take >= FlatMinSamples && capacity > 0)
            {
                var win = Math.Min(take, Math.Max(FlatMinSamples, take / 3));
                var start = Math.Max(0, take - win);
                var tail = slice.AsSpan(start, win).ToArray();
                if (IsFlatSignal(tail, capacity, out var flatMean, out _)
                    && Math.Abs(flatMean) < capacity * 0.98)
                {
                    var awayFromZero = Math.Abs(flatMean) > Math.Max(capacity * FlatNonZeroMeanCapacityFrac, FlatNonZeroMeanFloor);
                    var peerEnergy = false;
                    for (var j = 0; j < session.Columns.Count; j++)
                    {
                        if (j == c) continue;
                        if (Energy(session.Columns[j]) > 1e-4) { peerEnergy = true; break; }
                    }

                    if (awayFromZero || peerEnergy)
                    {
                        findings.Add(Finding("semnal-plat", name, DefectSeverity.Warning, now,
                            $"Semnal plat pe {name} — verificați cablul / senzorul.",
                            channelIndex: c));
                    }
                }
            }
        }

        // Canal greșit offline: one column dead, another lively
        if (session.Columns.Count >= 2)
        {
            var energy = session.Columns.Select((col, i) => (i, e: Energy(col))).ToList();
            var dead = energy.Where(x => x.e < 1e-8).ToList();
            var live = energy.Where(x => x.e > 1e-4).ToList();
            if (dead.Count > 0 && live.Count > 0)
            {
                var d = dead[0].i;
                var l = live[0].i;
                var dn = d < session.ChannelNames.Count ? session.ChannelNames[d] : $"CH{d}";
                var ln = l < session.ChannelNames.Count ? session.ChannelNames[l] : $"CH{l}";
                findings.Add(Finding("canal-gresit", dn, DefectSeverity.Warning, now,
                    $"Canal greșit?: {dn} e plat, {ln} are semnal. Verificați pe ce CH ați aplicat senzorul / Rec."));
            }
        }

        return findings
            .GroupBy(f => f.DefectId + "|" + f.ChannelHint)
            .Select(g => g.First())
            .OrderByDescending(f => f.Severity)
            .Take(12)
            .ToList();
    }

    private static void Add(
        List<TypicalMistakeFinding> list,
        string id,
        ChannelSnapshot ch,
        DefectSeverity sev,
        DateTime now,
        string message)
    {
        var def = LabDefectCatalog.Find(id);
        list.Add(new TypicalMistakeFinding
        {
            DefectId = id,
            Title = def?.Title ?? id,
            Message = message,
            ChannelHint = ch.Name,
            ChannelIndex = ch.Index,
            Severity = sev,
            DetectedLocal = now
        });
    }

    private static TypicalMistakeFinding Finding(
        string id, string channel, DefectSeverity sev, DateTime now, string message,
        int channelIndex = -1)
    {
        var def = LabDefectCatalog.Find(id);
        return new TypicalMistakeFinding
        {
            DefectId = id,
            Title = def?.Title ?? id,
            Message = message,
            ChannelHint = channel,
            ChannelIndex = channelIndex,
            Severity = sev,
            DetectedLocal = now
        };
    }

    private static bool IsFlatSignal(double[] samples, double capacity, out double mean, out double range)
    {
        mean = 0;
        range = 0;
        if (samples.Length < 2) return false;
        mean = samples.Average();
        var min = samples[0];
        var max = samples[0];
        foreach (var v in samples)
        {
            if (v < min) min = v;
            if (v > max) max = v;
        }

        range = max - min;
        var std = StdDev(samples);
        var rangeLim = Math.Max(capacity * FlatRangeCapacityFrac, FlatAbsRangeFloor);
        var stdLim = Math.Max(capacity * FlatStdCapacityFrac, FlatAbsStdFloor);
        return range <= rangeLim && std <= stdLim;
    }

    private static string FormatChannelHint(ChannelSnapshot ch)
    {
        if (!string.IsNullOrWhiteSpace(ch.Name))
        {
            // Prefer CH0 / CH1 style when name already starts with CH
            if (ch.Name.StartsWith("CH", StringComparison.OrdinalIgnoreCase))
                return ch.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return ch.Name;
        }

        return $"CH{ch.Index}";
    }

    private static bool IsDigitalLike(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Contains("DI", StringComparison.OrdinalIgnoreCase);

    private static bool HasEnergy(ChannelSnapshot ch)
    {
        if (ch.RecentSamples is { Length: > 4 })
            return StdDev(ch.RecentSamples) > 1e-4 || Math.Abs(ch.RecentSamples.Max() - ch.RecentSamples.Min()) > 1e-3;
        return Math.Abs(ch.Value) > 1e-3;
    }

    private static double Energy(double[] col)
    {
        if (col.Length < 2) return 0;
        var mean = col.Average();
        double s = 0;
        foreach (var v in col) s += (v - mean) * (v - mean);
        return s / col.Length;
    }

    private static bool DetectShuntStep(double[] y, out int at, out double amp)
    {
        at = 0;
        amp = 0;
        if (y.Length < 30) return false;
        var best = 0.0;
        var bestI = 0;
        var win = Math.Max(5, y.Length / 20);
        for (var i = win; i < y.Length - win; i++)
        {
            var a = MeanRange(y, i - win, i);
            var b = MeanRange(y, i, i + win);
            var d = Math.Abs(b - a);
            if (d > best)
            {
                best = d;
                bestI = i;
            }
        }

        var overall = StdDev(y);
        if (best < Math.Max(overall * 6, 1e-3)) return false;
        // Plateau after step
        var afterStd = StdDev(y.AsSpan(bestI, Math.Min(win * 2, y.Length - bestI)).ToArray());
        if (afterStd > best * 0.25) return false;
        at = bestI;
        amp = best;
        return true;
    }

    private static double MeanRange(double[] y, int from, int to)
    {
        from = Math.Clamp(from, 0, y.Length);
        to = Math.Clamp(to, from + 1, y.Length);
        double s = 0;
        for (var i = from; i < to; i++) s += y[i];
        return s / (to - from);
    }

    private static int CountNear(double[] y, double target, double tol)
    {
        var n = 0;
        foreach (var v in y)
            if (Math.Abs(v - target) <= tol) n++;
        return n;
    }

    private static double StdDev(double[] y)
    {
        if (y.Length < 2) return 0;
        var m = y.Average();
        double s = 0;
        foreach (var v in y) s += (v - m) * (v - m);
        return Math.Sqrt(s / (y.Length - 1));
    }

    private static double GuessCapacity(ChannelSnapshot ch)
    {
        if (ch.Capacity > 0) return ch.Capacity;
        if (IsPressureUnit(ch.Unit)) return 250;
        if (IsForceLike(ch)) return 5000;
        return Math.Max(10, Math.Abs(ch.Value) * 2);
    }

    private static bool IsForceLike(ChannelSnapshot ch) =>
        IsForceLikeUnit(ch.Unit) ||
        ch.Name.Contains("Forț", StringComparison.OrdinalIgnoreCase) ||
        ch.Name.Contains("Force", StringComparison.OrdinalIgnoreCase) ||
        ch.Name.Contains("U2B", StringComparison.OrdinalIgnoreCase);

    private static bool IsForceLikeUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        return unit.Contains("N", StringComparison.OrdinalIgnoreCase)
               || unit.Contains("kN", StringComparison.OrdinalIgnoreCase)
               || unit.Contains("kg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPressureUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return false;
        return unit.Contains("bar", StringComparison.OrdinalIgnoreCase)
               || unit.Contains("Pa", StringComparison.OrdinalIgnoreCase)
               || unit.Contains("MPa", StringComparison.OrdinalIgnoreCase);
    }

    private static string GuessUnitFromName(string name)
    {
        if (name.Contains('[') && name.Contains(']'))
        {
            var a = name.IndexOf('[');
            var b = name.IndexOf(']');
            if (b > a) return name[(a + 1)..b];
        }
        return "";
    }
}
