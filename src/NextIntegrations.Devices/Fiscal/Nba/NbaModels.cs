namespace NextIntegrations.Devices.Fiscal.Nba;

// Clean, public-facing result/argument types for NbaFiscalClient. These keep the wire DTOs
// (NbaDtos / NbaResponses) internal while giving callers strongly-typed, decimal-based inputs/outputs.
// The NBA "fiscalbox" document model is the same AZ e-kassa "token" model as Omnisoft — the field
// meanings mirror the Omnisoft equivalents; only the transport (ZeroMQ) and auth (toLogin) differ.

/// <summary>
/// A single sale line passed to <see cref="NbaFiscalClient"/>. Mirrors the protocol's per-item fields
/// (fiscalbox §6.1.6). <paramref name="Sum"/> should equal <c>Price * Quantity</c> less any discount, and
/// <paramref name="Price"/> should equal <c>Sum / Quantity</c> (the driver enforces this within 0.01/unit).
/// </summary>
/// <param name="Name">Display name printed on the receipt (UTF-8, ≤255, constrained charset — else code 1206).</param>
/// <param name="Code">Barcode or plain/service code.</param>
/// <param name="CodeType">How <paramref name="Code"/> is interpreted.</param>
/// <param name="Quantity">Quantity in the unit given by <paramref name="QuantityType"/> (3 decimals allowed).</param>
/// <param name="QuantityType">Measuring unit.</param>
/// <param name="Price">Final unit price (discounts applied).</param>
/// <param name="Sum">Final line total.</param>
/// <param name="VatPercent">VAT rate for the line (18/8/2/0).</param>
public sealed record NbaSaleItem(
    string Name,
    string Code,
    NbaItemCodeKind CodeType,
    decimal Quantity,
    NbaQuantityKind QuantityType,
    decimal Price,
    decimal Sum,
    decimal VatPercent);

/// <summary>Public mirror of the protocol's <c>itemCodeType</c> values (fiscalbox §6.1.6).</summary>
public enum NbaItemCodeKind
{
    /// <summary>0 — plain text / arbitrary value.</summary>
    PlainText = 0,

    /// <summary>1 — EAN-8 barcode.</summary>
    Ean8 = 1,

    /// <summary>2 — EAN-13 barcode.</summary>
    Ean13 = 2,

    /// <summary>3 — service code (e.g. a tip).</summary>
    Service = 3,
}

/// <summary>Public mirror of the protocol's <c>itemQuantityType</c> values (fiscalbox §6.1.6).</summary>
public enum NbaQuantityKind
{
    /// <summary>0 — pieces.</summary>
    Pieces = 0,

    /// <summary>1 — kilograms.</summary>
    Kilograms = 1,

    /// <summary>2 — liters.</summary>
    Liters = 2,

    /// <summary>3 — meters.</summary>
    Meters = 3,

    /// <summary>4 — square meters.</summary>
    SquareMeters = 4,

    /// <summary>5 — cube meters.</summary>
    CubeMeters = 5,
}

/// <summary>An aggregated VAT total grouped by rate (public mirror of the protocol's <c>vatAmounts</c> entry).</summary>
/// <param name="VatSum">VAT amount for the rate.</param>
/// <param name="VatPercent">The VAT rate.</param>
public sealed record NbaVatLine(decimal VatSum, decimal VatPercent);

/// <summary>
/// The payment split for a sale (fiscalbox §6.1.6). All amounts are in the document currency.
/// The device requires <c>incomingSum</c> and computes <c>changeSum = incomingSum - cashSum</c> when
/// <paramref name="Cash"/> is non-zero.
/// </summary>
/// <param name="Cash">Cash portion.</param>
/// <param name="Cashless">Card / cashless portion.</param>
/// <param name="Bonus">Bonus payment portion.</param>
/// <param name="IncomingCash">Cash tendered by the buyer; the device computes change (fiscalbox §6.1.6 "incomingSum").</param>
public sealed record NbaPayment(
    decimal Cash,
    decimal Cashless = 0m,
    decimal Bonus = 0m,
    decimal IncomingCash = 0m);

/// <summary>
/// A single interbank card transaction attached to a cashless sale (fiscalbox §6.1.6.3.2 "new method RRN").
/// Up to three banks may be listed; requires firmware ≥ 2.43.1 + driver ≥ 1.38.
/// </summary>
/// <param name="Rrn">Unique interbank transaction number (≤16 bytes; standard defines ≤12).</param>
/// <param name="Amount">The amount settled through this bank.</param>
/// <param name="Type">Whether the transaction is a purchase, reversal or refund.</param>
/// <param name="ApprovalCode">Bank approval code (required by the "new method").</param>
public sealed record NbaBankTransaction(
    string Rrn,
    decimal Amount,
    NbaTransactionKind Type = NbaTransactionKind.Purchase,
    string? ApprovalCode = null);

/// <summary>Interbank transaction kind for <see cref="NbaBankTransaction.Type"/> (fiscalbox §6.1.6.3.2).</summary>
public enum NbaTransactionKind
{
    /// <summary>A card purchase (used on a sale).</summary>
    Purchase = 0,

