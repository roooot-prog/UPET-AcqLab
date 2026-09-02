using System.Globalization;
using Spider8DAQ.Core.Projects;

namespace Spider8DAQ.Core.Export;

/// <summary>Optional Poisson ν + elastic recovery rows for Excel / HTML / PDF / README.</summary>
public static class StrainAnalysisIndicators
{
    public static void ApplyFromSummaries(ProjectMeta meta)
    {
        // Keep summaries as-is; callers set ApparentPoissonNu / summaries when computed.
        if (string.IsNullOrWhiteSpace(meta.ApparentPoissonSummary) &&
            !double.IsNaN(meta.ApparentPoissonNu) &&
            !double.IsInfinity(meta.ApparentPoissonNu) &&
            meta.ApparentPoissonNu != 0)
        {
            meta.ApparentPoissonSummary =
                "ν_ap≈" + meta.ApparentPoissonNu.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Meta rows for reports. Empty / unset → omit, or "—" when <paramref name="emptyAsDash"/>.
    /// </summary>
    public static IReadOnlyList<(string Label, string Value)> BuildReportMetaRows(
        ProjectMeta meta,
        bool emptyAsDash = false)
    {
        var rows = new List<(string, string)>();

        var hasNuValue = !double.IsNaN(meta.ApparentPoissonNu) && !double.IsInfinity(meta.ApparentPoissonNu);
        var hasNuSummary = !string.IsNullOrWhiteSpace(meta.ApparentPoissonSummary);

        if (hasNuValue || hasNuSummary)
        {
            if (hasNuValue)
                rows.Add(("ν aparent (Poisson)", meta.ApparentPoissonNu.ToString("0.####", CultureInfo.InvariantCulture)));
            if (hasNuSummary)
                rows.Add(("Poisson — detalii", meta.ApparentPoissonSummary.Trim()));
        }
        else if (emptyAsDash)
        {
            rows.Add(("ν aparent (Poisson)", "—"));
        }

        if (!string.IsNullOrWhiteSpace(meta.ElasticRecoverySummary))
            rows.Add(("Recuperare elastică / ε_perm", meta.ElasticRecoverySummary.Trim()));
        else if (emptyAsDash)
            rows.Add(("Recuperare elastică / ε_perm", "—"));

        return rows;
    }
}
