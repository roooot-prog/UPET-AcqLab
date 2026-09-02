using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Sensors;
using Xunit;

namespace Spider8DAQ.Core.Tests.Sensors;

public class ShuntCheckTests
{
    private static ShuntCheckRequest Quarter350(
        double reading,
        double rshKohm = 100,
        double? capacity = null,
        bool simulator = false,
        string unit = "µm/m") => new()
    {
        ChannelName = "CH0",
        Reading = reading,
        ShuntKohm = rshKohm,
        GaugeFactor = 2.0,
        GaugeOhm = 350,
        Bridge = nameof(BridgeType.Quarter),
        Unit = unit,
        Simulator = simulator
        // Capacity is intentionally not a field — Autorange domain must not affect ε_shunt.
    };

    [Fact]
    public void Expected_Quarter_350ohm_100k_Gf2_Is1750()
    {
        var e = ShuntCheck.ExpectedStrainUe(350, 100, 2.0, BridgeType.Quarter, null, 0.3);
        Assert.Equal(1750.0, e, 6);
        Assert.Equal(1.0, ShuntCheck.BFactor(BridgeType.Quarter, null, 0.3), 6);
        Assert.Equal(1.0, ShuntCheck.WheatstoneB(BridgeType.Quarter, null, 0.3), 6);
    }

    [Fact]
    public void Expected_HalfDummyT_MatchesQuarter_BFactor1()
    {
        var e = ShuntCheck.ExpectedStrainUe(
            350, 100, 2.0, BridgeType.Half, StrainScale.HalfConfigActivDummyT, 0.3);
        Assert.Equal(1750.0, e, 6);
        Assert.Equal(1.0, ShuntCheck.BFactor(BridgeType.Half, StrainScale.HalfConfigActivDummyT, 0.3), 6);
        Assert.Equal(2000.0,
            StrainScale.FromGaugeFactor(BridgeType.Half, 2.0, StrainScale.HalfConfigActivDummyT, 0.3), 6);
    }

    [Fact]
    public void Expected_HalfSimplu_BFactorHalf()
    {
        var e = ShuntCheck.ExpectedStrainUe(
            350, 100, 2.0, BridgeType.Half, StrainScale.HalfConfigSimplu, 0.3);
        Assert.Equal(875.0, e, 6);
        Assert.Equal(0.5, ShuntCheck.BFactor(BridgeType.Half, StrainScale.HalfConfigSimplu, 0.3), 6);
        Assert.Equal(2.0, ShuntCheck.WheatstoneB(BridgeType.Half, StrainScale.HalfConfigSimplu, 0.3), 6);
    }

    [Fact]
    public void Expected_HalfPoisson_UsesNu()
    {
        // B_w = 2(1+0.3)=2.6; B_factor=1/2.6; ε=1750/2.6
        var e = ShuntCheck.ExpectedStrainUe(
            350, 100, 2.0, BridgeType.Half, StrainScale.HalfConfigPoisson, 0.3);
        Assert.Equal(1750.0 / 2.6, e, 6);
        Assert.Equal(1.0 / 2.6, ShuntCheck.BFactor(BridgeType.Half, StrainScale.HalfConfigPoisson, 0.3), 6);
    }

    [Fact]
    public void Expected_Full_And_Incovoiere_BFactorQuarter()
    {
        var full = ShuntCheck.ExpectedStrainUe(350, 100, 2.0, BridgeType.Full, null, 0.3);
        var bend = ShuntCheck.ExpectedStrainUe(
            350, 100, 2.0, BridgeType.Half, StrainScale.HalfConfigIncovoiere, 0.3);
        Assert.Equal(437.5, full, 6);
        Assert.Equal(437.5, bend, 6);
        Assert.Equal(0.25, ShuntCheck.BFactor(BridgeType.Full, null, 0.3), 6);
        Assert.Equal(4.0, ShuntCheck.WheatstoneB(BridgeType.Full, null, 0.3), 6);
    }

    [Theory]
    [InlineData(1575.0, true)]
    [InlineData(1925.0, true)]
    [InlineData(1574.0, false)]
    [InlineData(1926.0, false)]
    [InlineData(1750.0, true)]
    public void PassFail_Window_10Percent_Around1750(double measuredUe, bool pass)
    {
        Assert.Equal(pass, ShuntCheck.InTolerance(measuredUe, 1750.0, 10));
    }