    /// <summary>A card reversal (used on a rollback).</summary>
    Reverse = 1,

    /// <summary>A card refund (used on a money_back).</summary>
    Refund = 2,
}

/// <summary>
/// Optional bank-settlement details for a cashless sale. Supply either the single legacy
/// <see cref="Rrn"/> ("old method") or one-to-three <see cref="Transactions"/> ("new method"),
/// not both (fiscalbox §6.1.6.3.2). When neither is set the sale carries no bank reference.
/// </summary>
/// <param name="Rrn">Legacy single interbank transaction number (≤16 bytes).</param>
/// <param name="Transactions">One-to-three per-bank transactions (new method).</param>
public sealed record NbaBankSettlement(
    string? Rrn = null,
    IReadOnlyList<NbaBankTransaction>? Transactions = null);

/// <summary>The fiscal proof returned by a createDocument (sale/deposit/…) call (fiscalbox §6.1.6).</summary>
/// <param name="DocumentId">Full fiscal identifier / SKMN (use as <c>parentDocument</c> later; also drives the QR).</param>
/// <param name="DocumentNumber">Per-token human-readable document number.</param>
/// <param name="ShiftDocumentNumber">Shift-scoped document sequence number.</param>
/// <param name="ShortDocumentId">Short fiscal identifier (printed as "Fiskal ID").</param>
/// <param name="ItemNamesNotSaved">
/// True when the device returned code 1205 — the document is fiscal and valid, but the item names could
/// not be persisted on the token. Callers may log this but must treat the document as successful.
/// </param>
public sealed record NbaFiscalResult(
    string DocumentId,
    int DocumentNumber,
    int ShiftDocumentNumber,
    string ShortDocumentId,
    bool ItemNamesNotSaved = false);

/// <summary>Token / cashbox / company information from getInfo (fiscalbox §6.1.1).</summary>
/// <param name="CompanyName">Registered company name.</param>
/// <param name="CompanyTaxNumber">Company tax (VÖEN) number.</param>
/// <param name="ObjectName">Trading object (store) name.</param>
/// <param name="ObjectAddress">Trading object address.</param>
/// <param name="ObjectTaxNumber">Object (obyekt) code.</param>
/// <param name="CashboxTaxNumber">NMQ registration number.</param>
/// <param name="CashboxFactoryNumber">Token (cashbox) factory serial.</param>
/// <param name="CashregisterFactoryNumber">Cash register factory serial (must match toLogin).</param>
/// <param name="CashregisterModel">Cash register model.</param>
/// <param name="FirmwareVersion">Fiscal driver firmware version.</param>
/// <param name="State">Token state, e.g. "ACTIVE".</param>
/// <param name="NotBefore">Certificate validity start (raw protocol string).</param>
/// <param name="NotAfter">Certificate validity end (raw protocol string).</param>
/// <param name="LastOnlineTime">Last time the register was online (updated every ~3 minutes).</param>
/// <param name="LastDocNumber">Number of the last fiscalized document.</param>
/// <param name="QrCodeUrl">e-kassa.az monitoring URL prefix (the document id is appended).</param>
public sealed record NbaDeviceInfo(
    string? CompanyName,
    string? CompanyTaxNumber,
    string? ObjectName,
    string? ObjectAddress,
    string? ObjectTaxNumber,
    string? CashboxTaxNumber,
    string? CashboxFactoryNumber,
    string? CashregisterFactoryNumber,
    string? CashregisterModel,
    string? FirmwareVersion,
    string? State,
    string? NotBefore,
    string? NotAfter,
    string? LastOnlineTime,
    int LastDocNumber,
    string? QrCodeUrl);

/// <summary>The current fiscal shift state from getShiftStatus (fiscalbox §6.1.4).</summary>
/// <param name="IsOpen">True when a shift is open.</param>
/// <param name="OpenedAt">Shift open time (raw protocol string; empty/null when closed).</param>
public sealed record NbaShiftStatus(bool IsOpen, string? OpenedAt);

/// <summary>
/// Headline totals from getXReport or closeShift (Z-Report) (fiscalbox §6 report section). The full
/// per-operation breakdown is intentionally not surfaced here yet; the fields below carry what the
/// <c>IFiscalDevice</c> port needs plus the identifiers that drive the printed report/QR.
/// </summary>
/// <param name="ReportNumber">The report (Z) number.</param>
/// <param name="DocumentId">Closing document fiscal id — empty for an X-Report.</param>
/// <param name="FirstDocNumber">First document number covered by the report.</param>
/// <param name="LastDocNumber">Last document number covered by the report.</param>
/// <param name="ShiftOpenedAt">When the reported shift opened (raw protocol string).</param>
/// <param name="CreatedAt">When the report was generated (raw protocol string).</param>
public sealed record NbaReport(
    int ReportNumber,
    string? DocumentId,
    int FirstDocNumber,
    int LastDocNumber,
    string? ShiftOpenedAt,
    string? CreatedAt);
