namespace NextIntegrations.Devices.Fiscal.Nba;

/// <summary>
/// Configuration for the NBA "fiscalbox" integration: the ZeroMQ endpoint, the token login credentials
/// (<c>pin</c>/<c>role</c>/<c>cashregister_factory_number</c>), and the defaults used to map a completed
/// sale onto the protocol's item/payment/VAT fields (the domain model does not carry every fiscal value).
/// </summary>
/// <remarks>
/// The endpoint is a raw ZeroMQ (ZMTP 3.x) REQ socket — <c>tcp://&lt;ip&gt;:26767</c> in production
/// (a local connection is recommended). It is <em>not</em> HTTP; a URL such as
/// <c>http://host:6730/api/v1</c> handed out for a test unit is still just <c>tcp://host:6730</c>.
/// </remarks>
public sealed record NbaFiscalOptions
{
    /// <summary>ZeroMQ endpoint host (IP or hostname of the fiscalbox), e.g. <c>127.0.0.1</c> or <c>81.21.87.10</c>.</summary>
    public required string Host { get; init; }

    /// <summary>ZeroMQ endpoint port. The fiscalbox default is 26767.</summary>
    public int Port { get; init; } = 26767;

    /// <summary>Token pin code (up to 3 wrong attempts lock the token — fiscalbox §6.1.2).</summary>
    public required string Pin { get; init; }

    /// <summary>Login role — "user" to operate the token (fiscalbox §6.1.2).</summary>
    public string Role { get; init; } = "user";

    /// <summary>
    /// Cash register factory/serial number; must match the value in the token certificate (from getInfo).
    /// Required by <c>toLogin</c> to prevent unauthorized token replacement (fiscalbox §6.1.2).
    /// </summary>
    public required string CashregisterFactoryNumber { get; init; }

    /// <summary>Per-request timeout for the ZeroMQ round-trip. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Cashier name used when a sale has no cashier id. Defaults to "Cashier".</summary>
    public string DefaultCashierName { get; init; } = "Cashier";

    /// <summary>Document currency used when a sale does not carry one. Defaults to "AZN".</summary>
    public string DefaultCurrency { get; init; } = "AZN";

    /// <summary>How a line's SKU is interpreted on the wire. Defaults to plain text (fiscalbox §6.1.6).</summary>
    public NbaItemCodeKind DefaultItemCodeType { get; init; } = NbaItemCodeKind.PlainText;

    /// <summary>VAT percent assumed when a line has VAT but the rate cannot be reconstructed. Defaults to 18 (AZ standard).</summary>
    public decimal DefaultVatPercent { get; init; } = 18m;

    /// <summary>The ZeroMQ connect address derived from <see cref="Host"/> and <see cref="Port"/>.</summary>
    public string Address => $"tcp://{Host}:{Port}";
}
