using System.Globalization;
using System.Text;
using Spider8DAQ.Core.Analysis;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>
/// Shared industrial-lab report sections (validity zone, Fmax/σ, fingerprint, quality, signatures, disclaimer)
/// used by Excel / HTML / PDF / lab package / dedicated industrial PDF.
/// </summary>
public static class IndustrialReportSections
{
    public const string DisclaimerRo =
        "Acest document este un raport de măsurătoare generat de UPET AcqLab — aplicație industrială de " +
        "achiziție și analiză pentru laborator metrologic (Universitatea din Petroșani). Rezultatele, " +
        "trasabilitatea și interpretarea se gestionează conform sistemului calității și procedurilor " +
        "laboratorului emitent.";

    public sealed class Options
    {
        public string? FingerprintStatusLabel { get; init; }
        /// <summary>Sealed CSV path for fingerprint verify (preferred over session/.upet payload).</summary>
        public string? CsvPath { get; init; }
        public string? PolarityOrDefectWarning { get; init; }
        public int? CursorA { get; init; }
        public int? CursorB { get; init; }
    }

    public sealed class Bundle
    {
        public required string ValidityText { get; init; }
        public required bool ValidityWindow { get; init; }
        public required int IndexA { get; init; }
        public required int IndexB { get; init; }
        public required OfflineSession StatsSession { get; init; }
        public required IReadOnlyList<(string Label, string Value)> IndicatorRows { get; init; }
        public required IReadOnlyList<string> QualityLines { get; init; }
    }

    public static Bundle Build(OfflineSession session, ProjectMeta meta, Options? options = null)
    {
        options ??= new Options();
        SampleDimensions.ApplyComputedFields(meta);
        StrainAnalysisIndicators.ApplyFromSummaries(meta);

        var n = Math.Max(0, session.Timestamps.Count);
        var rawA = options.CursorA ?? session.CursorA;
        var rawB = options.CursorB ?? session.CursorB;
        var a = n == 0 ? 0 : Math.Clamp(Math.Min(rawA, rawB), 0, n - 1);
        var b = n == 0 ? 0 : Math.Clamp(Math.Max(rawA, rawB), 0, n - 1);
        var validityWindow = n > 1 && a != b;
        var statsSession = validityWindow ? session.ExportRegion(a, b + 1) : session;

        string validityText;
        if (!validityWindow || n == 0)
        {
            validityText = "Zonă predare / date oficiale: întreaga înregistrare.";
        }
        else
        {
            var t0 = session.Timestamps[0];
            var tA = (session.Timestamps[a] - t0).TotalSeconds;
            var tB = (session.Timestamps[b] - t0).TotalSeconds;
            validityText =
                $"Zonă predare / date oficiale: index {a}–{b} " +
                $"(t={tA.ToString("0.###", CultureInfo.InvariantCulture)}–" +
                $"{tB.ToString("0.###", CultureInfo.InvariantCulture)} s).";
        }

        // Fingerprint verify needs the full session payload, not the validity-window subset.
        var indicatorRows = BuildIndicatorRows(session, statsSession, meta, options);
        var quality = BuildQualityLines(session, meta, options, validityWindow);

        return new Bundle
        {
            ValidityText = validityText,
            ValidityWindow = validityWindow,
            IndexA = a,
            IndexB = b,
            StatsSession = statsSession,
            IndicatorRows = indicatorRows,
            QualityLines = quality
        };
    }

    /// <summary>Flat meta rows for Excel/HTML/PDF/README (omit empty where noted).</summary>
    public static IReadOnlyList<(string Label, string Value)> BuildCommonMetaRows(
        OfflineSession session,
        ProjectMeta meta,
        Options? options = null,
        bool emptyAsDash = true)
    {
        var bundle = Build(session, meta, options);
        var rows = new List<(string, string)>
        {
            ("Zonă predare / date oficiale", bundle.ValidityText)
        };

        foreach (var row in bundle.IndicatorRows)
            rows.Add(row);

        var qi = 0;
        foreach (var line in bundle.QualityLines)
        {
            qi++;
            rows.Add((qi == 1 ? "Calitate / limitări" : "Calitate / limitări (cont.)", line));
        }

        rows.Add(("Semnătură operator",
            string.IsNullOrWhiteSpace(meta.Operator) ? "________________" : meta.Operator.Trim() + "  ________________"));
        rows.Add(("Semnătură expert", "________________"));
        rows.Add(("Disclaimer", DisclaimerRo));
        return rows;
    }

