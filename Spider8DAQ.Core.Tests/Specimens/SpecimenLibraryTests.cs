using Spider8DAQ.Core.Export;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Specimens;
using Xunit;

namespace Spider8DAQ.Core.Tests.Specimens;

public class SpecimenLibraryTests
{
    [Fact]
    public void Catalog_HasFortyToSixtyDistinctCards()
    {
        var cards = SpecimenCatalog.Build();
        Assert.InRange(cards.Count, 40, 60);
        Assert.Equal(cards.Count, cards.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Catalog_ContainsRequiredNames()
    {
        var names = SpecimenCatalog.Build().Select(c => c.NameRo).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var n in new[]
                 {
                     "S235", "S275", "S355", "S460", "C45", "C60", "42CrMo4", "AISI 304", "AISI 316",
                     "EN-GJL-250", "EN AW-6060", "EN AW-6082", "EN AW-7075", "Cu-ETP", "CuZn37", "Ti6Al4V",
                     "Granit", "Bazalt", "Andezit", "Gresie", "Calcar", "Dolomit", "Șist", "Marnă", "Gnais",
                     "Cuarțit", "Conglomerat", "Tuf", "Cărbune", "Antracit",
                     "Halit (sare gemă)", "Silvinit", "Carnalit", "Gips", "Anhidrit",
                     "C16/20", "C20/25", "C25/30", "C30/37", "C35/45", "C40/50", "C50/60",
                     "Mortar M10", "Mortar M20"
                 })
            Assert.Contains(n, names);
    }

    [Fact]
    public void Packs_AreNotCopiedAcrossClasses()
    {
        var lib = new SpecimenLibrary();
        var granit = lib.Search("granit").First(c => c.NameRo.Contains("Granit", StringComparison.OrdinalIgnoreCase));
        var halit = lib.Search("sare").First(c => c.NameRo.Contains("Halit", StringComparison.OrdinalIgnoreCase));
        var s355 = lib.Search("S355").First();
        var c25 = lib.Search("C25/30").First();

        Assert.Equal(FormulaPacks.IsrmUcs, granit.FormulaPack);
        Assert.Equal(FormulaPacks.SaltCreep, halit.FormulaPack);
        Assert.Equal(FormulaPacks.SteelMetal, s355.FormulaPack);
        Assert.Equal(FormulaPacks.En12390, c25.FormulaPack);
        Assert.True(halit.EGPa < granit.EGPa);
        Assert.True(granit.EGPa < s355.EGPa);
    }

    [Theory]
    [InlineData("sare")]
    [InlineData("SARE")]
    [InlineData("sare gema")]
    public void Search_Sare_FindsHalit(string q)
    {
        var hits = new SpecimenLibrary().Search(q);
        Assert.Contains(hits, c => c.Id == "salt-halit");
    }

    [Fact]
    public void Search_Sist_FindsSchist_IgnoringDiacritics()
    {
        var hits = new SpecimenLibrary().Search("sist");
        Assert.Contains(hits, c => c.Id == "rock-sist");
    }

    [Fact]
    public void Search_ClassRoca_FindsGranit()
    {
        var hits = new SpecimenLibrary().Search("roca");
        Assert.Contains(hits, c => c.Id == "rock-granit");
    }

    [Fact]
    public void SummaryLine_MatchesProductWording()
    {
        var halit = SpecimenCatalog.Build().First(c => c.Id == "salt-halit");
        Assert.Equal("Epruvetă: Halit (sare gemă) · pachet Sare (fluaj)", halit.BuildSummaryLine());
    }

    [Fact]
    public void IsCompression_GatesLibraryToCompressionTypes()
    {
        Assert.True(ExperimentTypes.IsCompression(ExperimentTypes.CylinderContour));
        Assert.True(ExperimentTypes.IsCompression("Compresiune cilindru - contur"));
        Assert.True(ExperimentTypes.IsCompression("Compresiune uniaxială"));
        Assert.False(ExperimentTypes.IsCompression(ExperimentTypes.StrainGauges));
        Assert.False(ExperimentTypes.IsCompression("tensometrie"));
        Assert.False(ExperimentTypes.IsCompression("Mixt / multi-senzor"));
        Assert.False(ExperimentTypes.IsCompression("Forță / Celule de sarcină"));
        Assert.False(ExperimentTypes.IsCompression("CWT"));
        Assert.False(ExperimentTypes.IsCompression(""));
        Assert.False(ExperimentTypes.IsCompression(null));
    }

    [Fact]
    public void ReportRows_Omitted_WhenNotCompression()
    {
        var meta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.StrainGauges,
            SpecimenId = "salt-halit",
            SpecimenNameRo = "Halit (sare gemă)",
            SpecimenFormulaPack = FormulaPacks.SaltCreep,
            SpecimenSummary = "Epruvetă: Halit (sare gemă) · pachet Sare (fluaj)"
        };
        Assert.Empty(SpecimenIdentification.BuildReportMetaRows(meta));
        Assert.Empty(SpecimenIdentification.BuildCsvCommentLines(meta));
    }

