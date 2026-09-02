using Spider8DAQ.Core.Sensors;
using Xunit;

namespace Spider8DAQ.Core.Tests.Sensors;

public class Spider8InternalShuntTests
{
    [Theory]
    [InlineData("HBM,Spider8-30,F07462,P24", true)]
    [InlineData("\"HBM,Spider8-30,F07462,P24\"", true)]
    [InlineData("IDN: HBM,Spider8-30,F00111,P24", true)]
    [InlineData("0,5057,1,5058", true)] // AID CF motherboard Spider8-30 / SR30
    [InlineData("HBM,Spider8-55/01,F00197,P24", false)]
    [InlineData("HBM,Spider8,F00197,P24", false)]
    [InlineData("HBM,Spider8-01,F00001,P24", false)]
    [InlineData("SIM-2.0", false)]
    [InlineData("simulator", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("Spider32.dll", false)]
    public void Detects_Spider8_30_From_Firmware(string? idn, bool expected)
    {
        Assert.Equal(expected, Spider8InternalShunt.IsSpider830(idn));
    }

    [Fact]
    public void GaugeOhm_Maps_Documented_Pin_Rsh()
    {
        Assert.Equal(29.9, Spider8InternalShunt.KohmForGaugeOhm(120), 6);
        Assert.Equal(87.35, Spider8InternalShunt.KohmForGaugeOhm(350), 6);
        Assert.Equal(175.0, Spider8InternalShunt.KohmForGaugeOhm(700), 6);
        Assert.Equal(87.35, Spider8InternalShunt.KohmForGaugeOhm(0), 6);
        Assert.Equal(29.9, Spider8InternalShunt.KohmForGaugeOhm(130), 6);
        Assert.Equal(175.0, Spider8InternalShunt.KohmForGaugeOhm(680), 6);
    }

    [Fact]
    public void Resolve_Spider830_Uses_Model_Table_Not_100k()
    {
        var r = Spider8InternalShunt.Resolve("HBM,Spider8-30,F07462,P24", 350);
        Assert.Equal(Spider8InternalShuntKind.Spider830, r.Kind);
        Assert.True(r.CanAutoFill);
        Assert.True(r.FromLiveIdn);
        Assert.Equal("live-idn+model-table", r.Source);
        Assert.Equal(87.35, r.Kohm, 6);
        Assert.DoesNotContain("100", r.StatusLabel, StringComparison.Ordinal);
        Assert.Equal("Rsh = 87.35 kΩ (intern Spider8-30)", r.StatusLabel);
    }

    [Fact]
    public void Resolve_Simulator_Does_Not_Fill()
    {
        var r = Spider8InternalShunt.Resolve("SIM-2.0", 350);
        Assert.Equal(Spider8InternalShuntKind.Simulator, r.Kind);
        Assert.False(r.CanAutoFill);
        Assert.Equal(0, r.Kohm);
        Assert.Contains("simulator", r.StatusLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_Unknown_Model_Does_Not_Guess()
    {
        var r = Spider8InternalShunt.Resolve("HBM,Spider8-55/01,F00197,P24", 350);
        Assert.Equal(Spider8InternalShuntKind.Unknown, r.Kind);
        Assert.False(r.CanAutoFill);
    }

    [Fact]
    public void Quarter_350_Official_Rsh_Matches_1mV_Within_Half_Percent()
    {
        var expected = ShuntCheck.ExpectedStrainUe(
            350, Spider8InternalShunt.For350OhmKohm, 2.0, Core.Devices.BridgeType.Quarter, null, 0.3);
        var measuredFrom1mV = ShuntCheck.ElectricalToStrainUe(1.0, 2.0, Core.Devices.BridgeType.Quarter, null, 0.3);
        Assert.Equal(2000.0, measuredFrom1mV, 6);
        Assert.True(ShuntCheck.InTolerance(measuredFrom1mV, expected, 0.5));
    }
}