    public static void AppendPlainTextSections(StringBuilder sb, Bundle bundle, ProjectMeta meta)
    {
        sb.AppendLine();
        sb.AppendLine("Zonă predare / calitate (stil industrial)");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine(bundle.ValidityText);
        foreach (var (label, value) in bundle.IndicatorRows)
            sb.AppendLine($"{label}: {value}");
        sb.AppendLine();
        sb.AppendLine("Calitate / limitări:");
        foreach (var line in bundle.QualityLines)
            sb.AppendLine("  • " + line);
        sb.AppendLine();
        sb.AppendLine("Semnături");
        sb.AppendLine("---------");
        sb.AppendLine("  Operator: " + (string.IsNullOrWhiteSpace(meta.Operator) ? "________________" : meta.Operator.Trim() + "  ________________"));
        sb.AppendLine("  Semnătură expert: ________________");
        sb.AppendLine();
        sb.AppendLine("Disclaimer");
        sb.AppendLine("----------");
        sb.AppendLine(DisclaimerRo);
    }

    public static string BuildHtmlSections(Bundle bundle, ProjectMeta meta)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<h2 class='sec'>Zonă predare / date oficiale</h2>");
        sb.AppendLine($"<p>{Esc(bundle.ValidityText)}</p>");

        if (bundle.IndicatorRows.Count > 0)
        {
            sb.AppendLine("<h2 class='sec'>Indicatori industriali (best-effort)</h2>");
            sb.AppendLine("<table class='meta'>");
            foreach (var (label, value) in bundle.IndicatorRows)
            {
                if (IsFingerprintStatusRow(label))
                {
                    var hex = MeasurementFingerprint.StatusColorHex(value);
                    sb.AppendLine(
                        $"<tr><td>{Esc(label)}</td><td>" +
                        $"<span class='fp-badge' style='color:{hex};font-weight:700'>{Esc(value)}</span>" +
                        "</td></tr>");
                }
                else
                {
                    sb.AppendLine($"<tr><td>{Esc(label)}</td><td>{Esc(value)}</td></tr>");
                }
            }
            sb.AppendLine("</table>");
        }

        sb.AppendLine("<h2 class='sec'>Calitate / limitări</h2>");
        sb.AppendLine("<ul>");
        foreach (var line in bundle.QualityLines)
            sb.AppendLine($"<li>{Esc(line)}</li>");
        sb.AppendLine("</ul>");

        sb.AppendLine("<h2 class='sec'>Semnături</h2>");
        sb.AppendLine("<table class='meta'>");
        sb.AppendLine($"<tr><td>Operator</td><td>{Esc(string.IsNullOrWhiteSpace(meta.Operator) ? "________________" : meta.Operator.Trim() + "  ________________")}</td></tr>");
        sb.AppendLine("<tr><td>Semnătură expert</td><td>________________</td></tr>");
        sb.AppendLine("</table>");

