using System.Globalization;
using Spider8DAQ.Core.Display;
using Xunit;

namespace Spider8DAQ.Core.Tests.Display;

public class LiveCitireTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Fact]
    public void Off_channel_shows_em_dash_not_leftover_strain()
    {
        var text = LiveCitire.FormatOrPlaceholder(
            0.408, "µm/m",
            enabled: false, overflow: false, linkLost: false, inScan: true, hasSetup: true, Inv);
        Assert.Equal("—", text);
        Assert.False(LiveCitire.ShouldShowEngineeringValue(false, false, false, true, true, 0.408));
    }

    [Fact]
    public void Overflow_and_link_lost_and_out_of_scan_hide_number()
    {
        Assert.Equal("—", LiveCitire.FormatOrPlaceholder(12.3, "µm/m", true, true, false, true, true, Inv));
        Assert.Equal("—", LiveCitire.FormatOrPlaceholder(12.3, "µm/m", true, false, true, true, true, Inv));
        Assert.Equal("—", LiveCitire.FormatOrPlaceholder(12.3, "µm/m", true, false, false, false, true, Inv));
        Assert.Equal("—", LiveCitire.FormatOrPlaceholder(double.NaN, "µm/m", true, false, false, true, true, Inv));
    }

    [Fact]
    public void Empty_unused_On_hides_open_bridge_scaled_noise()
    {
        Assert.False(LiveCitire.HasMeasurementSetup(null, null, 0, null, "Full", 0, "mV/V", "CH2"));
        Assert.False(LiveCitire.HasMeasurementSetup("off", null, 0, null, "Half", 0, "µm/m", "CH0"));
        var text = LiveCitire.FormatOrPlaceholder(
            188.7, "µm/m",
            enabled: true, overflow: false, linkLost: false, inScan: true,
            hasSetup: false, Inv);
        Assert.Equal("—", text);
    }

    [Fact]
    public void Half_dummy_Timbru_near_zero_stays_visible()
    {
        Assert.True(LiveCitire.HasMeasurementSetup(
            "Timbru activ + pasiv (compensare T°)", null, 2.12, "ActivDummyT", "Half", 2000, "µm/m", "CH1"));
        var text = LiveCitire.FormatOrPlaceholder(
            0.408, "µm/m",
            enabled: true, overflow: false, linkLost: false, inScan: true, hasSetup: true, Inv);
        Assert.Equal("0.4", text);
    }

    [Fact]
    public void U2B_and_DcVoltage_count_as_setup_even_at_zero()
    {
        Assert.True(LiveCitire.HasMeasurementSetup("U2B 5kN", "u2b-5kn", 0, null, "Full", 5000, "N", "CH2"));
        Assert.True(LiveCitire.HasMeasurementSetup(null, null, 0, null, "DcVoltage", 0, "bar", "CH3"));
        Assert.Equal("0.0", LiveCitire.FormatOrPlaceholder(
            0.04, "N", true, false, false, true, true, Inv));
    }

    [Fact]
    public void GaugeFactor_or_HalfConfig_is_enough_without_catalog_name()
    {
        Assert.True(LiveCitire.HasMeasurementSetup(null, null, 2.0, null, "Half", 0, "µm/m", "CH1"));
        Assert.True(LiveCitire.HasMeasurementSetup(null, null, 0, "Simplu", "Half", 0, "µm/m", "CH1"));
    }
}
