using System.Globalization;
using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Specimens;

/// <summary>
/// Apply / clear specimen identity on <see cref="ProjectMeta"/> and build report rows.
/// Gated to compression experiment types — leftover cards must not leak into tensometrie.
/// </summary>
public static class SpecimenIdentification
{
    public static bool Applies(ProjectMeta? meta)
        => meta is not null
           && ExperimentTypes.IsCompression(meta.ExperimentType)
           && HasIdentity(meta);

    /// <summary>True when a real card was chosen — leftover "Epruvetă: · pachet" does not count.</summary>
    public static bool HasIdentity(ProjectMeta? meta)
    {
        if (meta is null) return false;
        if (!string.IsNullOrWhiteSpace(meta.SpecimenId)) return true;
        if (!string.IsNullOrWhiteSpace(meta.SpecimenNameRo)) return true;
        return HasMeaningfulSpecimenText(meta.SpecimenSummary);
    }

    public static void ApplyCard(
        ProjectMeta meta,
        SpecimenCard card,
        double? l0Mm = null,
        double? d0Mm = null,
        double? eGPa = null,
        double? nu = null,
        string? extraNotes = null)
    {
        meta.SpecimenId = card.Id ?? "";
        meta.SpecimenNameRo = card.NameRo ?? "";
        meta.SpecimenClass = card.Class ?? "";
        meta.SpecimenFormulaPack = card.FormulaPack ?? "";
        meta.SpecimenFormulaPackLabel = FormulaPacks.LabelRo(card.FormulaPack);
        meta.SpecimenSummary = card.BuildSummaryLine();
        meta.SpecimenStandardNote = card.StandardNote ?? "";
        meta.SpecimenStrengthNotes = card.StrengthNotes ?? "";
        meta.SpecimenShape = string.IsNullOrWhiteSpace(card.Shape)
            ? SpecimenShapes.Cilindru
            : card.Shape;
        meta.SpecimenYoungGPa = eGPa is > 0 ? eGPa.Value : card.EGPa;
        meta.SpecimenPoissonNu = nu is > 0 ? nu.Value : card.Nu;
        meta.SpecimenDensityKgM3 = card.DensityKgM3;
        var notes = extraNotes ?? card.Notes;
        meta.SpecimenNotes = notes ?? "";

        var L = l0Mm is > 0 ? l0Mm.Value : card.L0Mm;
        var D = d0Mm is > 0 ? d0Mm.Value : card.D0Mm;
        if (L > 0) meta.SampleLengthMm = L;
        if (D > 0) meta.SampleDiameterMm = D;
        SampleDimensions.ApplyComputedFields(meta);
    }

    public static void ClearFromMeta(ProjectMeta meta)
    {
        meta.SpecimenId = "";
        meta.SpecimenNameRo = "";
        meta.SpecimenClass = "";
        meta.SpecimenFormulaPack = "";
        meta.SpecimenFormulaPackLabel = "";
        meta.SpecimenSummary = "";
        meta.SpecimenStandardNote = "";
        meta.SpecimenStrengthNotes = "";
        meta.SpecimenShape = "";
        meta.SpecimenYoungGPa = 0;
        meta.SpecimenPoissonNu = 0;
        meta.SpecimenDensityKgM3 = 0;
        meta.SpecimenNotes = "";
    }

    /// <summary>Copy specimen identity fields (caller gates on compression type).</summary>
    public static void CopyTo(ProjectMeta dest, ProjectMeta src)
    {
        dest.SpecimenId = src.SpecimenId ?? "";
        dest.SpecimenNameRo = src.SpecimenNameRo ?? "";
        dest.SpecimenClass = src.SpecimenClass ?? "";
        dest.SpecimenFormulaPack = src.SpecimenFormulaPack ?? "";
        dest.SpecimenFormulaPackLabel = src.SpecimenFormulaPackLabel ?? "";
        dest.SpecimenSummary = src.SpecimenSummary ?? "";
        dest.SpecimenStandardNote = src.SpecimenStandardNote ?? "";
        dest.SpecimenStrengthNotes = src.SpecimenStrengthNotes ?? "";
        dest.SpecimenShape = src.SpecimenShape ?? "";
        dest.SpecimenYoungGPa = src.SpecimenYoungGPa;
        dest.SpecimenPoissonNu = src.SpecimenPoissonNu;
        dest.SpecimenDensityKgM3 = src.SpecimenDensityKgM3;
        dest.SpecimenNotes = src.SpecimenNotes ?? "";
    }

