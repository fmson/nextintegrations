using System.Globalization;
using NextIntegrations.Common;
using Xunit;

namespace NextIntegrations.Tests;

public sealed class HorecaFormatsTests
{
    [Fact]
    public void DatePattern_DefaultsToDayMonthYear()
    {
        // No config (or the default config) → dd/MM/yyyy.
        Assert.Equal("dd/MM/yyyy", new HorecaFormatConfig().DateFormat);
    }

    [Fact]
    public void Date_UsesConfiguredPattern_InvariantCulture()
    {
        var value = new DateTimeOffset(2026, 6, 26, 16, 22, 0, TimeSpan.Zero);
        Assert.Equal(value.ToString(HorecaFormats.DatePattern, CultureInfo.InvariantCulture), HorecaFormats.Date(value));
    }

    [Fact]
    public void Time_UsesConfiguredPattern()
    {
        var value = new DateTimeOffset(2026, 6, 26, 16, 22, 0, TimeSpan.Zero);
        Assert.Equal(value.ToString(HorecaFormats.TimePattern, CultureInfo.InvariantCulture), HorecaFormats.Time(value));
    }
}
