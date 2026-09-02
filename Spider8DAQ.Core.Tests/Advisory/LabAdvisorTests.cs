using Spider8DAQ.Core.Advisory;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Sensors;
using Xunit;

namespace Spider8DAQ.Core.Tests.Advisory;

public class LabAdvisorTests
{
    [Fact]
    public void Advise_OpenPort_returns_connect_steps()
    {
        var items = LabAdvisor.Advise("Intfac OpenPort eșuat #1 (Connect Intfac…)");
        Assert.NotEmpty(items);
        Assert.Contains(items, i => i.Id == "connect-openport");
        Assert.Contains(items[0].Steps, s => s.Contains("catman", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("preflight", items.First(i => i.Id == "connect-openport").ActionId);
    }

    [Fact]
    public void Advise_NoLive_returns_omb_hint()
    {
        var items = LabAdvisor.Advise("Avertisment: fără eșantioane live după 2s — verificați OMB/USB/Start.");
        Assert.Contains(items, i => i.Id == "no-live-samples");
    }

    [Fact]
    public void Advise_UsbHbm_and_Serial_and_Preflight()
    {
        Assert.Contains(LabAdvisor.Advise("USBHBM lipsă VID_10D1"), i => i.Id == "usb-hbm");
        Assert.Contains(LabAdvisor.Advise("Selectați Serial pe COM3"), i => i.Id == "serial-com");
        Assert.Contains(LabAdvisor.Advise("Rulează Preflight F10"), i => i.Id == "preflight");
    }

    [Fact]
    public void Advise_P15_U2B_Timbru_Polarity_Shunt_Capacity()
    {
        Assert.Contains(LabAdvisor.Advise("cum configurez P15 presiune bar?"), i => i.Id == "p15-dcvoltage");
        Assert.Contains(LabAdvisor.Advise("U2B 5kN celulă de forță"), i => i.Id == "u2b-force");
        Assert.Contains(LabAdvisor.Advise("timbru µm/m Gauge Factor"), i => i.Id == "timbru");
        Assert.Contains(LabAdvisor.Advise("inversare polaritate Scale × −1"), i => i.Id == "polarity");
        Assert.Contains(LabAdvisor.Advise("Shunt CH pe Full bridge"), i => i.Id == "shunt");
        Assert.Contains(LabAdvisor.Advise("valori peste Capacity / %FS"), i => i.Id == "capacity-over");
        Assert.Contains(LabAdvisor.Advise("Scale suspect: U2B=7000 > Capacity×1.2"), i => i.Id == "scale-suspect");
        Assert.Contains(LabAdvisor.Advise("Half bridge compensare temperatură"), i => i.Id == "half-bridge-thermal");
        Assert.Contains(LabAdvisor.Advise("Excitație suspectă pe CH0"), i => i.Id == "excitation-mismatch");
        Assert.Contains(LabAdvisor.Advise("CH0 Overflow — oprește, mărește domeniul sau lasă Autorange să aleagă înainte de Rec, Zero, reia"),
            i => i.Id == "autorange-overflow");
    }

    [Fact]
    public void Advise_LedEst_and_Record()
    {
        Assert.Contains(LabAdvisor.Advise("LED ERROR EST=100 Power-cycle"), i => i.Id == "led-error");
        Assert.Contains(LabAdvisor.Advise("Înregistrare goală 0 eșantioane CSV"), i => i.Id == "record-empty");
    }

    [Fact]
    public void Advise_ShuntNoOmb_is_primary()
    {
        var status = "Shunt CH0: FAIL — fără citire OMB (Start măsurare + canal On)  Sugestie: "
                     + ShuntCheck.SuggestNoOmb;
        var items = LabAdvisor.Advise(status);
        Assert.Equal("shunt-no-omb", items[0].Id);
        Assert.Equal(ShuntCheck.SuggestNoOmb, items[0].Suggestion);
    }

    [Fact]
    public void Advise_ShuntEstBanner_beats_Omb()
    {
        var status = "Shunt CH0: FAIL — fără citire OMB (Start măsurare + canal On)  Sugestie: "
                     + ShuntCheck.SuggestEstError;
        var items = LabAdvisor.Advise(status);
        Assert.Equal("shunt-est", items[0].Id);
        Assert.Equal(ShuntCheck.SuggestEstError, items[0].Suggestion);
    }

    [Fact]
    public void Advise_ShuntStepFail_mentions_wiring()
    {
        var status = "Shunt CH0: 1262 µm/m (așteptat 1890 ±0.5%)  FAIL — cablaj / Rsh / punte (Δ=33%)  "
                     + "Sugestie: Δ=33% față de așteptat. " + ShuntCheck.SuggestStepFailBody;
        var items = LabAdvisor.Advise(status);
        Assert.Equal("shunt-step", items[0].Id);
        Assert.Contains("pin 120", items[0].Suggestion, StringComparison.Ordinal);
    }

    [Fact]
    public void Advise_ScaleInvalid_And_StepLow_And_Dll()
    {
        Assert.Equal("scale-timbru-absurd",
            LabAdvisor.Advise(ShuntCheck.ScaleInvalidLabel + " Scale=91885").First().Id);
        Assert.Equal("shunt-step-low",
            LabAdvisor.Advise("Shunt CH0: FAIL Treapta e sub așteptat ~25% jos").First().Id);
        Assert.Equal("shunt-dll",
            LabAdvisor.Advise("Shunt CH0: " + ShuntCheck.SuggestNoOmbDll).First().Id);
        Assert.Equal("shunt-pass",
            LabAdvisor.Advise("Shunt CH0: PASS " + ShuntCheck.SuggestPass).First().Id);
    }

    [Fact]
    public void Advise_HalfDummy_ShuntSkip_NotWiringFail()
    {
        var status = "Shunt CH1: 301 µm/m — " + ShuntCheck.PassHalfDummyLabel + " — "
                     + ShuntCheck.HalfDummySkipBody
                     + "\nSugestie: " + ShuntCheck.SuggestHalfDummySkip;
        var items = LabAdvisor.Advise(status);
        Assert.Equal("shunt-half-dummy", items[0].Id);
        Assert.Equal(ShuntCheck.SuggestHalfDummySkip, items[0].Suggestion);
        Assert.DoesNotContain(items, i => i.Id is "shunt-step" or "shunt-step-low" or "shunt-step-high");
        Assert.Contains(items[0].Steps, s => s.Contains("apăsați pe timbrul activ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Advise_Context_not_connected_boosts_connect()
    {
        var ctx = new AdviceContext { IsConnected = false, IsStreaming = false, ChannelsOn = 0 };
        var items = LabAdvisor.Advise("nu vin date live", null, ctx, max: 4);
        Assert.Contains(items, i => i.Id is "ctx-not-connected" or "connect-generic" or "no-live-samples");
    }

    [Fact]
    public void Advise_Context_connected_not_streaming()
    {
        var ctx = new AdviceContext
        {
            IsConnected = true,
            IsStreaming = false,
            ChannelsOn = 1,
            SelectedSensor = "U2B 5kN"
        };
        var items = LabAdvisor.Advise("fără eșantioane live", null, ctx, max: 4);
        Assert.Contains(items, i => i.Id is "ctx-not-streaming" or "no-live-samples");
        Assert.Contains(items, i => i.Id == "u2b-force");
    }

    [Fact]
    public void Advise_Question_enrichment_P15()
    {
        var items = LabAdvisor.Advise("de ce nu primesc date de pe P15?");
        Assert.Contains(items, i => i.Id is "p15-dcvoltage" or "no-live-samples" or "generic-help");
        Assert.False(string.IsNullOrWhiteSpace(items[0].Suggestion));
    }

    [Fact]
    public void Memory_boosts_helpful_rules()
    {
        var path = Path.Combine(Path.GetTempPath(), "advisor-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var mem = new AdvisorMemory(path);
            mem.RecordShown("connect-openport", "OpenPort eșuat");
            mem.RecordFeedback("connect-openport", helpful: true);
            Assert.True(mem.Boost("connect-openport") > 0);

            var ranked = LabAdvisor.Advise("OpenPort eșuat catman", mem, max: 2);
            Assert.Equal("connect-openport", ranked[0].Id);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LooksLikeProblem_detects_common_errors()
    {
        Assert.True(LabAdvisor.LooksLikeProblem("OpenPort eșuat"));
        Assert.True(LabAdvisor.LooksLikeProblem("Avertisment: fără date"));
        Assert.False(LabAdvisor.LooksLikeProblem("OK"));
    }

    [Fact]
    public void Advise_StrainType_never_mentions_contour_sheet()
    {
        var ctx = new AdviceContext
        {
            ExperimentType = ExperimentTypes.StrainGauges,
            IsConnected = true,
            IsStreaming = true,
            IsZeroed = true,
            HasAppliedSensor = true,
            ChannelsOn = 1,
            HasGaugeFactorScale = false,
            HasOfflineSession = true,
            OfflineSampleCount = 80
        };
        var items = LabAdvisor.Advise("cum fac tensometrie?", null, ctx, max: 4);
        Assert.Contains(items, i => i.Id is "ctx-strain-gf" or "timbru" or "ctx-strain-unload" or "ctx-strain-eps");
        Assert.DoesNotContain(items, i =>
            (i.Title + i.Suggestion + string.Join(' ', i.Steps))
            .Contains("foaia Contur", StringComparison.OrdinalIgnoreCase)
            || i.Id.StartsWith("ctx-contour", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Advise_ContourType_asks_for_diameter()
    {
        var ctx = new AdviceContext
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            IsConnected = true,
            IsStreaming = true,
            IsZeroed = true,
            HasAppliedSensor = true,
            ChannelsOn = 4,
            SampleDiameterMm = 0,
            SampleLengthMm = 0
        };
        var items = LabAdvisor.Advise("", null, ctx, max: 4);
        Assert.Contains(items, i => i.Id == "ctx-contour-diameter");
        Assert.DoesNotContain(items, i => i.Id is "timbru" or "ctx-strain-gf");
    }

    [Fact]
    public void Advise_ExportBusy_and_NotZeroed()
    {
        var busy = LabAdvisor.Advise("", null, new AdviceContext
        {
            IsConnected = true,
            IsStreaming = true,
            ExportStatus = AdvisorExportStatus.InProgress
        }, max: 3);
        Assert.Contains(busy, i => i.Id == "ctx-export-busy");
        Assert.Contains("8000", busy[0].Suggestion);

        var zero = LabAdvisor.Advise("", null, new AdviceContext
        {
            IsConnected = true,
            IsStreaming = true,
            HasAppliedSensor = true,
            SelectedSensor = "U2B",
            ChannelsOn = 1,
            IsZeroed = false
        }, max: 3);
        Assert.Contains(zero, i => i.Id == "ctx-not-zeroed");
        Assert.Equal("zero", zero.First(i => i.Id == "ctx-not-zeroed").ActionId);
    }

    [Fact]
    public void Advise_RejectedRule_is_skipped()
    {
        var path = Path.Combine(Path.GetTempPath(), "advisor-rej-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var mem = new AdvisorMemory(path);
            mem.RecordShown("preflight", "F10");
            mem.RecordFeedback("preflight", helpful: false);
            Assert.True(mem.IsRejected("preflight"));
            var items = LabAdvisor.Advise("Rulează Preflight F10", mem, max: 3);
            Assert.DoesNotContain(items, i => i.Id == "preflight");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Advise_PeerCompare_same_type_only()
    {
        var current = new ExperimentSummary
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleCount = 1000,
            OvalityMm = 0.07,
            FileName = "now.csv",
            ModifiedLocal = DateTime.Now
        };
        var peer = new ExperimentSummary
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            SampleCount = 900,
            OvalityMm = 0.05,
            FileName = "old.csv",
            ModifiedLocal = new DateTime(2026, 8, 10)
        };
        var strainPeer = new ExperimentSummary
        {
            ExperimentType = ExperimentTypes.StrainGauges,
            EpsMax = 400,
            SampleCount = 800,
            FileName = "strain.csv",
            ModifiedLocal = new DateTime(2026, 8, 9)
        };

        var ctx = new AdviceContext
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            IsConnected = true,
            IsStreaming = true,
            IsZeroed = true,
            HasAppliedSensor = true,
            ChannelsOn = 4,
            SampleDiameterMm = 100,
            SampleLengthMm = 80,
            ContourMapped = true,
            ContourAnglesSet = true,
            HasOfflineSession = true,
            OfflineSampleCount = 1000,
            Current = current,
            Peers = new[] { peer, strainPeer },
            PeerScanComplete = true
        };
        var items = LabAdvisor.Advise("", null, ctx, max: 4);
        var cmp = items.First(i => i.Id == "peer-compare");
        Assert.Contains("ovalitate", cmp.Suggestion, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0.07", cmp.Suggestion);
        Assert.Contains("0.05", cmp.Suggestion);
        Assert.DoesNotContain("ε_max", cmp.Suggestion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("400", cmp.Suggestion);
    }

    [Fact]
    public void Advise_CompressionSpecimenPack_mentions_salt_not_steel()
    {
        var ctx = new AdviceContext
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            IsConnected = true,
            IsStreaming = true,
            IsZeroed = true,
            HasAppliedSensor = true,
            ChannelsOn = 4,
            SampleDiameterMm = 40,
            SampleLengthMm = 80,
            ContourMapped = true,
            ContourAnglesSet = true,
            SpecimenName = "Halit (sare gemă)",
            SpecimenClass = "Sare",
            SpecimenFormulaPack = "SaltCreep"
        };
        var items = LabAdvisor.Advise("", null, ctx, max: 4);
        Assert.Contains(items, i => i.Id == "ctx-specimen-pack");
        var pack = items.First(i => i.Id == "ctx-specimen-pack");
        Assert.Contains("fluaj", pack.Suggestion, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("plasticitate", pack.Suggestion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Advise_StrainType_never_mentions_specimen_pack()
    {
        var ctx = new AdviceContext
        {
            ExperimentType = ExperimentTypes.StrainGauges,
            IsConnected = true,
            IsStreaming = true,
            IsZeroed = true,
            HasAppliedSensor = true,
            ChannelsOn = 1,
            HasGaugeFactorScale = false,
            SpecimenName = "Halit (sare gemă)",
            SpecimenFormulaPack = "SaltCreep"
        };
        var items = LabAdvisor.Advise("cum fac tensometrie?", null, ctx, max: 4);
        Assert.DoesNotContain(items, i => i.Id.StartsWith("ctx-specimen", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(items, i =>
            (i.Title + i.Suggestion).Contains("pachet", StringComparison.OrdinalIgnoreCase)
            && (i.Title + i.Suggestion).Contains("sare", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Advise_CompressionWithoutSpecimen_does_not_nag_missing_card()
    {
        var ctx = new AdviceContext
        {
            ExperimentType = ExperimentTypes.CylinderContour,
            IsConnected = true,
            IsStreaming = true,
            IsZeroed = true,
            HasAppliedSensor = true,
            ChannelsOn = 4,
            SampleDiameterMm = 40,
            SampleLengthMm = 80,
            ContourMapped = true,
            ContourAnglesSet = true
        };
        var items = LabAdvisor.Advise("", null, ctx, max: 4);
        Assert.DoesNotContain(items, i => i.Id == "ctx-specimen-optional");
        Assert.DoesNotContain(items, i => i.Id == "ctx-specimen-pack");
        Assert.DoesNotContain(items, i =>
            (i.Title + " " + i.Suggestion).Contains("lipsește", StringComparison.OrdinalIgnoreCase)
            || (i.Title + " " + i.Suggestion).Contains("lipsa epruvet", StringComparison.OrdinalIgnoreCase));
    }
}
