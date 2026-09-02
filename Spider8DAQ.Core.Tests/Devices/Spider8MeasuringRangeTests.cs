using Spider8DAQ.Core.Devices;
using Xunit;

namespace Spider8DAQ.Core.Tests.Devices;

public class Spider8MeasuringRangeTests
{
    [Fact]
    public void P15_forces_DcVoltage_even_if_bridge_left_Full()
    {
        var ch = new ChannelConfig
        {
            Bridge = BridgeType.Full,
            Unit = "bar",
            Capacity = 200,
            RangeMvPerV = 2,
            ExcitationV = 2.5,
            SensorName = "HBM P15RVA/1/200B",
            SensorId = "s0254"
        };
        Assert.Equal(BridgeType.DcVoltage, Spider8MeasuringRange.ResolveAcquisitionBridge(ch));
    }

    [Fact]
    public void Strain_gauge_pressure_stays_Full()
    {
        var ch = new ChannelConfig
        {
            Bridge = BridgeType.Full,
            Unit = "bar",
            Capacity = 200,
            RangeMvPerV = 2,
            ExcitationV = 10,
            SensorName = "Presiune 200 bar rel",
            SensorCategory = "Presiune"
        };
        Assert.Equal(BridgeType.Full, Spider8MeasuringRange.ResolveAcquisitionBridge(ch));
    }

    [Fact]
    public void Amplified_zero_excitation_bar_uses_DcVoltage()
    {
        var ch = new ChannelConfig
        {
            Bridge = BridgeType.Full,
            Unit = "bar",
            Capacity = 200,
            RangeMvPerV = 10,
            ExcitationV = 0,
            SensorName = "Process 0-10V"
        };
        Assert.Equal(BridgeType.DcVoltage, Spider8MeasuringRange.ResolveAcquisitionBridge(ch));
    }

    [Fact]
    public void DcVoltage_FS_ignores_engineering_capacity()
    {
        // Capacity=200 bar must NOT become 0.1 V or 1 V ASA.
        var fs = Spider8MeasuringRange.ResolveElectricalFullScale(BridgeType.DcVoltage, 10, 200, "bar");
        Assert.Equal(10.0, fs);
        Assert.Equal(Spider8MeasuringRange.Asa10V0, Spider8MeasuringRange.ToAsaRangeCode(BridgeType.DcVoltage, fs));
    }

    [Fact]
    public void Strain_FS_never_uses_sensitivity_2_as_OMB_range()
    {
        var ch = new ChannelConfig { Bridge = BridgeType.Full, RangeMvPerV = 2, Capacity = 5000, Unit = "N" };
        var fs = Spider8MeasuringRange.ResolveProgrammedElectricalFs(ch, BridgeType.Full);
        Assert.Equal(3.0, fs);
    }

    [Fact]
    public void P15_Apply_keeps_zero_excitation_and_DcVoltage()
    {
        var sensor = new Spider8DAQ.Core.Sensors.SensorDefinition
        {
            Code = "P15RVA/1/200B",
            Name = "HBM P15RVA/1/200B",
            Unit = "bar",
            Scale = 20,
            Bridge = "DcVoltage",
            Capacity = 200,
            Sensitivity = 1,
            ExcitationV = 0,
            FilterHz = 20,
            RangeMvPerV = 10,
            Category = "Presiune"
        };
        var ch = new ChannelConfig { Index = 1, Bridge = BridgeType.Full, ExcitationV = 2.5 };
        var r = Spider8DAQ.Core.Sensors.SensorApplyHelper.Apply(ch, sensor);
        Assert.Equal(BridgeType.DcVoltage, r.Bridge);
        Assert.Equal(0, ch.ExcitationV);
        Assert.Equal(10.0, ch.RangeMvPerV);
        Assert.Equal(20.0, ch.Scale);
        Assert.Equal(BridgeType.DcVoltage, Spider8MeasuringRange.ResolveAcquisitionBridge(ch));
        Assert.Equal(Spider8MeasuringRange.Asa10V0,
            Spider8MeasuringRange.ToAsaRangeCode(BridgeType.DcVoltage, ch.RangeMvPerV));
    }
}
