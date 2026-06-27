using System.Text.Json.Serialization;

namespace NextIntegrations.Devices.Fiscal.Omnisoft;

// Request/response DTO records for the Omnisoft / "Omnicashier" fiscal API (OmniTech TPS575).
// JSON over HTTP POST to http://<device-ip>:8989/v2. Every shape below is transcribed from the
// PDF spec "Omnisoft API V2.1 (release 12.04.2022)". Page citations are given per type.
// All numeric money/quantity fields are JSON numbers (doubles) in the protocol.

/// <summary>
/// Operation discriminator (<c>check_type</c>) for every Omnisoft API call (PDF p.20 §6.1.5,
/// p.36 §5 "Quick start"). Each value is the wire integer sent under <c>checkData.check_type</c>.
/// </summary>
internal enum OmnisoftCheckType
{
    /// <summary>Sale receipt — <c>doc_type</c> "sale" (PDF p.8, p.20).</summary>
    Sale = 1,

    /// <summary>Cash deposit into the drawer (PDF p.7, p.20).</summary>
    Deposit = 7,

    /// <summary>Cash withdrawal from the drawer (PDF p.9, p.20).</summary>
    Withdraw = 8,

    /// <summary>Cancel a fiscalized sale within the open shift, by <c>fiscalId</c> (long_id) (PDF p.9, p.23).</summary>
    Rollback = 10,

    /// <summary>Reprint a receipt copy by <c>fiscalId</c> (long_id) (PDF p.10, p.28).</summary>
    ReceiptCopy = 11,

    /// <summary>X-Report — mid-shift totals, does not close the shift (PDF p.10, p.28).</summary>
    XReport = 12,

    /// <summary>Close shift &amp; Z-Report — ends the fiscal day (PDF p.10, p.30).</summary>
    CloseShiftZReport = 13,

    /// <summary>Shift status / unit information (PDF p.7, p.20).</summary>
    ShiftStatus = 14,

    /// <summary>Open fiscal shift (PDF p.7, p.20).</summary>
    OpenShift = 15,

    /// <summary>Reprint after a printing error (PDF p.9, p.28).</summary>
    ReprintAfterError = 16,

    /// <summary>Correction document — non-inclusion correction (PDF p.10, p.24).</summary>
    Correction = 19,

    /// <summary>Open the money box (cashbox / drawer) over the fiscal API (PDF p.18).</summary>
    OpenMoneyBox = 28,

    /// <summary>Close the money box (cashbox / drawer) over the fiscal API (PDF p.18).</summary>
    CloseMoneyBox = 29,

    /// <summary>Login / authorization — returns <c>access_token</c> (PDF p.7, p.20).</summary>
    Login = 40,

    /// <summary>Get info about token and last document number (PDF p.7, p.17/19).</summary>
    GetInfo = 41,

    /// <summary>Money back / refund — <c>doc_type</c> "money_back" (PDF p.9, p.23).</summary>
    MoneyBack = 100,
}

/// <summary>Item code interpretation for <c>itemCode</c> (PDF p.21 §6.1.7 "itemCodeType").</summary>
internal enum OmnisoftItemCodeType
{
    /// <summary>0 — plain text / arbitrary value.</summary>
    PlainText = 0,

    /// <summary>1 — EAN-8 barcode.</summary>
    Ean8 = 1,

    /// <summary>2 — EAN-13 barcode.</summary>
    Ean13 = 2,

    /// <summary>3 — service code.</summary>
    Service = 3,

    /// <summary>5 — credit payment.</summary>
    Credit = 5,
}

/// <summary>Measuring unit for <c>itemQuantityType</c> (PDF p.21 §6.1.7).</summary>
internal enum OmnisoftQuantityType
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

// ---------------------------------------------------------------------------
// Common building blocks (used inside requests)
// ---------------------------------------------------------------------------

/// <summary>A single sale/refund line item (PDF p.8 "Sale", p.21 §6.1.7 field descriptions).</summary>
internal sealed record OmnisoftItem
{
    [JsonPropertyName("itemName")]
    public required string ItemName { get; init; }

    [JsonPropertyName("itemCodeType")]
    public int ItemCodeType { get; init; }

    [JsonPropertyName("itemCode")]
    public required string ItemCode { get; init; }

    [JsonPropertyName("itemQuantityType")]
    public int ItemQuantityType { get; init; }

    [JsonPropertyName("itemQuantity")]
    public double ItemQuantity { get; init; }

    /// <summary>Final unit price, discounts already applied: <c>itemPrice = itemSum / itemQuantity</c> (PDF p.21).</summary>
    [JsonPropertyName("itemPrice")]
    public double ItemPrice { get; init; }

    [JsonPropertyName("itemSum")]
    public double ItemSum { get; init; }

    [JsonPropertyName("itemVatPercent")]
    public double ItemVatPercent { get; init; }

