namespace Spider8DAQ.Core.Specimens;

/// <summary>
/// Built-in specimen cards (~50). Values are typical literature / order-of-magnitude
/// lab defaults the operator must confirm — never certified.
/// </summary>
public static class SpecimenCatalog
{
    public const int CatalogVersion = 1;

    public const string IndicativeDisclaimerRo =
        "Valori indicative (literatură / ordin de mărime). Confirmați pe epruveta măsurată — nu sunt valori certificate.";

    public static IReadOnlyList<SpecimenCard> Build() => Cards;

    private static readonly SpecimenCard[] Cards = BuildCards();

    private static SpecimenCard[] BuildCards()
    {
        var list = new List<SpecimenCard>(56);

        // --- Metale (Oțel / metal) — cilindru lab L/D ≈ 2 ---
        Metal(list, "metal-s235", "S235", 210, 0.30, 7850,
            "fy ≈ 235 MPa (EN 10025)", "EN 10025 — valori indicative",
            "S235JR", "otel", "oțel", "steel");
        Metal(list, "metal-s275", "S275", 210, 0.30, 7850,
            "fy ≈ 275 MPa", "EN 10025 — valori indicative",
            "S275JR", "otel");
        Metal(list, "metal-s355", "S355", 210, 0.30, 7850,
            "fy ≈ 355 MPa", "EN 10025 — valori indicative",
            "S355JR", "S355J2", "otel", "oțel", "steel");
        Metal(list, "metal-s460", "S460", 210, 0.30, 7850,
            "fy ≈ 460 MPa", "EN 10025 — valori indicative",
            "S460NL");
        Metal(list, "metal-s690", "S690", 210, 0.30, 7850,
            "fy ≈ 690 MPa", "EN 10025 — valori indicative",
            "S690QL");
        Metal(list, "metal-c45", "C45", 210, 0.29, 7850,
            "fy ≈ 370 MPa · Rm ≈ 600 MPa", "EN 10083 — valori indicative",
            "1.0503", "AISI 1045");
        Metal(list, "metal-c60", "C60", 210, 0.29, 7850,
            "fy ≈ 480 MPa · Rm ≈ 750 MPa", "EN 10083 — valori indicative",
            "1.0601");
        Metal(list, "metal-42crmo4", "42CrMo4", 210, 0.29, 7850,
            "fy ≈ 650–900 MPa (tratat)", "EN 10083 — valori indicative",
            "AISI 4140", "1.7225");
        Metal(list, "metal-aisi304", "AISI 304", 193, 0.29, 7900,
            "fy ≈ 210 MPa (recoacere)", "EN 1.4301 — valori indicative",
            "304", "X5CrNi18-10", "inox");
        Metal(list, "metal-aisi316", "AISI 316", 193, 0.30, 8000,
            "fy ≈ 220 MPa (recoacere)", "EN 1.4401 — valori indicative",
            "316", "X5CrNiMo17-12-2", "inox");
        Metal(list, "metal-en-gjl-250", "EN-GJL-250", 110, 0.26, 7200,
            "Rm ≈ 250 MPa (fontă cenușie)", "EN 1561 — E mai mic decât oțelul",
            "GG25", "fonta", "fontă");
        Metal(list, "metal-en-gjs-400", "EN-GJS-400-15", 170, 0.28, 7100,
            "Rm ≈ 400 MPa (fontă ductilă)", "EN 1563 — valori indicative",
            "GGG40", "fonta ductila");
        Metal(list, "metal-aw6060", "EN AW-6060", 70, 0.33, 2700,
            "fy ≈ 150 MPa (T6, ordin)", "EN 755 — aluminiu",
            "6060", "AlMgSi", "aluminiu", "aluminum");
        Metal(list, "metal-aw6082", "EN AW-6082", 70, 0.33, 2700,
            "fy ≈ 260 MPa (T6, ordin)", "EN 755 — aluminiu",
            "6082", "aluminiu");
        Metal(list, "metal-aw7075", "EN AW-7075", 72, 0.33, 2800,
            "fy ≈ 500 MPa (T6, ordin)", "EN 755 — aluminiu",
            "7075", "aluminiu");
        Metal(list, "metal-aw2024", "EN AW-2024", 73, 0.33, 2780,
            "fy ≈ 350 MPa (T3/T4, ordin)", "EN 755 — aluminiu",
            "2024", "dural");
        Metal(list, "metal-cu-etp", "Cu-ETP", 115, 0.34, 8900,
            "fy ≈ 70 MPa (recopt, ordin)", "EN 13601 — cupru electrolitic",
            "E-Cu", "cupru", "copper");
        Metal(list, "metal-cuzn37", "CuZn37", 100, 0.33, 8400,
            "fy ≈ 140 MPa (ordin)", "EN 12163 — alamă",
            "Ms63", "alama", "alamă", "brass");
        Metal(list, "metal-ti6al4v", "Ti6Al4V", 114, 0.34, 4430,
            "fy ≈ 830 MPa (ordin)", "ASTM B348 / ISO 5832-3 — valori indicative",
            "Grade 5", "titan", "titanium");

        // --- Roci (ISRM / UCS) — carotă L/D ≈ 2 ---
        Rock(list, "rock-granit", "Granit", 50, 0.25, 2700,
            "UCS ≈ 100–250 MPa (ordin 150)", "ISRM suggested methods — UCS, nu plasticitate metalică",
            "granite", "roca", "rocă");
        Rock(list, "rock-bazalt", "Bazalt", 60, 0.25, 2900,
            "UCS ≈ 150–250 MPa (ordin 180)", "ISRM — valori indicative",
            "basalt");
        Rock(list, "rock-andezit", "Andezit", 40, 0.25, 2600,
            "UCS ≈ 80–150 MPa (ordin 100)", "ISRM — valori indicative",
            "andesite");
        Rock(list, "rock-gresie", "Gresie", 15, 0.25, 2300,
            "UCS ≈ 20–80 MPa (ordin 40)", "ISRM — valori indicative",
            "sandstone", "gresie");
        Rock(list, "rock-calcar", "Calcar", 30, 0.28, 2500,
            "UCS ≈ 30–100 MPa (ordin 50)", "ISRM — valori indicative",
            "limestone", "calcar");
        Rock(list, "rock-dolomit", "Dolomit", 40, 0.28, 2700,
            "UCS ≈ 50–150 MPa (ordin 80)", "ISRM — valori indicative",
            "dolomite");
        Rock(list, "rock-sist", "Șist", 20, 0.20, 2600,
            "UCS ≈ 20–80 MPa · anizotrop", "ISRM — orientarea foliației contează",
            "sist", "shist", "schist", "șist");
        Rock(list, "rock-marna", "Marnă", 8, 0.30, 2200,
            "UCS ≈ 5–30 MPa (ordin 15)", "ISRM — rocă slabă",
            "marna", "marl");
        Rock(list, "rock-gnais", "Gnais", 45, 0.25, 2700,
            "UCS ≈ 80–180 MPa (ordin 120)", "ISRM — valori indicative",
            "gneiss", "gnais");
        Rock(list, "rock-cuartit", "Cuarțit", 60, 0.15, 2650,
            "UCS ≈ 150–300 MPa (ordin 200)", "ISRM — valori indicative",
            "cuartit", "quartzite", "cuarțit");
        Rock(list, "rock-conglomerat", "Conglomerat", 15, 0.25, 2400,
            "UCS ≈ 20–80 MPa (ordin 40)", "ISRM — valori indicative",
            "conglomerate");
        Rock(list, "rock-tuf", "Tuf", 5, 0.25, 1800,
            "UCS ≈ 5–30 MPa (ordin 15)", "ISRM — rocă poroasă",
            "tuff", "tuf vulcanic");
        Rock(list, "rock-carbune", "Cărbune", 3, 0.35, 1400,
            "UCS ≈ 5–20 MPa (ordin 10)", "ISRM / minerit — valori indicative",
            "carbune", "coal", "huila", "cărbune");
        Rock(list, "rock-antracit", "Antracit", 5, 0.30, 1600,
            "UCS ≈ 10–40 MPa (ordin 20)", "ISRM / minerit — valori indicative",
            "anthracite");
        Rock(list, "rock-travertin", "Travertin", 15, 0.25, 2400,
            "UCS ≈ 20–60 MPa (ordin 30)", "ISRM — calcar poros",
            "travertine");

        // --- Sare / evaporite (fluaj) — E << granit ---
        Salt(list, "salt-halit", "Halit (sare gemă)", 20, 0.30, 2160,
            "UCS ≈ 15–25 MPa · fluaj la T amb.",
            "Sare gemă — σ–ε măsurat; fluaj Norton nu e ajustat în aplicație",
            "sare", "sare gema", "sare gemă", "salt", "NaCl", "halite", "rock salt");
        Salt(list, "salt-silvinit", "Silvinit", 12, 0.30, 2000,
            "UCS ≈ 10–20 MPa · fluaj",
            "Evaporit K — valori indicative",
            "silvin", "sylvite", "KCl");
        Salt(list, "salt-carnalit", "Carnalit", 8, 0.32, 1600,
            "UCS ≈ 5–15 MPa · fluaj marcat",
            "Evaporit slab — notați T și umiditatea",
            "carnallite", "carnalit");
        Salt(list, "salt-gips", "Gips", 15, 0.28, 2300,
            "UCS ≈ 15–40 MPa (ordin 20)",
            "CaSO4·2H2O — evaporit; nu copiați E de oțel",
            "gypsum", "gips");
        Salt(list, "salt-anhidrit", "Anhidrit", 50, 0.28, 2900,
            "UCS ≈ 40–80 MPa (ordin 60)",
            "CaSO4 — mai rigid decât halitul, tot pachet sare/evaporit",
            "anhydrite", "anhidrit");

        // --- Beton (EN 12390) — cilindru Ø150 × 300 (notă cub) ---
        Beton(list, "beton-c12-15", "C12/15", 25, 0.20, 2300,
            "fck,cyl ≈ 12 MPa · fck,cub ≈ 15 MPa",
            "C12", "C15");
        Beton(list, "beton-c16-20", "C16/20", 27, 0.20, 2300,
            "fck,cyl ≈ 16 MPa · fck,cub ≈ 20 MPa",
            "C16", "C20");
        Beton(list, "beton-c20-25", "C20/25", 29, 0.20, 2300,
            "fck,cyl ≈ 20 MPa · fck,cub ≈ 25 MPa",
            "C20", "C25");
        Beton(list, "beton-c25-30", "C25/30", 31, 0.20, 2400,
            "fck,cyl ≈ 25 MPa · fck,cub ≈ 30 MPa",
            "C25", "C30");
        Beton(list, "beton-c30-37", "C30/37", 33, 0.20, 2400,
            "fck,cyl ≈ 30 MPa · fck,cub ≈ 37 MPa",
            "C30", "C37");
        Beton(list, "beton-c35-45", "C35/45", 34, 0.20, 2400,
            "fck,cyl ≈ 35 MPa · fck,cub ≈ 45 MPa",
            "C35", "C45");
        Beton(list, "beton-c40-50", "C40/50", 35, 0.20, 2400,
            "fck,cyl ≈ 40 MPa · fck,cub ≈ 50 MPa",
            "C40", "C50");
        Beton(list, "beton-c50-60", "C50/60", 37, 0.20, 2400,
            "fck,cyl ≈ 50 MPa · fck,cub ≈ 60 MPa",
            "C50", "C60");
        Beton(list, "beton-c55-67", "C55/67", 38, 0.20, 2450,
            "fck,cyl ≈ 55 MPa · fck,cub ≈ 67 MPa",
            "C55", "C67");
        Mortar(list, "beton-m10", "Mortar M10", 12, 0.20, 2000,
            "fc ≈ 10 MPa (ordin)",
            "M10", "mortar");
        Mortar(list, "beton-m20", "Mortar M20", 18, 0.20, 2100,
            "fc ≈ 20 MPa (ordin)",
            "M20", "mortar");

        return list.ToArray();
    }