    [Fact]
    public void ReportRows_IncludePack_WhenCompression()
    {
        var card = SpecimenCatalog.Build().First(c => c.Id == "salt-halit");
        var meta = new ProjectMeta { ExperimentType = ExperimentTypes.CylinderContour };
        SpecimenIdentification.ApplyCard(meta, card);
        var rows = SpecimenIdentification.BuildReportMetaRows(meta);
        Assert.Contains(rows, r => r.Label == "Epruvetă" && r.Value.Contains("Halit") && r.Value.Contains("Sare (fluaj)"));
        Assert.Contains(rows, r => r.Label == "Pachet formule" && r.Value == FormulaPacks.LabelSaltCreep);
        Assert.Contains(rows, r => r.Label == "Interpretare (pachet)" && r.Value.Contains("Norton", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(rows, r => r.Label == "Valori epruvetă" && r.Value.Contains("indicative", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(SpecimenIdentification.BuildCsvCommentLines(meta),
            l => l.StartsWith("FormulaPack=", StringComparison.Ordinal));
    }

    [Fact]
    public void CustomCard_PersistsNextToAppDataFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "upet_spec_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var lib = SpecimenLibrary.Load(path);
            lib.AddOrUpdateCustom(new SpecimenCard
            {
                NameRo = "Probă lab X",
                Class = SpecimenClasses.Personalizat,
                FormulaPack = FormulaPacks.IsrmUcs,
                Aliases = ["xyz-alias"],
                L0Mm = 70,
                D0Mm = 35,
                EGPa = 12,
                Nu = 0.25
            });
            lib.LastSpecimenId = lib.CustomCards[0].Id;
            lib.Save(path);

            var loaded = SpecimenLibrary.Load(path);
            Assert.Equal(lib.LastSpecimenId, loaded.LastSpecimenId);
            Assert.Single(loaded.CustomCards);
            Assert.Contains(loaded.Search("xyz-alias"), c => c.NameRo == "Probă lab X");
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void HtmlReadme_IncludeSpecimen_OnlyForCompression()
    {
        var card = SpecimenCatalog.Build().First(c => c.Id == "rock-granit");
        var compression = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleId = "P1"
        };
        SpecimenIdentification.ApplyCard(compression, card);
        var readme = LabPackageBuilder.BuildReadme(compression, "a.csv", ["a.csv"]);
        Assert.Contains("Epruvetă", readme);
        Assert.Contains("ISRM", readme);

        var strain = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.StrainGauges,
            SampleId = "P1",
            SpecimenId = card.Id,
            SpecimenNameRo = card.NameRo,
            SpecimenFormulaPack = card.FormulaPack,
            SpecimenSummary = card.BuildSummaryLine()
        };
        var strainReadme = LabPackageBuilder.BuildReadme(strain, "a.csv", ["a.csv"]);
        Assert.DoesNotContain("pachet ISRM", strainReadme);
        Assert.DoesNotContain("Epruvetă: Granit", strainReadme);
    }

    [Fact]
    public void ReportRows_Omitted_WhenCompressionButNoSpecimen()
    {
        var meta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleId = "P1",
            SampleLengthMm = 80,
            SampleDiameterMm = 40
        };
        Assert.False(SpecimenIdentification.Applies(meta));
        Assert.Empty(SpecimenIdentification.BuildReportMetaRows(meta));
        Assert.Empty(SpecimenIdentification.BuildCsvCommentLines(meta));

        var readme = LabPackageBuilder.BuildReadme(meta, "a.csv", ["a.csv"]);
        Assert.DoesNotContain("Epruvetă:", readme);
        Assert.DoesNotContain("Pachet formule", readme);
        Assert.DoesNotContain("· pachet", readme);
    }

    [Fact]
    public void ReportRows_Omitted_JunkSummaryWithoutIdentity()
    {
        var meta = new ProjectMeta
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SpecimenSummary = "Epruvetă: · pachet"
        };
        Assert.False(SpecimenIdentification.Applies(meta));
        Assert.Empty(SpecimenIdentification.BuildReportMetaRows(meta));
        Assert.Empty(SpecimenIdentification.BuildCsvCommentLines(meta));
        Assert.False(SpecimenIdentification.HasMeaningfulSpecimenText("Epruvetă: · pachet"));
        Assert.False(SpecimenIdentification.HasMeaningfulSpecimenText(""));
        Assert.True(SpecimenIdentification.HasMeaningfulSpecimenText("Halit (sare gemă) · pachet Sare (fluaj)"));
    }

    [Fact]
    public void LastSpecimenId_Empty_PersistsAsNone()
    {
        var path = Path.Combine(Path.GetTempPath(), "upet_spec_none_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var lib = SpecimenLibrary.Load(path);
            lib.RememberLast("salt-halit", path);
            Assert.Equal("salt-halit", SpecimenLibrary.Load(path).LastSpecimenId);

            lib.RememberLast("", path);
            var loaded = SpecimenLibrary.Load(path);
            Assert.Equal("", loaded.LastSpecimenId);
            Assert.Null(loaded.FindById(loaded.LastSpecimenId));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }
}