    [JsonPropertyName("discount")]
    public double Discount { get; init; }
}

/// <summary>An aggregated VAT line (<c>vatAmounts</c> entry) grouped by <c>vatPercent</c> (PDF p.8, p.21).</summary>
internal sealed record OmnisoftVatAmount
{
    [JsonPropertyName("vatSum")]
    public double VatSum { get; init; }

    [JsonPropertyName("vatPercent")]
    public double VatPercent { get; init; }
}

/// <summary>An extra printed receipt field (<c>receiptDetails</c> entry) (PDF p.8, p.23 §6.1.9).</summary>
internal sealed record OmnisoftReceiptDetail
{
    /// <summary>Type: 0 raw, 1 key/value, 2 barcode, 3 qr-code (PDF p.23).</summary>
    [JsonPropertyName("t")]
    public int T { get; init; }

    [JsonPropertyName("k")]
    public string K { get; init; } = string.Empty;

    [JsonPropertyName("v")]
    public string V { get; init; } = string.Empty;
}

// ---------------------------------------------------------------------------
// Request envelopes
// ---------------------------------------------------------------------------

/// <summary>
/// Bare request used by token-less / parameter-less operations
/// (Login, Get info, Shift status, Open shift, X-Report, Close shift, reprint, drawer open/close).
/// Shape: <c>{ "requestData": { "checkData": { "check_type": N }, ... } }</c> (PDF p.7, p.10, p.19, p.20).
/// </summary>
internal sealed record OmnisoftSimpleRequest
{
    [JsonPropertyName("requestData")]
    public required OmnisoftSimpleRequestData RequestData { get; init; }
}