        sb.AppendLine("<h2 class='sec'>Disclaimer</h2>");
        sb.AppendLine($"<p class='sub'>{Esc(DisclaimerRo)}</p>");
        return sb.ToString();
    }

    private static List<(string Label, string Value)> BuildIndicatorRows(
        OfflineSession fullSession,
        OfflineSession statsSession,
        ProjectMeta meta,
        Options options)
    {
        var rows = new List<(string, string)>();

        // Strain ν / recuperare remain in exporters via StrainAnalysisIndicators;
        // here only industrial extras (Fmax/σ + fingerprint).

        var force = TryDeriveForceStress(statsSession, meta);
        if (force is not null)
        {
            rows.Add(("Fmax (canal forță)", force.FmaxText));
            if (!string.IsNullOrWhiteSpace(force.SigmaText))
                rows.Add(("σ derivat", force.SigmaText));
            if (!string.IsNullOrWhiteSpace(force.Assumption))
                rows.Add(("Ipoteză Fmax/σ", force.Assumption));
        }

        var csvPath = MeasurementFingerprint.ResolveExistingCsvPath(
            options.CsvPath, fullSession.SourcePath);
        var status = MeasurementFingerprint.ResolveReportStatusLabel(
            fullSession, meta, options.FingerprintStatusLabel, csvPath);

        if (!string.IsNullOrWhiteSpace(meta.MeasurementFingerprint))
        {
            rows.Add(("Amprentă măsurare", meta.MeasurementFingerprint.Trim()));
            rows.Add(("Stare amprentă", status));
        }
        else
        {
            rows.Add(("Amprentă măsurare", "— (nesigilată)"));
            rows.Add(("Stare amprentă", status)); // Lipsă
        }

        return rows;
    }

    public static bool IsFingerprintStatusRow(string label)
        => label.Contains("Stare amprent", StringComparison.OrdinalIgnoreCase);

    private static List<string> BuildQualityLines(
        OfflineSession session,
        ProjectMeta meta,
        Options options,
        bool validityWindow)
    {
        var lines = new List<string>();
        var fsSet = meta.SampleRateHz > 0 ? meta.SampleRateHz : 0;
        var fsEf = SamplingRateInfo.EstimateEffectiveHz(
            session.Timestamps, fsSet > 0 ? fsSet : 50);
        lines.Add(fsSet > 0
            ? $"fs setată={fsSet.ToString(CultureInfo.InvariantCulture)} Hz; fs efectivă≈{SamplingRateInfo.FormatHz(fsEf)} Hz."
            : $"fs efectivă≈{SamplingRateInfo.FormatHz(fsEf)} Hz (fs setată nespecificată).");

        lines.Add(validityWindow
            ? "Statisticile pe zonă CursorA–CursorB (date oficiale), când e aplicabil."
            : "Statisticile pe întreaga înregistrare (cursoare A/B nedefinite sau egale).");

        if (!string.IsNullOrWhiteSpace(options.PolarityOrDefectWarning))
            lines.Add("Avertisment polaritate/defect: " + options.PolarityOrDefectWarning.Trim());
        else if (!string.IsNullOrWhiteSpace(meta.DeviceEstHint) && meta.DeviceEstHint != "EST: —")
            lines.Add("Stare EST: " + meta.DeviceEstHint.Trim());

        if (Spider8DAQ.Core.Specimens.SpecimenIdentification.Applies(meta))
        {
            var pack = Spider8DAQ.Core.Specimens.FormulaPacks.ReportMeaningRo(meta.SpecimenFormulaPack);
            if (!string.IsNullOrWhiteSpace(pack))
                lines.Add("Pachet epruvetă: " + pack);
            lines.Add(Spider8DAQ.Core.Specimens.SpecimenCatalog.IndicativeDisclaimerRo);
        }

        lines.Add("Trasabilitate și interpretare conform sistemului calității / procedurilor laboratorului emitent.");
        lines.Add("Indicatorii derivați (σ, ν, recuperare) sunt best-effort din datele disponibile.");
        return lines;
    }

    private sealed class ForceStressResult
    {
        public string FmaxText { get; init; } = "";
        public string? SigmaText { get; init; }
        public string? Assumption { get; init; }
    }

    private static ForceStressResult? TryDeriveForceStress(OfflineSession session, ProjectMeta meta)
    {
        var idx = FindForceChannelIndex(session, meta);
        if (idx < 0 || idx >= session.Columns.Count) return null;

        var name = session.ChannelNames[idx];
        var unit = GuessUnit(name, meta, idx);
        var stats = idx < session.Stats.Count
            ? session.Stats[idx]
            : SignalAnalysis.ComputeStats(name, session.Columns[idx]);
        if (stats.Count == 0 || double.IsNaN(stats.Max)) return null;

        var fPeak = Math.Abs(stats.Max) >= Math.Abs(stats.Min) ? stats.Max : stats.Min;
        var fAbs = Math.Abs(fPeak);
        var fN = ConvertForceToNewtons(fAbs, unit, out var unitNote);
        var fmaxText =
            $"{Fmt(fPeak)} {unit}".Trim() +
            (string.IsNullOrWhiteSpace(unitNote) ? "" : $"  (~{Fmt(fN)} N)");

        var area = SampleDimensions.ResolveAreaMm2(meta);
        string? sigmaText = null;
        var assumption =
            "Fmax = extrem pe canalul de forță în zona oficială; polaritate neatribuită automat.";
        if (!string.IsNullOrWhiteSpace(unitNote))
            assumption += " " + unitNote;

        if (area > 0 && fN > 0)
        {
            var sigmaMpa = fN / area;
            sigmaText =
                $"{Fmt(sigmaMpa)} MPa  (σ ≈ |F|_N / A_mm²; A={Fmt(area)} mm²)";
            assumption +=
                " σ = |F| convertit în N împărțit la aria secțiunii [mm²] (echivalent MPa).";
            if (Spider8DAQ.Core.Specimens.SpecimenIdentification.Applies(meta))
            {
                var pack = Spider8DAQ.Core.Specimens.FormulaPacks.ReportMeaningRo(meta.SpecimenFormulaPack);
                if (!string.IsNullOrWhiteSpace(pack))
                    assumption += " " + pack;
            }
        }
        else if (area <= 0)
        {
            assumption += " Aria probei nestată — σ necalculat.";
        }

        return new ForceStressResult
        {
            FmaxText = fmaxText + $" [{Trunc(name, 28)}]",
            SigmaText = sigmaText,
            Assumption = assumption
        };
    }

    private static int FindForceChannelIndex(OfflineSession session, ProjectMeta meta)
    {
        for (var i = 0; i < session.ChannelNames.Count; i++)
        {
            var name = session.ChannelNames[i];
            var unit = GuessUnit(name, meta, i);
            var cat = meta.Channels?.FirstOrDefault(c =>
                c.Index == i ||
                string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.SensorCategory;
            if (IsForceLike(name, unit, cat))
                return i;
        }
        return -1;
    }

    private static string GuessUnit(string channelName, ProjectMeta meta, int index)
    {
        var fromMeta = meta.Channels?.FirstOrDefault(c =>
            c.Index == index ||
            string.Equals(c.Name, channelName, StringComparison.OrdinalIgnoreCase))?.Unit;
        if (!string.IsNullOrWhiteSpace(fromMeta)) return fromMeta!;
        var a = channelName.IndexOf('[');
        var b = channelName.IndexOf(']');
        if (a >= 0 && b > a) return channelName[(a + 1)..b].Trim();
        return "";
    }

    private static bool IsForceLike(string name, string? unit, string? category)
    {
        if (!string.IsNullOrWhiteSpace(category) &&
            (category.Contains("For", StringComparison.OrdinalIgnoreCase) ||
             category.Contains("Force", StringComparison.OrdinalIgnoreCase) ||
             category.Contains("Load", StringComparison.OrdinalIgnoreCase)))
            return true;
        if (name.Contains("Force", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Fort", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("U2B", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Load", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(unit)) return false;
        var u = unit.Trim();
        return u.Equals("N", StringComparison.OrdinalIgnoreCase)
               || u.Equals("kN", StringComparison.OrdinalIgnoreCase)
               || u.Contains("kN", StringComparison.OrdinalIgnoreCase)
               || (u.Contains('N') && !u.Contains("mm", StringComparison.OrdinalIgnoreCase)
                   && !u.Contains("nm", StringComparison.OrdinalIgnoreCase));
    }

    private static double ConvertForceToNewtons(double value, string? unit, out string note)
    {
        note = "";
        if (string.IsNullOrWhiteSpace(unit))
        {
            note = "Unitate forță necunoscută — tratat ca N.";
            return value;
        }
        var u = unit.Trim();
        if (u.Contains("kN", StringComparison.OrdinalIgnoreCase))
        {
            note = "Conversie: 1 kN = 1000 N.";
            return value * 1000.0;
        }
        if (u.Contains("kg", StringComparison.OrdinalIgnoreCase) &&
            !u.Contains("kN", StringComparison.OrdinalIgnoreCase))
        {
            note = "Conversie: kgf ≈ kg × 9.80665 N (aproximativ).";
            return value * 9.80665;
        }
        if (u.Contains('N'))
        {
            note = "Unitate tratată ca newtoni (N).";
            return value;
        }
        note = "Unitate forță nerecunoscută — tratat ca N.";
        return value;
    }

    private static string Fmt(double v)
        => v.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Trunc(string s, int max)
        => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string Esc(string? s)
        => System.Net.WebUtility.HtmlEncode(s ?? "");
}
