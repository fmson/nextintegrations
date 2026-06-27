namespace NextIntegrations.Devices.PriceCheck;

/// <summary>
/// The answer an in-store price-checker ("qiymət oxuma cihazı") gets for a scanned code: the product name
/// and its effective shelf price, or <see cref="Found"/> = false. App-neutral — each POS head maps its own
/// pricing result onto this so the host (and its wire format) is shared.
/// </summary>
public sealed record PriceCheckResult(bool Found, string Sku, string Name, long PriceMinor, string Unit, string Currency)
{
    public static PriceCheckResult NotFound { get; } = new(false, string.Empty, string.Empty, 0, string.Empty, "AZN");

    /// <summary>The price in major units (e.g. 60 qəpik → 0.60).</summary>
    public decimal Price => PriceMinor / 100m;
}

/// <summary>
/// Resolves a scanned barcode/SKU to a <see cref="PriceCheckResult"/>. <see cref="PriceCheckHost"/> requires
/// this; the consuming app supplies it (reading from its own catalog / store-price rules).
/// </summary>
public delegate Task<PriceCheckResult> PriceLookup(string code, CancellationToken cancellationToken = default);
