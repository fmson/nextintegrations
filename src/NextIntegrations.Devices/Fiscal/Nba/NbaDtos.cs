using System.Text.Json.Serialization;

namespace NextIntegrations.Devices.Fiscal.Nba;

// Request DTO records for the NBA "fiscalbox" API (ZeroMQ API v1.2 fiscalbox).
// Transport is a raw ZeroMQ (ZMTP 3.x) REQ socket; every message is a single JSON object of the shape
//   { "parameters": { ... }, "operationId": "<op>", "version": 1 }
// where the per-operation inputs live under "parameters". For createDocument the fiscal document body
// lives at parameters.data (NOT at the top level) — confirmed against the spec's concrete examples
// (fiscalbox §5 "Quick start", §6.1.6). getInfo / getLastDocument carry no "parameters" at all.
// All numeric money/quantity fields are JSON numbers (doubles) in the protocol.

/// <summary>
/// The outer message envelope: <c>parameters</c> (operation inputs, omitted for parameter-less ops),
/// <c>operationId</c> (the called operation) and <c>version</c> (always 1) (fiscalbox §3).
/// </summary>
/// <typeparam name="TParameters">The parameter payload type for this operation.</typeparam>
internal sealed record NbaEnvelope<TParameters>
{
    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TParameters? Parameters { get; init; }

    [JsonPropertyName("operationId")]
    public required string OperationId { get; init; }

    [JsonPropertyName("version")]
    public int Version { get; init; } = 1;
}

/// <summary>Parameters for <c>toLogin</c> — token authorization (fiscalbox §6.1.2).</summary>
internal sealed record NbaLoginParameters
{
    [JsonPropertyName("pin")]
    public required string Pin { get; init; }

    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("cashregister_factory_number")]
    public required string CashregisterFactoryNumber { get; init; }
}

/// <summary>
/// Parameters for the many session-bound, otherwise parameter-less operations that only carry the
/// access token: <c>toLogout</c>, <c>getShiftStatus</c>, <c>openShift</c>, <c>getXReport</c>,
/// <c>closeShift</c>, <c>getControlTape</c> (fiscalbox §6.1.3–§6.1.5).
/// </summary>
internal sealed record NbaTokenParameters
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }
}

/// <summary>
/// Parameters for <c>createDocument</c> — carries the access token, the document type, an optional
/// previous-document guard, and the fiscal document body under <c>data</c> (fiscalbox §6.1.6).
/// </summary>
internal sealed record NbaCreateDocumentParameters
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    /// <summary>"sale", "deposit", "withdraw", "rollback", "money_back", "correction", "prepay", "creditpay".</summary>
    [JsonPropertyName("doc_type")]
    public required string DocType { get; init; }

    /// <summary>
    /// Recommended guard against accidental double-punching: the previous document's
    /// <c>document_number</c>, incremented by 1 each new document (fiscalbox §6.1.6). Omitted when null.
    /// </summary>
    [JsonPropertyName("prev_doc_number")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PrevDocNumber { get; init; }

    [JsonPropertyName("data")]
    public required NbaDocumentData Data { get; init; }
}

/// <summary>
/// The fiscal document <c>data</c> body: cashier, currency, line items, payment split and VAT lines
/// (fiscalbox §6.1.6). Refund/rollback-only fields (<c>parentDocument</c>, <c>moneyBackType</c>) are
/// present but omitted when null so the one record serves sale and (future) refund/rollback documents.
/// </summary>
internal sealed record NbaDocumentData
{
    [JsonPropertyName("cashier")]
    public required string Cashier { get; init; }

    [JsonPropertyName("currency")]
    public required string Currency { get; init; }

    // The optional bank/bonus references sit between "currency" and "items" to match the spec's
    // documented field order for cashless / bonus sales (fiscalbox §6.1.6.3.2/§6.1.6.3.3).

    /// <summary>Cashless "old method" — single interbank RRN (≤16 bytes); omitted when unset.</summary>
    [JsonPropertyName("rrn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Rrn { get; init; }

    /// <summary>Cashless "new method" — one-to-three per-bank transactions; omitted when unset.</summary>
    [JsonPropertyName("transactions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<NbaTransaction>? Transactions { get; init; }

    /// <summary>Bonus payment — the bonus card number; omitted when unset (fiscalbox §6.1.6.3.3).</summary>
    [JsonPropertyName("bonusCardNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BonusCardNumber { get; init; }

    /// <summary>Sale/refund line items (omitted for cash-movement documents that have none).</summary>
    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<NbaItem>? Items { get; init; }

    /// <summary>Total to pay; <c>sum = Σ itemSum = cashSum + cashlessSum + bonusSum</c> (fiscalbox §6.1.6).</summary>
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

    /// <summary>Cash the buyer tendered; required when <c>cashSum</c> is non-zero (fiscalbox §6.1.6).</summary>
    [JsonPropertyName("incomingSum")]
    public double IncomingSum { get; init; }

    /// <summary>Change given back: <c>changeSum = incomingSum − cashSum</c>; required when <c>cashSum</c> is non-zero.</summary>
    [JsonPropertyName("changeSum")]
    public double ChangeSum { get; init; }

    [JsonPropertyName("vatAmounts")]
    public required IReadOnlyList<NbaVatAmount> VatAmounts { get; init; }

    // --- Rollback / money_back specific fields (fiscalbox §6.1.6) ---

    /// <summary>Full (not short) fiscal id of the parent sale, for rollback / money_back / creditpay.</summary>
    [JsonPropertyName("parentDocument")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ParentDocument { get; init; }

    /// <summary>money_back only — parent type: 0 sale, 6 prepay, 7 creditpay.</summary>
    [JsonPropertyName("moneyBackType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MoneyBackType { get; init; }
}

/// <summary>A single sale/refund line item (fiscalbox §6.1.6 field descriptions).</summary>
internal sealed record NbaItem
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

    /// <summary>Final unit price, discounts already applied: <c>itemPrice = itemSum / itemQuantity</c>.</summary>
    [JsonPropertyName("itemPrice")]
    public double ItemPrice { get; init; }

    [JsonPropertyName("itemSum")]
    public double ItemSum { get; init; }

    [JsonPropertyName("itemVatPercent")]
    public double ItemVatPercent { get; init; }
}

/// <summary>An aggregated VAT line (<c>vatAmounts</c> entry) grouped by <c>vatPercent</c> (fiscalbox §6.1.6).</summary>
internal sealed record NbaVatAmount
{
    [JsonPropertyName("vatSum")]
    public double VatSum { get; init; }

    [JsonPropertyName("vatPercent")]
    public double VatPercent { get; init; }
}

/// <summary>
/// A per-bank interbank transaction inside a cashless sale's <c>transactions</c> array
/// (fiscalbox §6.1.6.3.2 "new method RRN").
/// </summary>
internal sealed record NbaTransaction
{
    [JsonPropertyName("rrn")]
    public required string Rrn { get; init; }

    /// <summary>Bank approval code; omitted when unset (present in the "new method").</summary>
    [JsonPropertyName("approval_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApprovalCode { get; init; }

    [JsonPropertyName("amount")]
    public double Amount { get; init; }

    /// <summary>"PURCHASE", "REVERSE" or "REFUND".</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}
