namespace NextIntegrations.Devices.Fiscal.Omnisoft;

// Clean, public-facing result/argument types for OmnisoftFiscalClient. These keep the wire DTOs
// (OmnisoftDtos / OmnisoftResponses) internal while giving callers strongly-typed inputs/outputs.

/// <summary>
/// A single sale/refund line passed to <see cref="OmnisoftFiscalClient"/>. Mirrors the protocol's
/// per-item fields (PDF p.8, p.21). <paramref name="Sum"/> must equal <c>Price * Quantity</c> less
/// discount, and <paramref name="Price"/> must equal <c>Sum / Quantity</c> (PDF p.21).
/// </summary>
/// <param name="Name">Display name printed on the receipt.</param>
/// <param name="Code">Barcode or service/plain code.</param>
/// <param name="CodeType">How <paramref name="Code"/> is interpreted.</param>
/// <param name="Quantity">Quantity in the unit given by <paramref name="QuantityType"/>.</param>
/// <param name="QuantityType">Measuring unit.</param>
/// <param name="Price">Final unit price (discounts applied).</param>
/// <param name="Sum">Final line total.</param>
/// <param name="VatPercent">VAT rate for the line.</param>
/// <param name="Discount">Line discount amount.</param>
public sealed record OmnisoftSaleItem(
    string Name,
    string Code,
    OmnisoftItemCodeKind CodeType,
    decimal Quantity,
    OmnisoftQuantityKind QuantityType,
    decimal Price,
    decimal Sum,
    decimal VatPercent,
    decimal Discount = 0m);

/// <summary>Public mirror of the protocol's <c>itemCodeType</c> values (PDF p.21).</summary>
public enum OmnisoftItemCodeKind
{
    /// <summary>Plain text / arbitrary value.</summary>
    PlainText = 0,

    /// <summary>EAN-8 barcode.</summary>
    Ean8 = 1,

    /// <summary>EAN-13 barcode.</summary>
    Ean13 = 2,

    /// <summary>Service code.</summary>
    Service = 3,
}

/// <summary>Public mirror of the protocol's <c>itemQuantityType</c> values (PDF p.21).</summary>
public enum OmnisoftQuantityKind
{
    /// <summary>Pieces.</summary>
    Pieces = 0,

    /// <summary>Kilograms.</summary>
    Kilograms = 1,

    /// <summary>Liters.</summary>
    Liters = 2,

    /// <summary>Meters.</summary>
    Meters = 3,

    /// <summary>Square meters.</summary>
    SquareMeters = 4,

    /// <summary>Cube meters.</summary>
    CubeMeters = 5,
}

/// <summary>An aggregated VAT total grouped by rate (public mirror of the protocol's <c>vatAmounts</c> entry) (PDF p.8, p.21).</summary>
/// <param name="VatSum">VAT amount for the rate.</param>
/// <param name="VatPercent">The VAT rate.</param>
public sealed record OmnisoftVatLine(decimal VatSum, decimal VatPercent);

/// <summary>The payment split for a sale or refund (PDF p.21). All amounts are in the document currency.</summary>
/// <param name="Cash">Cash portion.</param>
/// <param name="Cashless">Card / cashless portion.</param>
/// <param name="Bonus">Bonus payment portion.</param>
/// <param name="IncomingCash">Cash tendered by the buyer; the device computes change (PDF p.21).</param>
public sealed record OmnisoftPayment(
    decimal Cash,
    decimal Cashless = 0m,
    decimal Bonus = 0m,
    decimal IncomingCash = 0m);

/// <summary>The fiscal proof returned by a Sale, Refund or Receipt-copy (PDF p.24 §6.1.4.8).</summary>
/// <param name="DocumentNumber">Per-shift human-readable document number.</param>
/// <param name="LongId">Full fiscal identifier (use as parentDocument/fiscalId later).</param>
/// <param name="ShortId">Short fiscal identifier (use as refund_short_document_id later).</param>
/// <param name="ShiftDocumentNumber">Shift-scoped document sequence number.</param>
public sealed record OmnisoftFiscalResult(
    int DocumentNumber,
    string LongId,
    string ShortId,
    int ShiftDocumentNumber);

