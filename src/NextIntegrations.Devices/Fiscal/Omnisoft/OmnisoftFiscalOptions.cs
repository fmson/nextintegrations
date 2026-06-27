namespace NextIntegrations.Devices.Fiscal.Omnisoft;

/// <summary>
/// Configuration for the Omnisoft fiscal integration: the device address, API credentials, and the
/// defaults used to map a a completed sale onto the protocol's
/// item/payment/VAT fields (the domain model does not carry every fiscal-specific value).
/// </summary>
public sealed record OmnisoftFiscalOptions
{
    /// <summary>
    /// Device base address, e.g. <c>http://192.168.1.103:8989/</c> (note the trailing slash; the client
    /// appends <c>v2</c>) (PDF p.3 §2.1).
    /// </summary>
    public required Uri BaseAddress { get; init; }

    /// <summary>API-grade account username. Test units accept <c>SuperApi</c> (PDF p.3 §2.1.1).</summary>
    public required string UserName { get; init; }

    /// <summary>API-grade account password. Test units accept <c>123</c> (PDF p.3 §2.1.1).</summary>
    public required string Password { get; init; }

    /// <summary>Per-request timeout for the HTTP client. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Cashier name used when a sale has no cashier id. Defaults to "Cashier".</summary>
    public string DefaultCashierName { get; init; } = "Cashier";

    /// <summary>How a line's SKU is interpreted on the wire. Defaults to plain text (PDF p.21).</summary>
    public OmnisoftItemCodeKind DefaultItemCodeType { get; init; } = OmnisoftItemCodeKind.PlainText;

    /// <summary>VAT percent assumed when a line has VAT but the rate cannot be reconstructed. Defaults to 18 (AZ standard).</summary>
    public decimal DefaultVatPercent { get; init; } = 18m;
}
