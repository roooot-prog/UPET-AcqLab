namespace Spider8DAQ.Core.Specimens;

/// <summary>One catalog or operator-defined specimen card (indicative lab defaults).</summary>
public sealed class SpecimenCard
{
    public string Id { get; set; } = "";
    public string NameRo { get; set; } = "";
    public List<string> Aliases { get; set; } = new();
    /// <summary>Metal / Roca / Sare / Beton / Personalizat.</summary>
    public string Class { get; set; } = SpecimenClasses.Metal;
    public string Shape { get; set; } = SpecimenShapes.Cilindru;
    public double L0Mm { get; set; }
    public double D0Mm { get; set; }
    /// <summary>Young modulus [GPa], order-of-magnitude.</summary>
    public double EGPa { get; set; }
    public double Nu { get; set; }
    /// <summary>Density [kg/m³]; 0 = unspecified.</summary>
    public double DensityKgM3 { get; set; }
    /// <summary>fy / UCS / f_c notes (indicative).</summary>
    public string StrengthNotes { get; set; } = "";
    public string FormulaPack { get; set; } = FormulaPacks.SteelMetal;
    public string StandardNote { get; set; } = "";
    public bool IsCustom { get; set; }
    /// <summary>Optional T, humidity, operator remarks (salt creep placeholders).</summary>
    public string Notes { get; set; } = "";

    public string FormulaPackLabel => FormulaPacks.LabelRo(FormulaPack);

    public string BuildSummaryLine()
    {
        var name = string.IsNullOrWhiteSpace(NameRo) ? Id : NameRo.Trim();
        var pack = FormulaPackLabel;
        return string.IsNullOrWhiteSpace(pack)
            ? "Epruvetă: " + name
            : "Epruvetă: " + name + " · pachet " + pack;
    }

    public string BuildCardSubtitle()
    {
        var bits = new List<string> { Shape };
        if (L0Mm > 0) bits.Add("L0 " + TrimNum(L0Mm) + " mm");
        if (D0Mm > 0) bits.Add("Ø " + TrimNum(D0Mm) + " mm");
        if (EGPa > 0) bits.Add("E ≈ " + TrimNum(EGPa) + " GPa");
        if (Nu > 0) bits.Add("ν ≈ " + TrimNum(Nu));
        bits.Add("pachet " + FormulaPackLabel);
        return string.Join(" · ", bits);
    }

    private static string TrimNum(double v)
    {
        if (Math.Abs(v - Math.Round(v)) < 1e-6)
            return Math.Round(v).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        return v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
