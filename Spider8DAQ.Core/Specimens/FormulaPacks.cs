namespace Spider8DAQ.Core.Specimens;

/// <summary>
/// Analysis/report packs attached to a specimen card.
/// Geometry/colors of contour plots are independent of the pack.
/// </summary>
public static class FormulaPacks
{
    public const string SteelMetal = "SteelMetal";
    public const string IsrmUcs = "IsrmUcs";
    public const string SaltCreep = "SaltCreep";
    public const string En12390 = "En12390";

    public const string LabelSteelMetal = "Oțel / metal";
    public const string LabelIsrmUcs = "ISRM / UCS";
    public const string LabelSaltCreep = "Sare (fluaj)";
    public const string LabelEn12390 = "EN 12390";

    public static string LabelRo(string? packId) => packId switch
    {
        SteelMetal => LabelSteelMetal,
        IsrmUcs => LabelIsrmUcs,
        SaltCreep => LabelSaltCreep,
        En12390 => LabelEn12390,
        _ => string.IsNullOrWhiteSpace(packId) ? "" : packId.Trim()
    };

    public static string DefaultForClass(string? specimenClass) => specimenClass switch
    {
        SpecimenClasses.Metal => SteelMetal,
        SpecimenClasses.Roca => IsrmUcs,
        SpecimenClasses.Sare => SaltCreep,
        SpecimenClasses.Beton => En12390,
        _ => SteelMetal
    };

    /// <summary>What the pack means in analysis / report (Romanian, no fake solvers).</summary>
    public static string ReportMeaningRo(string? packId) => packId switch
    {
        SteelMetal =>
            "σ = F/A, ε din cursă/L0, bombare și curba σ–ε (matematica existentă de compresiune cilindru). " +
            "Interpretare: oțel / metal — plasticitate, nu UCS de rocă.",
        IsrmUcs =>
            "UCS = Fmax/A, raport L/D, comportare casantă. Se folosesc F, cursa și u_i măsurate; " +
            "interpretare: rezistență uniaxială de rocă (ISRM), nu plasticitate metalică.",
        SaltCreep =>
            "σ–ε / F–cursă măsurate pe epruvetă de sare (fluaj). " +
            "Ajustarea Norton (creep fit) nu este inclusă — notați T și umiditatea dacă sunt relevante.",
        En12390 =>
            "Compresiune măsurată interpretată ca f_c (EN 12390). " +
            "Notați dacă proba e cub sau cilindru; valorile de catalog sunt indicative, nu clasa certificată.",
        _ => ""
    };

    public static string AdvisorTextRo(string? packId) => packId switch
    {
        SteelMetal =>
            "Pachet Oțel / metal: σ=F/A, ε, bombare, σ–ε. Conturul geometric nu se schimbă după material.",
        IsrmUcs =>
            "Pachet ISRM / UCS: interpretați Fmax/A ca UCS de rocă (L/D, casant), nu ca curgere de oțel.",
        SaltCreep =>
            "Pachet Sare (fluaj): folosiți σ–ε / F–cursă. Fit Norton nu e în aplicație — câmpuri notă T / umiditate.",
        En12390 =>
            "Pachet EN 12390: f_c cub/cilindru. Confirmați geometria probei (cub 150 mm vs cilindru Ø150×300).",
        _ => ""
    };

    public static bool IsKnown(string? packId) =>
        packId is SteelMetal or IsrmUcs or SaltCreep or En12390;
}

public static class SpecimenClasses
{
    public const string Metal = "Metal";
    public const string Roca = "Roca";
    public const string Sare = "Sare";
    public const string Beton = "Beton";
    public const string Personalizat = "Personalizat";

    public static IReadOnlyList<string> All { get; } =
        [Metal, Roca, Sare, Beton, Personalizat];
}

public static class SpecimenShapes
{
    public const string Cilindru = "cilindru";
}
