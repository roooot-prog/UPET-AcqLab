using System.Globalization;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>Helpers for optional specimen (probă) dimensions + mass at Start experiment.</summary>
public static class SampleDimensions
{
    public static double ComputeAreaMm2(double lengthMm, double widthMm, double thicknessMm, double diameterMm)
    {
        if (diameterMm > 0)
            return Math.PI * diameterMm * diameterMm / 4.0;
        if (widthMm > 0 && thicknessMm > 0)
            return widthMm * thicknessMm;
        if (lengthMm > 0 && widthMm > 0)
            return lengthMm * widthMm;
        return 0;
    }

    public static string BuildSummary(
        double lengthMm,
        double widthMm,
        double thicknessMm,
        double diameterMm,
        double areaMm2 = 0,
        double massG = 0)
    {
        var parts = new List<string>();
        if (lengthMm > 0)
            parts.Add("Lungime=" + FormatMm(lengthMm));
        if (widthMm > 0)
            parts.Add("Lățime=" + FormatMm(widthMm));
        if (thicknessMm > 0)
            parts.Add("Grosime=" + FormatMm(thicknessMm));
        if (diameterMm > 0)
            parts.Add("Diametru=" + FormatMm(diameterMm));

        var area = areaMm2 > 0
            ? areaMm2
            : ComputeAreaMm2(lengthMm, widthMm, thicknessMm, diameterMm);
        if (area > 0)
            parts.Add("Aria=" + FormatArea(area) + " mm²");
        if (massG > 0)
            parts.Add("Greutate=" + FormatMass(massG) + " g");

        return parts.Count == 0 ? "" : string.Join(" · ", parts);
    }

    public static string BuildSummary(ProjectMeta meta)
        => string.IsNullOrWhiteSpace(meta.SampleDimensionsSummary)
            ? BuildSummary(
                meta.SampleLengthMm,
                meta.SampleWidthMm,
                meta.SampleThicknessMm,
                meta.SampleDiameterMm,
                meta.SampleAreaMm2,
                meta.SampleMassG)
            : meta.SampleDimensionsSummary.Trim();

    public static void ApplyComputedFields(ProjectMeta meta)
    {
        meta.SampleAreaMm2 = ResolveAreaMm2(meta);
        meta.SampleDimensionsSummary = BuildSummary(
            meta.SampleLengthMm,
            meta.SampleWidthMm,
            meta.SampleThicknessMm,
            meta.SampleDiameterMm,
            meta.SampleAreaMm2,
            meta.SampleMassG);
    }

    /// <summary>
    /// Meta rows for Excel / HTML / PDF / README. Optional fields: 0 → omit, or "—" when <paramref name="emptyAsDash"/>.
    /// </summary>
    public static IReadOnlyList<(string Label, string Value)> BuildReportMetaRows(
        ProjectMeta meta,
        bool emptyAsDash = false)
    {
        var rows = new List<(string, string)>();

        void AddOptional(string label, double value, Func<double, string> format)
        {
            if (value > 0)
                rows.Add((label, format(value)));
            else if (emptyAsDash)
                rows.Add((label, "—"));
        }

        AddOptional("Lungime [mm]", meta.SampleLengthMm, FormatMm);
        AddOptional("Lățime [mm]", meta.SampleWidthMm, FormatMm);
        AddOptional("Grosime [mm]", meta.SampleThicknessMm, FormatMm);
        AddOptional("Diametru [mm]", meta.SampleDiameterMm, FormatMm);

        var area = ResolveAreaMm2(meta);
        AddOptional("Aria secțiune [mm²]", area, a => FormatArea(a) + " mm²");
        AddOptional("Greutate [g]", meta.SampleMassG, g => FormatMass(g) + " g");

        var summary = string.IsNullOrWhiteSpace(meta.SampleDimensionsSummary)
            ? BuildSummary(
                meta.SampleLengthMm,
                meta.SampleWidthMm,
                meta.SampleThicknessMm,
                meta.SampleDiameterMm,
                area,
                meta.SampleMassG)
            : meta.SampleDimensionsSummary.Trim();
        if (!string.IsNullOrWhiteSpace(summary))
            rows.Add(("Dimensiuni / greutate", summary));
        else if (emptyAsDash)
            rows.Add(("Dimensiuni / greutate", "—"));

        return rows;
    }

    /// <summary>Stored area if set; otherwise compute from dimensions (no manual area entry).</summary>
    public static double ResolveAreaMm2(ProjectMeta meta)
        => meta.SampleAreaMm2 > 0
            ? meta.SampleAreaMm2
            : ComputeAreaMm2(
                meta.SampleLengthMm,
                meta.SampleWidthMm,
                meta.SampleThicknessMm,
                meta.SampleDiameterMm);

    private static string FormatMm(double mm)
        => mm.ToString("0.###", CultureInfo.InvariantCulture) + " mm";

    private static string FormatArea(double mm2)
        => mm2.ToString("0.###", CultureInfo.InvariantCulture);

    private static string FormatMass(double g)
        => g.ToString("0.###", CultureInfo.InvariantCulture);
}