    public static IReadOnlyList<(string Label, string Value)> BuildReportMetaRows(
        ProjectMeta meta,
        bool emptyAsDash = false)
    {
        if (!Applies(meta))
            return Array.Empty<(string, string)>();

        var rows = new List<(string, string)>();
        var value = string.IsNullOrWhiteSpace(meta.SpecimenSummary)
            ? BuildSummary(meta)
            : meta.SpecimenSummary.Trim();
        if (value.StartsWith("Epruvetă:", StringComparison.OrdinalIgnoreCase))
            value = value["Epruvetă:".Length..].Trim();
        if (HasMeaningfulSpecimenText(value))
            rows.Add(("Epruvetă", value));

        if (!string.IsNullOrWhiteSpace(meta.SpecimenClass))
            rows.Add(("Clasă epruvetă", meta.SpecimenClass.Trim()));

        var packLabel = string.IsNullOrWhiteSpace(meta.SpecimenFormulaPackLabel)
            ? FormulaPacks.LabelRo(meta.SpecimenFormulaPack)
            : meta.SpecimenFormulaPackLabel.Trim();
        if (!string.IsNullOrWhiteSpace(packLabel))
            rows.Add(("Pachet formule", packLabel));

        var meaning = FormulaPacks.ReportMeaningRo(meta.SpecimenFormulaPack);
        if (!string.IsNullOrWhiteSpace(meaning))
            rows.Add(("Interpretare (pachet)", meaning));

        if (meta.SpecimenYoungGPa > 0)
            rows.Add(("E [GPa] (indicativ)", FormatNum(meta.SpecimenYoungGPa)));
        if (meta.SpecimenPoissonNu > 0)
            rows.Add(("ν (indicativ)", FormatNum(meta.SpecimenPoissonNu)));
        if (meta.SpecimenDensityKgM3 > 0)
            rows.Add(("Densitate [kg/m³] (indicativ)",
                meta.SpecimenDensityKgM3.ToString("0", CultureInfo.InvariantCulture)));
        if (!string.IsNullOrWhiteSpace(meta.SpecimenStrengthNotes))
            rows.Add(("Rezistență (indicativ)", meta.SpecimenStrengthNotes.Trim()));
        if (!string.IsNullOrWhiteSpace(meta.SpecimenStandardNote))
            rows.Add(("Standard / notă epruvetă", meta.SpecimenStandardNote.Trim()));
        if (!string.IsNullOrWhiteSpace(meta.SpecimenNotes))
            rows.Add(("Note epruvetă (T / umiditate)", meta.SpecimenNotes.Trim()));

        if (rows.Count == 0)
            return rows;

        rows.Add(("Valori epruvetă", SpecimenCatalog.IndicativeDisclaimerRo));
        return rows;
    }

    public static IEnumerable<string> BuildCsvCommentLines(ProjectMeta meta)
    {
        if (!Applies(meta)) yield break;
        if (!string.IsNullOrWhiteSpace(meta.SpecimenId))
            yield return "SpecimenId=" + meta.SpecimenId.Trim();
        if (!string.IsNullOrWhiteSpace(meta.SpecimenNameRo))
            yield return "SpecimenName=" + meta.SpecimenNameRo.Trim();
        if (!string.IsNullOrWhiteSpace(meta.SpecimenClass))
            yield return "SpecimenClass=" + meta.SpecimenClass.Trim();
        if (!string.IsNullOrWhiteSpace(meta.SpecimenFormulaPack))
            yield return "FormulaPack=" + meta.SpecimenFormulaPack.Trim();
        if (HasMeaningfulSpecimenText(meta.SpecimenSummary))
            yield return "SpecimenSummary=" + meta.SpecimenSummary.Trim();
        if (meta.SpecimenYoungGPa > 0)
            yield return "SpecimenYoungGPa=" +
                         meta.SpecimenYoungGPa.ToString("G17", CultureInfo.InvariantCulture);
        if (meta.SpecimenPoissonNu > 0)
            yield return "SpecimenPoissonNu=" +
                         meta.SpecimenPoissonNu.ToString("G17", CultureInfo.InvariantCulture);
        if (meta.SampleLengthMm > 0)
            yield return "SampleLengthMm=" +
                         meta.SampleLengthMm.ToString("G17", CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(meta.SpecimenNotes))
            yield return "SpecimenNotes=" + meta.SpecimenNotes.Trim();
    }

    public static bool TryApplyCsvKey(ProjectMeta meta, string key, string val)
    {
        switch (key)
        {
            case "SpecimenId":
                meta.SpecimenId = val; return true;
            case "SpecimenName":
                meta.SpecimenNameRo = val; return true;
            case "SpecimenClass":
                meta.SpecimenClass = val; return true;
            case "FormulaPack":
                meta.SpecimenFormulaPack = val;
                meta.SpecimenFormulaPackLabel = FormulaPacks.LabelRo(val);
                return true;
            case "SpecimenSummary":
                meta.SpecimenSummary = val; return true;
            case "SpecimenYoungGPa" when double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var e):
                meta.SpecimenYoungGPa = e; return true;
            case "SpecimenPoissonNu" when double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var nu):
                meta.SpecimenPoissonNu = nu; return true;
            case "SpecimenNotes":
                meta.SpecimenNotes = val; return true;
            case "SampleLengthMm" when double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out var l)
                                       && meta.SampleLengthMm <= 0:
                meta.SampleLengthMm = l; return true;
            default:
                return false;
        }
    }

    public static string BuildSummary(ProjectMeta meta)
    {
        var name = string.IsNullOrWhiteSpace(meta.SpecimenNameRo) ? meta.SpecimenId : meta.SpecimenNameRo;
        var pack = string.IsNullOrWhiteSpace(meta.SpecimenFormulaPackLabel)
            ? FormulaPacks.LabelRo(meta.SpecimenFormulaPack)
            : meta.SpecimenFormulaPackLabel;
        if (!HasMeaningfulSpecimenText(name)) return "";
        return string.IsNullOrWhiteSpace(pack)
            ? "Epruvetă: " + name.Trim()
            : "Epruvetă: " + name.Trim() + " · pachet " + pack.Trim();
    }

    /// <summary>False for empty / "· pachet" leftovers — reports must omit those lines.</summary>
    public static bool HasMeaningfulSpecimenText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.StartsWith("Epruvetă:", StringComparison.OrdinalIgnoreCase))
            t = t["Epruvetă:".Length..].Trim();
        t = t.Trim('·', '-', '—', ' ', '\t');
        if (t.StartsWith("pachet", StringComparison.OrdinalIgnoreCase))
            t = t["pachet".Length..].Trim().Trim('·', ' ', '-');
        return t.Length > 0;
    }

    private static string FormatNum(double v)
    {
        if (Math.Abs(v - Math.Round(v)) < 1e-6)
            return Math.Round(v).ToString("0", CultureInfo.InvariantCulture);
        return v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