    [Fact]
    public void PassFail_Example_1984_vs_2000()
    {
        Assert.True(ShuntCheck.InTolerance(1984, 2000, 10));
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH0",
            Reading = 1984, // already µm/m (> 50 electrical span)
            ShuntKohm = 87.5, // 1e6×350/(87500×2)×1 = 2000
            GaugeFactor = 2,
            GaugeOhm = 350,
            Bridge = nameof(BridgeType.Quarter),
            Unit = "µm/m",
            TolerancePercent = 1
        });
        Assert.True(r.Passed);
        Assert.Contains("PASS (±1%)", r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("așteptat 2000 ±1%", r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("Timbru Quarter GF=2 R=350", r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("Rsh=87.5 kΩ", r.StatusLine, StringComparison.Ordinal);
        var lines = r.StatusLine.Split('\n');
        Assert.True(lines.Length >= 3, r.StatusLine);
        Assert.Contains("PASS", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("Timbru ", lines[1], StringComparison.Ordinal);
        Assert.StartsWith("Sugestie: ", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultAllowedDifference_IsHalfPercent_NotTen()
    {
        Assert.Equal(0.5, ShuntCheck.DefaultTolerancePercent, 6);
        var pass = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH0",
            Reading = 1991, // 0.45% from 2000
            ShuntKohm = 87.5,
            GaugeFactor = 2,
            GaugeOhm = 350,
            Bridge = nameof(BridgeType.Quarter),
            Unit = "µm/m"
        });
        Assert.True(pass.Passed);
        Assert.Contains("PASS (±0.5%)", pass.StatusLine, StringComparison.Ordinal);

        var fail = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH0",
            Reading = 1984, // 0.8% from 2000
            ShuntKohm = 87.5,
            GaugeFactor = 2,
            GaugeOhm = 350,
            Bridge = nameof(BridgeType.Quarter),
            Unit = "µm/m"
        });
        Assert.False(fail.Passed);
        Assert.DoesNotContain("±10%", fail.StatusLine, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 0.5)]
    [InlineData(-1, 0.5)]
    [InlineData(0.4, 0.5)]
    [InlineData(0.5, 0.5)]
    [InlineData(5, 5)]
    [InlineData(10, 5)]
    [InlineData(double.NaN, 0.5)]
    public void ClampAllowedDifference_CatmanRange(double input, double expected)
    {
        Assert.Equal(expected, ShuntCheck.ClampAllowedDifferencePercent(input), 6);
    }

    [Fact]
    public void ElectricalMvPerV_ConvertedWithSameScaleRelation()
    {
        // 0.875 mV/V × (4000/2) = 1750 µm/m
        var r = ShuntCheck.Evaluate(Quarter350(0.875));
        Assert.True(r.Passed);
        Assert.Equal(1750.0, r.MeasuredUe, 3);
        Assert.Equal(1750.0, r.ExpectedUe, 3);
        Assert.Contains("PASS", r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("Timbru Quarter", r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("Rsh=100 kΩ", r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void CapacityAutorangeDomain_DoesNotChangeExpected()
    {
        var e = ShuntCheck.ExpectedStrainUe(350, 100, 2.0, BridgeType.Quarter, null, 0.3);
        Assert.Equal(1750.0, e, 6);
        var r = ShuntCheck.Evaluate(Quarter350(0.875));
        Assert.Equal(1750.0, r.ExpectedUe, 6);
        Assert.DoesNotContain("10000", r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("2000 ±", r.StatusLine, StringComparison.Ordinal); // expected is 1750, not domain 2000
    }

    [Fact]
    public void MissingRsh_Status()
    {
        var r = ShuntCheck.Evaluate(Quarter350(0.875, rshKohm: 0));
        Assert.False(r.Passed);
        Assert.Equal(ShuntCheckKind.Incomplete, r.Kind);
        Assert.Contains(ShuntCheck.MissingRsh, r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void RcompStoredAsRsh_TreatedAsMissing()
    {
        var r = ShuntCheck.Evaluate(Quarter350(0.875, rshKohm: 0.35));
        Assert.False(r.Passed);
        Assert.Contains(ShuntCheck.MissingRsh, r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ForceChannelWithoutTimbru_IsSkippedNotSilentGf()
    {
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH2",
            Reading = 0.9,
            ShuntKohm = 100,
            Bridge = nameof(BridgeType.Full),
            Unit = "N"
        });
        Assert.Equal(ShuntCheckKind.Skipped, r.Kind);
        Assert.False(r.Passed);
        Assert.Contains("sărit", r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("GF=2", r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("PASS", r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTimbru_NoSilentGf2()
    {
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH0",
            Reading = 0.875,
            ShuntKohm = 100,
            Bridge = nameof(BridgeType.Half),
            Unit = "µm/m"
        });
        Assert.False(r.Passed);
        Assert.Contains(ShuntCheck.MissingTimbru, r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("PASS", r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("GF=2", r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void HalfWithoutConfig_RequiresTimbru()
    {
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH0",
            Reading = 0.875,
            ShuntKohm = 100,
            GaugeFactor = 2,
            GaugeOhm = 350,
            Bridge = nameof(BridgeType.Half),
            HalfConfig = "",
            Unit = "µm/m"
        });
        Assert.Contains(ShuntCheck.MissingTimbru, r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Simulator_NeverPassVsRsh()
    {
        var r = ShuntCheck.Evaluate(Quarter350(0.875, simulator: true));
        Assert.False(r.Passed);
        Assert.Equal(ShuntCheckKind.Simulator, r.Kind);
        Assert.Contains(ShuntCheck.SimulatorLabel, r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("PASS", r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("așteptat", r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void NaN_And_Garbage_Fail()
    {
        var nan = ShuntCheck.Evaluate(Quarter350(double.NaN));
        Assert.False(nan.Passed);
        Assert.Contains(ShuntCheck.NoOmb, nan.StatusLine, StringComparison.Ordinal);

        var huge = ShuntCheck.Evaluate(Quarter350(5e6));
        Assert.False(huge.Passed);
        Assert.Equal(ShuntCheckKind.Fail, huge.Kind);
        Assert.Contains(ShuntCheck.FailWiring, huge.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void OutsideBand_FailWiring()
    {
        var r = ShuntCheck.Evaluate(Quarter350(0.2)); // 400 µm/m vs 1750
        Assert.False(r.Passed);
        Assert.Contains(ShuntCheck.FailWiring, r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("Timbru Quarter GF=2 R=350", r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaleAfterShunt_MultipliesExpectedOverMeasured()
    {
        Assert.Equal(2000.0 * (1750.0 / 1600.0), ShuntCheck.ScaleAfterShunt(2000, 1750, 1600), 6);
    }

    [Fact]
    public void CanApplyScale_OnlyStrainAndAdjacent()
    {
        var ok = ShuntCheck.Evaluate(Quarter350(0.875));
        Assert.True(ok.CanApplyScale);

        var force = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH2",
            Reading = 0.875,
            ShuntKohm = 100,
            GaugeFactor = 2,
            GaugeOhm = 350,
            Bridge = nameof(BridgeType.Full),
            Unit = "N"
        });
        Assert.True(force.Passed); // Full B_factor 0.25: 0.875×500=437.5, expected 437.5
        Assert.False(force.CanApplyScale);
    }

    [Fact]
    public void NoOmb_Appends_Romanian_Suggestion()
    {
        var r = ShuntCheck.Evaluate(Quarter350(double.NaN));
        Assert.Equal(ShuntCheckKind.NoReading, r.Kind);
        Assert.Contains(ShuntCheck.NoOmb, r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("Sugestie: " + ShuntCheck.SuggestNoOmb, r.StatusLine, StringComparison.Ordinal);
        Assert.Equal(ShuntCheck.SuggestNoOmb, r.Suggestion);
    }

    [Fact]
    public void NoOmb_WithDeviceInError_Uses_Est_Suggestion()
    {
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH0",
            Reading = double.NaN,
            ShuntKohm = 29.9,
            GaugeFactor = 2,
            GaugeOhm = 120,
            Bridge = nameof(BridgeType.Quarter),
            Unit = "µm/m",
            DeviceInError = true
        });
        Assert.Equal(ShuntCheckKind.NoReading, r.Kind);
        Assert.Contains(ShuntCheck.NoOmb, r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("Sugestie: " + ShuntCheck.SuggestEstError, r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain(ShuntCheck.SuggestNoOmb, r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void StepFail_Includes_Percent_And_Wiring_Suggestion()
    {
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH0",
            Reading = 1262, // already µm/m
            ShuntKohm = 29.9,
            GaugeFactor = 2,
            GaugeOhm = 120,
            Bridge = nameof(BridgeType.Quarter),
            Unit = "µm/m"
        });
        Assert.False(r.Passed);
        Assert.Equal(ShuntCheckKind.Fail, r.Kind);
        Assert.True(r.DeviationPercent > 30);
        Assert.Contains("Δ=", r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("Sugestie:", r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("pin 120 vs 350", r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("nu Aplică Scale din shunt pe FAIL", r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("PASS", r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRsh_And_Timbru_Have_Suggestions()
    {
        var rsh = ShuntCheck.Evaluate(Quarter350(0.875, rshKohm: 0));
        Assert.Contains("Sugestie: " + ShuntCheck.SuggestMissingRsh, rsh.StatusLine, StringComparison.Ordinal);

        var tim = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH0",
            Reading = 0.875,
            ShuntKohm = 100,
            Bridge = nameof(BridgeType.Half),
            Unit = "µm/m"
        });
        Assert.Contains("Sugestie: " + ShuntCheck.SuggestMissingTimbru, tim.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Simulator_Keeps_Label_Without_Extra_Sugestie_Line()
    {
        var r = ShuntCheck.Evaluate(Quarter350(0.875, simulator: true));
        Assert.Equal(ShuntCheck.SuggestSimulator, r.Suggestion);
        Assert.Contains(ShuntCheck.SimulatorLabel, r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Sugestie:", r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void LooksLikeEstLedError_Hard_Codes_Not_ResetAck()
    {
        Assert.True(ShuntCheck.LooksLikeEstLedError("EST=10003 LED ERROR"));
        Assert.True(ShuntCheck.LooksLikeEstLedError("Power-cycle Spider8 — LED ERROR"));
        Assert.False(ShuntCheck.LooksLikeEstLedError("EST=10000 reset ACK"));
        Assert.False(ShuntCheck.LooksLikeEstLedError(""));
    }

    [Fact]
    public void PollutedScale_91885_DoesNotChangeMeasured_HalfDummySkipsQuarter()
    {
        // 0.75 mV/V × (4000/2.12) ≈ 1415; quarter formula would be ~1893 — must NOT FAIL cablaj.
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH0",
            Reading = 0.75,
            ShuntKohm = 29.9,
            GaugeFactor = 2.12,
            GaugeOhm = 120,
            Bridge = nameof(BridgeType.Half),
            HalfConfig = StrainScale.HalfConfigActivDummyT,
            Unit = "µm/m",
            ChannelScale = 91885
        });
        Assert.True(r.Passed);
        Assert.Equal(ShuntCheckKind.Info, r.Kind);
        Assert.False(r.CanApplyScale);
        Assert.True(double.IsNaN(r.ExpectedUe));
        Assert.InRange(r.MeasuredUe, 1410, 1420);
        Assert.Contains(ShuntCheck.HalfDummySkipBody, r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain(ShuntCheck.FailWiring, r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("așteptat 1893", r.StatusLine, StringComparison.Ordinal);
        Assert.Contains(ShuntCheck.ScaleInvalidLabel, r.StatusLine, StringComparison.Ordinal);
        Assert.Contains(ShuntCheck.SuggestHalfDummySkip, r.Suggestion, StringComparison.Ordinal);
        Assert.Contains(ShuntCheck.SuggestScaleAbsurd, r.Suggestion, StringComparison.Ordinal);
        Assert.DoesNotContain("91885 µm/m", r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void HalfDummy_301_IsPassSkipNotWiringFail()
    {
        // User case: two external 120 Ω, Scale ±1887, residual ~0.16 mV/V = 301 µm/m.
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH1",
            Reading = 301, // already µm/m
            ShuntKohm = 29.9,
            GaugeFactor = 2.12,
            GaugeOhm = 120,
            Bridge = nameof(BridgeType.Half),
            HalfConfig = StrainScale.HalfConfigActivDummyT,
            Unit = "µm/m",
            ChannelScale = 4000.0 / 2.12
        });
        Assert.True(r.Passed);
        Assert.Equal(ShuntCheckKind.Info, r.Kind);
        Assert.False(r.CanApplyScale);
        Assert.True(double.IsNaN(r.ExpectedUe));
        Assert.Equal(301.0, r.MeasuredUe, 3);
        Assert.Contains("Shunt CH1: 301 µm/m — " + ShuntCheck.PassHalfDummyLabel + " — "
                        + ShuntCheck.HalfDummySkipBody, r.StatusLine, StringComparison.Ordinal);
        Assert.Contains("Timbru Half+dummy T° GF=2.12 R=120", r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain(ShuntCheck.FailWiring, r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain(ShuntCheck.SuggestStepLow, r.StatusLine, StringComparison.Ordinal);
        Assert.DoesNotContain("25% jos", r.StatusLine, StringComparison.Ordinal);
        Assert.Equal(ShuntCheck.SuggestHalfDummySkip, r.Suggestion);
    }

    [Fact]
    public void HalfDummy_ElectricalResidual_AlsoPass()
    {
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH1",
            Reading = 0.16, // mV/V → ~302 µm/m at GF=2.12
            ShuntKohm = 29.9,
            GaugeFactor = 2.12,
            GaugeOhm = 120,
            Bridge = nameof(BridgeType.Half),
            HalfConfig = StrainScale.HalfConfigActivDummyT,
            Unit = "µm/m"
        });
        Assert.True(r.Passed);
        Assert.Equal(ShuntCheckKind.Info, r.Kind);
        Assert.Contains(ShuntCheck.PassHalfDummyLabel, r.StatusLine, StringComparison.Ordinal);
        Assert.InRange(r.MeasuredUe, 300, 305);
    }

    [Fact]
    public void HalfDummy_MissingRsh_StillSkipQuarter()
    {
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH1",
            Reading = 301,
            ShuntKohm = 0,
            GaugeFactor = 2.12,
            GaugeOhm = 120,
            Bridge = nameof(BridgeType.Half),
            HalfConfig = StrainScale.HalfConfigActivDummyT,
            Unit = "µm/m"
        });
        Assert.True(r.Passed);
        Assert.Equal(ShuntCheckKind.Info, r.Kind);
        Assert.DoesNotContain(ShuntCheck.MissingRsh, r.StatusLine, StringComparison.Ordinal);
        Assert.Contains(ShuntCheck.PassHalfDummyLabel, r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void Quarter_301_vs_1893_StillFailWiring()
    {
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH1",
            Reading = 301,
            ShuntKohm = 29.9,
            GaugeFactor = 2.12,
            GaugeOhm = 120,
            Bridge = nameof(BridgeType.Quarter),
            Unit = "µm/m"
        });
        Assert.False(r.Passed);
        Assert.Equal(ShuntCheckKind.Fail, r.Kind);
        Assert.Contains(ShuntCheck.FailWiring, r.StatusLine, StringComparison.Ordinal);
        Assert.InRange(r.ExpectedUe, 1890, 1896);
        Assert.Contains("așteptat 1893", r.StatusLine, StringComparison.Ordinal);
        Assert.Contains(ShuntCheck.SuggestStepLow, r.Suggestion, StringComparison.Ordinal);
    }

    [Fact]
    public void Quarter_Pin120_1893_StillPassBand()
    {
        var expected = ShuntCheck.ExpectedStrainUe(120, 29.9, 2.12, BridgeType.Quarter, null, 0.3);
        Assert.InRange(expected, 1890, 1896);
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH0",
            Reading = expected,
            ShuntKohm = 29.9,
            GaugeFactor = 2.12,
            GaugeOhm = 120,
            Bridge = nameof(BridgeType.Quarter),
            Unit = "µm/m"
        });
        Assert.True(r.Passed);
        Assert.Equal(ShuntCheckKind.Pass, r.Kind);
        Assert.Contains("PASS (±0.5%)", r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void StepLow_Vs_StepHigh_Pick_Bodies()
    {
        Assert.Equal(ShuntCheck.SuggestStepLow, ShuntCheck.PickStepBody(1415, 1893));
        Assert.Equal(ShuntCheck.SuggestStepHigh, ShuntCheck.PickStepBody(2500, 1893));
        Assert.Equal(ShuntCheck.SuggestStepFailBody, ShuntCheck.PickStepBody(1880, 1893));
    }

    [Fact]
    public void Pass_Suggests_Zero()
    {
        var r = ShuntCheck.Evaluate(Quarter350(0.875));
        Assert.True(r.Passed);
        Assert.Equal(ShuntCheck.SuggestPass, r.Suggestion);
        Assert.Contains("Sugestie: " + ShuntCheck.SuggestPass, r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void NoOmb_Spider32_Uses_Dll_Suggestion()
    {
        var r = ShuntCheck.Evaluate(new ShuntCheckRequest
        {
            ChannelName = "CH0",
            Reading = double.NaN,
            ShuntKohm = 29.9,
            GaugeFactor = 2.12,
            GaugeOhm = 120,
            Bridge = nameof(BridgeType.Half),
            HalfConfig = StrainScale.HalfConfigActivDummyT,
            Unit = "µm/m",
            BackendHint = "Spider8 [Spider32]"
        });
        Assert.Equal(ShuntCheckKind.NoReading, r.Kind);
        Assert.Equal(ShuntCheck.SuggestNoOmbDll, r.Suggestion);
        Assert.Contains("Spider32.dll", r.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ScaleAfterShunt_Refuses_Polluted_Basis()
    {
        var gf = 4000.0 / 2.12;
        var next = ShuntCheck.ScaleAfterShunt(91885, 1893, 1415, gf);
        Assert.False(StrainScale.LooksAbsurdTimbruScale(next, gf));
        Assert.InRange(next, 1800, 2800);
    }
}
