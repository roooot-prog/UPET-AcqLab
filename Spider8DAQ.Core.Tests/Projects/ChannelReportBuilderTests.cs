using Spider8DAQ.Core.Devices;
using Spider8DAQ.Core.Projects;
using Spider8DAQ.Core.Sensors;
using Xunit;

namespace Spider8DAQ.Core.Tests.Projects;

public class ChannelReportBuilderTests
{
    [Fact]
    public void FromChannels_UsesEnabledRecorded_AndEnrichesFromCatalog()
    {
        var channels = new List<ChannelConfig>
        {
            new()
            {
                Index = 0, Name = "F", Unit = "N", Enabled = true, RecordEnabled = true,
                SensorId = "u2b1", SensorName = "U2B Force", Scale = 10, ExcitationV = 2.5,
                Bridge = BridgeType.Full, Capacity = 50_000
            },
            new()
            {
                Index = 1, Name = "Off", Unit = "N", Enabled = false, RecordEnabled = true,
                SensorName = "ignored"
            },
            new()
            {
                Index = 2, Name = "LiveOnly", Unit = "mV/V", Enabled = true, RecordEnabled = false
            }
        };
        var catalog = new[]
        {
            new SensorDefinition
            {
                Id = "u2b1", Code = "U2B-50k", Name = "U2B Force", Category = "Forță",
                Sensitivity = 2.0, TransducerType = "Load cell", Notes = "HBM U2B"
            }
        };

        var report = ChannelReportBuilder.FromChannels(channels, catalog);
        Assert.Single(report);
        Assert.Equal("F", report[0].Name);
        Assert.Equal("U2B-50k", report[0].SensorCode);
        Assert.Equal(2.0, report[0].Sensitivity);
        Assert.Equal("HBM U2B", report[0].SensorNotes);
        Assert.Contains("U2B", ChannelReportBuilder.BuildSensorSummary(report));
    }
}