    private static void Metal(
        List<SpecimenCard> list, string id, string name,
        double e, double nu, double rho, string strength, string standard,
        params string[] aliases)
        => list.Add(Card(id, name, SpecimenClasses.Metal, FormulaPacks.SteelMetal,
            l0: 80, d0: 40, e, nu, rho, strength, standard, aliases));

    private static void Rock(
        List<SpecimenCard> list, string id, string name,
        double e, double nu, double rho, string strength, string standard,
        params string[] aliases)
        => list.Add(Card(id, name, SpecimenClasses.Roca, FormulaPacks.IsrmUcs,
            l0: 100, d0: 50, e, nu, rho, strength, standard, aliases));

    private static void Salt(
        List<SpecimenCard> list, string id, string name,
        double e, double nu, double rho, string strength, string standard,
        params string[] aliases)
        => list.Add(Card(id, name, SpecimenClasses.Sare, FormulaPacks.SaltCreep,
            l0: 80, d0: 40, e, nu, rho, strength, standard, aliases));

    private static void Beton(
        List<SpecimenCard> list, string id, string name,
        double e, double nu, double rho, string strength,
        params string[] aliases)
        => list.Add(Card(id, name, SpecimenClasses.Beton, FormulaPacks.En12390,
            l0: 300, d0: 150, e, nu, rho, strength,
            "EN 12390 — cilindru Ø150×300 mm (cub 150 mm: notați forma). Clasa e indicativă.",
            aliases));

    private static void Mortar(
        List<SpecimenCard> list, string id, string name,
        double e, double nu, double rho, string strength,
        params string[] aliases)
        => list.Add(Card(id, name, SpecimenClasses.Beton, FormulaPacks.En12390,
            l0: 80, d0: 40, e, nu, rho, strength,
            "EN 12390 / mortar — geometrie cilindru implicită; confirmați prisma/cubul real.",
            aliases));

    private static SpecimenCard Card(
        string id, string name, string cls, string pack,
        double l0, double d0, double e, double nu, double rho,
        string strength, string standard, string[] aliases)
        => new()
        {
            Id = id,
            NameRo = name,
            Class = cls,
            FormulaPack = pack,
            Shape = SpecimenShapes.Cilindru,
            L0Mm = l0,
            D0Mm = d0,
            EGPa = e,
            Nu = nu,
            DensityKgM3 = rho,
            StrengthNotes = strength,
            StandardNote = standard,
            Aliases = aliases.ToList()
        };
}
