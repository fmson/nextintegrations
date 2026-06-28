using NextIntegrations.Devices.Barcodes;
using Xunit;

namespace NextIntegrations.Tests;

public sealed class BarcodeServiceTests
{
    [Theory]
    [InlineData("400638133393", 1)] // classic EAN-13 example
    [InlineData("978014300723", 4)]
    public void Ean13CheckDigit_MatchesKnownValues(string body, int expected) =>
        Assert.Equal(expected, BarcodeService.Ean13CheckDigit(body));

    [Fact]
    public void CompleteEan13_ProducesValidCode()
    {
        string code = BarcodeService.CompleteEan13("400638133393");
        Assert.True(BarcodeService.IsValidEan13(code));
        Assert.Equal(13, code.Length);
    }

    [Theory]
    [InlineData("4006381333931", true)]
    [InlineData("4006381333930", false)] // wrong check digit
    [InlineData("40063813339", false)]   // too short
    [InlineData("abcdefghijklm", false)] // not digits
    [InlineData(null, false)]
    public void IsValidEan13_ValidatesCheckDigitAndShape(string? code, bool expected) =>
        Assert.Equal(expected, BarcodeService.IsValidEan13(code));

    [Fact]
    public void GenerateInternal_IsValid_AndUsesInStorePrefix()
    {
        string code = BarcodeService.GenerateInternal(42);
        Assert.True(BarcodeService.IsValidEan13(code));
        Assert.StartsWith("29", code, StringComparison.Ordinal);
        // Not mistaken for a variable-measure label.
        Assert.False(BarcodeService.ParseVariableMeasure(code).IsRecognized);
    }

    [Fact]
    public void BuildWeightLabel_RoundTrips_ToPluAndKilograms()
    {
        string label = BarcodeService.BuildWeightLabel(plu: 137, weightKg: 1.250m);
        Assert.True(BarcodeService.IsValidEan13(label));
        Assert.StartsWith("21", label, StringComparison.Ordinal);

        VariableMeasure parsed = BarcodeService.ParseVariableMeasure(label);
        Assert.Equal(MeasureMode.Weight, parsed.Mode);
        Assert.Equal(137, parsed.Plu);
        Assert.Equal(1.250m, parsed.WeightKg);
    }

    [Fact]
    public void BuildPriceLabel_RoundTrips_ToPluAndMinorUnits()
    {
        string label = BarcodeService.BuildPriceLabel(plu: 88, price: 4.35m);
        Assert.True(BarcodeService.IsValidEan13(label));
        Assert.StartsWith("20", label, StringComparison.Ordinal);

        VariableMeasure parsed = BarcodeService.ParseVariableMeasure(label);
        Assert.Equal(MeasureMode.Price, parsed.Mode);
        Assert.Equal(88, parsed.Plu);
        Assert.Equal(435, parsed.PriceMinorUnits);
    }

    [Fact]
    public void ParseVariableMeasure_OnOrdinaryBarcode_IsNotRecognized() =>
        Assert.False(BarcodeService.ParseVariableMeasure("4006381333931").IsRecognized);

    [Fact]
    public void Ean13Modules_Has95Modules_WithCorrectGuards()
    {
        bool[] modules = BarcodeService.Ean13Modules("4006381333931");

        Assert.Equal(95, modules.Length);
        // Start guard 1-0-1
        Assert.Equal([true, false, true], modules[..3]);
        // End guard 1-0-1
        Assert.Equal([true, false, true], modules[^3..]);
        // Centre guard 0-1-0-1-0 at modules 45..50
        Assert.Equal([false, true, false, true, false], modules[45..50]);
    }

    [Fact]
    public void Ean13Modules_OnInvalidCode_Throws() =>
        Assert.Throws<ArgumentException>(() => BarcodeService.Ean13Modules("123"));
}