/// <summary>Inner <c>requestData</c> for <see cref="OmnisoftSimpleRequest"/> (PDF p.7, p.20).</summary>
internal sealed record OmnisoftSimpleRequestData
{
    /// <summary>Session key; omitted only on the Login request (PDF p.7).</summary>
    [JsonPropertyName("access_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AccessToken { get; init; }

    [JsonPropertyName("checkData")]
    public required OmnisoftCheckData CheckData { get; init; }

    /// <summary>Login only — API-grade account username (PDF p.7, p.20).</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    /// <summary>Login only — API-grade account password (PDF p.7, p.20).</summary>
    [JsonPropertyName("password")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Password { get; init; }

    /// <summary>Rollback / receipt-copy only — the parent receipt's <c>long_id</c> (PDF p.9, p.10, p.23).</summary>
    [JsonPropertyName("fiscalId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FiscalId { get; init; }
}

/// <summary>The <c>checkData</c> object carried by every request (PDF p.5 §3).</summary>
internal sealed record OmnisoftCheckData
{
    [JsonPropertyName("check_type")]
    public required int CheckType { get; init; }
}

/// <summary>
/// Document-creating request (Sale, Money back, Correction, Deposit, Withdraw).
/// Shape: <c>{ "requestData": { "access_token", "int_ref"?, "tokenData": { "parameters": {...} },
/// "checkData": { "check_type" }, "receiptDetails"? } }</c> (PDF p.8 "Sale", p.9 "Moneyback", p.10 "Correction").
/// </summary>
internal sealed record OmnisoftDocumentRequest
{
    [JsonPropertyName("requestData")]
    public required OmnisoftDocumentRequestData RequestData { get; init; }
}

/// <summary>Inner <c>requestData</c> for a document-creating request (PDF p.8).</summary>
internal sealed record OmnisoftDocumentRequestData
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>Optional client-side transaction id, echoed back in Transaction history (PDF p.8, p.23 §6.1.9).</summary>
    [JsonPropertyName("int_ref")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntRef { get; init; }

    [JsonPropertyName("tokenData")]
    public required OmnisoftTokenData TokenData { get; init; }

    [JsonPropertyName("checkData")]
    public required OmnisoftCheckData CheckData { get; init; }

    /// <summary>Up to four extra printed fields (PDF p.8, p.23 §6.1.9).</summary>
    [JsonPropertyName("receiptDetails")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OmnisoftReceiptDetail>? ReceiptDetails { get; init; }
}

/// <summary>The <c>tokenData</c> wrapper around document parameters (PDF p.5 §3, p.8).</summary>
internal sealed record OmnisoftTokenData
{
    [JsonPropertyName("parameters")]
    public required OmnisoftParameters Parameters { get; init; }

    /// <summary>"createDocument" for sale/refund/correction (PDF p.5 §3, p.8).</summary>
    [JsonPropertyName("operationId")]
    public string OperationId { get; init; } = "createDocument";

    /// <summary>API version — always 1 (PDF p.5 §3).</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;
}

/// <summary>The <c>parameters</c> object: document type plus the <c>data</c> body (PDF p.8, p.21).</summary>
internal sealed record OmnisoftParameters
{
    /// <summary>"sale", "money_back", "correction" (PDF p.8, p.9, p.10, p.21 §6.1.5).</summary>
    [JsonPropertyName("doc_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DocType { get; init; }

    /// <summary>Optional previous document number, used for ordering checks (PDF p.8 "prev_doc_number").</summary>
    [JsonPropertyName("prev_doc_number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PrevDocNumber { get; init; }

    [JsonPropertyName("data")]
    public required OmnisoftDocumentData Data { get; init; }
}

/// <summary>
/// The fiscal document <c>data</c> body: cashier, currency, items, payment split and VAT lines
/// (PDF p.8 "Sale", p.9 "Moneyback", p.10 "Correction", p.21 §6.1.7 field descriptions).
/// </summary>
internal sealed record OmnisoftDocumentData
{
    [JsonPropertyName("cashier")]
    public required string Cashier { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    /// <summary>Sale/refund line items (omitted for Correction, which has no items) (PDF p.8, p.10).</summary>
    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OmnisoftItem>? Items { get; init; }

    /// <summary>Total to pay; <c>sum = cashSum + cashlessSum + bonusSum</c> (PDF p.21).</summary>
    [JsonPropertyName("sum")]
    public double Sum { get; init; }

    [JsonPropertyName("cashSum")]
    public double CashSum { get; init; }

    [JsonPropertyName("cashlessSum")]
    public double CashlessSum { get; init; }

    [JsonPropertyName("prepaymentSum")]
    public double PrepaymentSum { get; init; }

    [JsonPropertyName("creditSum")]
    public double CreditSum { get; init; }

    [JsonPropertyName("bonusSum")]
    public double BonusSum { get; init; }

    /// <summary>Cash the buyer tendered; the device computes change automatically (PDF p.3, p.21 "incomingSum").</summary>
    [JsonPropertyName("incomingSum")]
    public double IncomingSum { get; init; }

    [JsonPropertyName("vatAmounts")]
    public required IReadOnlyList<OmnisoftVatAmount> VatAmounts { get; init; }

    // --- Refund (money_back) specific fields (PDF p.9, p.23) ---

    /// <summary>Refund only — full <c>long_id</c> of the original sale receipt (PDF p.9, p.23).</summary>
    [JsonPropertyName("parentDocument")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentDocument { get; init; }

    /// <summary>Refund only — original sale document number (PDF p.9, p.23).</summary>
    [JsonPropertyName("refund_document_number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefundDocumentNumber { get; init; }

    /// <summary>Refund only — <c>short_id</c> of the original sale document (PDF p.9, p.24).</summary>
    [JsonPropertyName("refund_short_document_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefundShortDocumentId { get; init; }

    /// <summary>Refund only — when the parent operation occurred (PDF p.9, may be empty).</summary>
    [JsonPropertyName("lastOperationAtUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastOperationAtUtc { get; init; }

    // --- Correction specific fields (PDF p.10, p.24) ---

    /// <summary>Correction only — start of the corrected interval (PDF p.10, p.24).</summary>
    [JsonPropertyName("firstOperationAtUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FirstOperationAtUtc { get; init; }
}

/// <summary>
/// Deposit / Withdraw request body (just a cash sum) (PDF p.7 "Deposit", p.9 "Withdraw", p.23 §6.1.4.1).
/// Shape: <c>{ "requestData": { "access_token", "tokenData": { "parameters": { "data": { "cashSum" } } },
/// "checkData": { "check_type" } } }</c>.
/// </summary>
internal sealed record OmnisoftCashMovementRequest
{
    [JsonPropertyName("requestData")]
    public required OmnisoftCashMovementRequestData RequestData { get; init; }
}

/// <summary>Inner <c>requestData</c> for a deposit/withdraw (PDF p.7, p.9).</summary>
internal sealed record OmnisoftCashMovementRequestData
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("tokenData")]
    public required OmnisoftCashMovementTokenData TokenData { get; init; }

    [JsonPropertyName("checkData")]
    public required OmnisoftCheckData CheckData { get; init; }
}

/// <summary><c>tokenData</c> for a deposit/withdraw — only <c>parameters.data.cashSum</c> (PDF p.7).</summary>
internal sealed record OmnisoftCashMovementTokenData
{
    [JsonPropertyName("parameters")]
    public required OmnisoftCashMovementParameters Parameters { get; init; }
}

/// <summary><c>parameters</c> for a deposit/withdraw (PDF p.7).</summary>
internal sealed record OmnisoftCashMovementParameters
{
    [JsonPropertyName("data")]
    public required OmnisoftCashSum Data { get; init; }
}

/// <summary>The single <c>cashSum</c> carried by a deposit/withdraw (PDF p.7, p.9).</summary>
internal sealed record OmnisoftCashSum
{
    [JsonPropertyName("cashSum")]
    public double CashSum { get; init; }
}