/// <summary>Token / cashbox / company information from Get info (PDF p.17, p.19).</summary>
/// <param name="CompanyName">Registered company name.</param>
/// <param name="CompanyTaxNumber">Company tax (VÖEN) number.</param>
/// <param name="CashboxFactoryNumber">Token (cashbox) factory serial.</param>
/// <param name="CashregisterFactoryNumber">Cash register factory serial.</param>
/// <param name="FirmwareVersion">Fiscal driver firmware version.</param>
/// <param name="LastDocNumber">Number of the last fiscalized document.</param>
/// <param name="State">Token state, e.g. "ACTIVE".</param>
/// <param name="NotBefore">Certificate validity start (raw protocol string).</param>
/// <param name="NotAfter">Certificate validity end (raw protocol string).</param>
/// <param name="QrCodeUrl">e-kassa.az monitoring URL prefix.</param>
public sealed record OmnisoftDeviceInfo(
    string? CompanyName,
    string? CompanyTaxNumber,
    string? CashboxFactoryNumber,
    string? CashregisterFactoryNumber,
    string? FirmwareVersion,
    int LastDocNumber,
    string? State,
    string? NotBefore,
    string? NotAfter,
    string? QrCodeUrl);

/// <summary>The current fiscal shift state from Shift status (PDF p.20).</summary>
/// <param name="IsOpen">True when a shift is open.</param>
/// <param name="Serial">Operated unit serial number.</param>
/// <param name="OpenedAt">Shift open time (raw protocol string; empty when closed).</param>
public sealed record OmnisoftShiftStatus(bool IsOpen, string? Serial, string? OpenedAt);

/// <summary>
/// Totals from an X-Report or Z-Report (PDF p.29, p.31). The full per-currency breakdown is available
/// via <paramref name="Currencies"/>; the scalar fields surface the headline numbers.
/// </summary>
/// <param name="ReportNumber">The report (Z) number.</param>
/// <param name="DocumentId">Closing document fiscal id — empty for an X-Report (PDF p.29 vs p.31).</param>
/// <param name="FirstDocNumber">First document number covered by the report.</param>
/// <param name="LastDocNumber">Last document number covered by the report.</param>
/// <param name="ShiftOpenedAt">When the reported shift opened (raw protocol string).</param>
/// <param name="CreatedAt">When the report was generated (raw protocol string).</param>
/// <param name="Currencies">Per-currency totals as returned by the device.</param>
public sealed record OmnisoftReport(
    int ReportNumber,
    string? DocumentId,
    int FirstDocNumber,
    int LastDocNumber,
    string? ShiftOpenedAt,
    string? CreatedAt,
    IReadOnlyList<OmnisoftReportCurrencyTotals> Currencies);

/// <summary>Per-currency totals exposed from a report (subset of the protocol's currency block) (PDF p.29).</summary>
/// <param name="Currency">Currency code.</param>
/// <param name="SaleCount">Number of sales.</param>
/// <param name="SaleSum">Total sale amount.</param>
/// <param name="SaleCashSum">Cash portion of sales.</param>
/// <param name="SaleCashlessSum">Cashless portion of sales.</param>
/// <param name="MoneyBackCount">Number of refunds.</param>
/// <param name="MoneyBackSum">Total refunded amount.</param>
/// <param name="DepositSum">Total cash deposited.</param>
/// <param name="WithdrawSum">Total cash withdrawn.</param>
public sealed record OmnisoftReportCurrencyTotals(
    string? Currency,
    int SaleCount,
    decimal SaleSum,
    decimal SaleCashSum,
    decimal SaleCashlessSum,
    int MoneyBackCount,
    decimal MoneyBackSum,
    decimal DepositSum,
    decimal WithdrawSum);
