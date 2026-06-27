using NextIntegrations.Devices.PriceCheck;
using Xunit;

namespace NextIntegrations.Tests;

/// <summary>The shared price-checker HTTP host — serves a supplied <see cref="PriceLookup"/> over the LAN.</summary>
public sealed class PriceCheckHostTests
{
    private static PriceLookup StubLookup() => (code, _) =>
        Task.FromResult(code == "111"
            ? new PriceCheckResult(true, "MA001", "Çörək", 60, "əd", "AZN")
            : PriceCheckResult.NotFound);

    [Fact]
    public async Task ServesJson_ForKnownBarcode()
    {
        using PriceCheckHost host = new(StubLookup(), port: 0);
        host.Start();
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };

        string json = await http.GetStringAsync($"http://127.0.0.1:{host.Port}/price?barcode=111");

        Assert.Contains("\"found\":true", json, StringComparison.Ordinal);
        Assert.Contains("Çörək", json, StringComparison.Ordinal);
        Assert.Contains("\"priceMinor\":60", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServesTwoLineText_ForDumbDisplays()
    {
        using PriceCheckHost host = new(StubLookup(), port: 0);
        host.Start();
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };

        string text = await http.GetStringAsync($"http://127.0.0.1:{host.Port}/price?barcode=111&fmt=text");

        Assert.Equal("Çörək\n0.60 AZN", text);
    }

    [Fact]
    public async Task UnknownBarcode_ReportsNotFound()
    {
        using PriceCheckHost host = new(StubLookup(), port: 0);
        host.Start();
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };

        string json = await http.GetStringAsync($"http://127.0.0.1:{host.Port}/price?barcode=NOPE");

        Assert.Contains("\"found\":false", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthEndpoint_Responds()
    {
        using PriceCheckHost host = new(StubLookup(), port: 0);
        host.Start();
        using HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };

        string body = await http.GetStringAsync($"http://127.0.0.1:{host.Port}/health");

        Assert.Contains("OK", body, StringComparison.Ordinal);
    }
}
