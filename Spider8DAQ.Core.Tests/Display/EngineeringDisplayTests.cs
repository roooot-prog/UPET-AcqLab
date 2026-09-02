using System.Globalization;
using Spider8DAQ.Core.Display;
using Xunit;

namespace Spider8DAQ.Core.Tests.Display;

public class EngineeringDisplayTests
{
    private static readonly CultureInfo Ro = CultureInfo.GetCultureInfo("ro-RO");
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Fact]
    public void FormatScale_RoundsToOneDecimal_DoesNotDumpFullDouble()
    {
        const double raw = 1886.7924528301885;
        Assert.Equal("1886.8", EngineeringDisplay.FormatScale(raw, Inv));
        Assert.Equal("1886,8", EngineeringDisplay.FormatScale(raw, Ro));
        var exact = EngineeringDisplay.FormatScale(raw, Inv, fullPrecision: true);
        Assert.StartsWith("1886.79", exact);
        Assert.NotEqual("1886.8", exact);
    }

    [Fact]
    public void FormatReading_Strain_OneDecimal()
    {
        Assert.Equal("12.3", EngineeringDisplay.FormatReading(12.34, "µm/m", Inv));
        Assert.Equal("12,3", EngineeringDisplay.FormatReading(12.34, "µm/m", Ro));
        Assert.Equal("0.0", EngineeringDisplay.FormatReading(0.04, "µm/m", Inv));
        Assert.Equal("—", EngineeringDisplay.FormatReading(double.NaN, "µm/m", Inv));
    }

    [Fact]
    public void FormatReading_AlwaysOneDecimal_CultureAware()
    {
        Assert.Equal("770.0", EngineeringDisplay.FormatReading(770.04, "bar", Inv));
        Assert.Equal("1.2", EngineeringDisplay.FormatReading(1.2344, "mV/V", Inv));
        Assert.Equal("1,2", EngineeringDisplay.FormatReading(1.2344, "mV/V", Ro));
        Assert.Equal("12,3", EngineeringDisplay.FormatRange(12.34, Ro));
    }
}
