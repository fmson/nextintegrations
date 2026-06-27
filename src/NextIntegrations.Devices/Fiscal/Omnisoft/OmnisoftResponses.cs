using System.Text.Json.Serialization;

namespace NextIntegrations.Devices.Fiscal.Omnisoft;

// Response DTO records for the Omnisoft / "Omnicashier" fiscal API.
// Envelope: { "code": <int>, "data": {...}, "message": "...", "info": "..." } where code == 0 means
// success (PDF p.5 §3, p.58 §9). Login is the one exception: it returns the access_token and
// message "login success" (PDF p.20). Page citations are given per type.

/// <summary>
/// Login response — carries the session <c>access_token</c> used in all later calls (PDF p.7, p.20).
/// Note the doc example shows <c>code: 1, message: "login success"</c>, so success here is signalled
/// by the presence of <c>access_token</c>, not by <c>code == 0</c>.
/// </summary>
internal sealed record OmnisoftLoginResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }

    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// Generic envelope for calls that only report success/failure (Open shift, Reprint, drawer
/// open/close) (PDF p.20, p.28, p.18). <c>code == 0</c> = success.
/// </summary>
internal sealed record OmnisoftAckResponse
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("info")]
    public string? Info { get; init; }
}

/// <summary>
/// Get info response — token / cashbox / company details and the last document number (PDF p.17, p.19).
/// </summary>
internal sealed record OmnisoftInfoResponse
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("data")]
    public OmnisoftInfoData? Data { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>Token information payload returned by Get info (PDF p.17, p.19).</summary>
internal sealed record OmnisoftInfoData
{
    [JsonPropertyName("cashbox_factory_number")]
    public string? CashboxFactoryNumber { get; init; }

    [JsonPropertyName("cashbox_tax_number")]
    public string? CashboxTaxNumber { get; init; }

    [JsonPropertyName("cashregister_factory_number")]
    public string? CashregisterFactoryNumber { get; init; }

    [JsonPropertyName("cashregister_model")]
    public string? CashregisterModel { get; init; }

    [JsonPropertyName("company_name")]
    public string? CompanyName { get; init; }

    [JsonPropertyName("company_tax_number")]
    public string? CompanyTaxNumber { get; init; }

    [JsonPropertyName("firmware_version")]
    public string? FirmwareVersion { get; init; }

    [JsonPropertyName("last_doc_number")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int LastDocNumber { get; init; }

    [JsonPropertyName("last_online_time")]
    public string? LastOnlineTime { get; init; }

    /// <summary>Certificate validity upper bound — enforced by the token (PDF p.19, errors 706/718).</summary>
    [JsonPropertyName("not_after")]
    public string? NotAfter { get; init; }

    /// <summary>Certificate validity lower bound (PDF p.19).</summary>
    [JsonPropertyName("not_before")]
    public string? NotBefore { get; init; }

    [JsonPropertyName("object_address")]
    public string? ObjectAddress { get; init; }

    [JsonPropertyName("object_name")]
    public string? ObjectName { get; init; }

    [JsonPropertyName("object_tax_number")]
    public string? ObjectTaxNumber { get; init; }

    /// <summary>e-kassa.az monitoring URL prefix; the document hash is appended (PDF p.17, p.19).</summary>
    [JsonPropertyName("qr_code_url")]
    public string? QrCodeUrl { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }
}

/// <summary>Shift status response — open/closed state and the operated unit's serial (PDF p.20).</summary>
internal sealed record OmnisoftShiftStatusResponse
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("desc")]
    public string? Desc { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("serial")]
    public string? Serial { get; init; }

    [JsonPropertyName("shiftStatus")]
    public bool ShiftStatus { get; init; }

    [JsonPropertyName("shift_open_time")]
    public string? ShiftOpenTime { get; init; }
}

/// <summary>
/// Document (sale / refund / receipt-copy) response — the fiscal proof of registration (PDF p.24 §6.1.4.8).
/// <c>code == 0</c> = success; <c>long_id</c>/<c>short_id</c> identify the document for later refund/rollback.
/// </summary>
internal sealed record OmnisoftDocumentResponse
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    /// <summary>Per-shift human-readable document number (PDF p.24).</summary>
    [JsonPropertyName("document_number")]
    public int DocumentNumber { get; init; }

    /// <summary>Full fiscal identifier — used as <c>parentDocument</c>/<c>fiscalId</c> later (PDF p.24).</summary>
    [JsonPropertyName("long_id")]
    public string? LongId { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>Shift-scoped document sequence number (PDF p.24).</summary>
    [JsonPropertyName("shift_document_number")]
    public int ShiftDocumentNumber { get; init; }

    /// <summary>Short fiscal identifier — used as <c>refund_short_document_id</c> later (PDF p.24).</summary>
    [JsonPropertyName("short_id")]
    public string? ShortId { get; init; }
}

/// <summary>
/// X-Report (check_type 12) and Z-Report (check_type 13) response — identical shape; on the Z-Report
/// the <c>document_id</c> is populated (PDF p.29 X-Report, p.30–31 Z-Report).
/// </summary>
internal sealed record OmnisoftReportResponse
{
    /// <summary>Z-counter (0 on X-Report) (PDF p.29).</summary>
    [JsonPropertyName("_z")]
    public int Z { get; init; }

    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("data")]
    public OmnisoftReportData? Data { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>Report totals payload (PDF p.29 X-Report, p.30–31 Z-Report).</summary>
internal sealed record OmnisoftReportData
{
    [JsonPropertyName("createdAtUtc")]
    public string? CreatedAtUtc { get; init; }

    [JsonPropertyName("currencies")]
    public IReadOnlyList<OmnisoftReportCurrency>? Currencies { get; init; }

    [JsonPropertyName("docCountToSend")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int DocCountToSend { get; init; }

    /// <summary>Empty on X-Report; the closing document's fiscal id on the Z-Report (PDF p.29 vs p.31).</summary>
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("firstDocNumber")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int FirstDocNumber { get; init; }

    [JsonPropertyName("lastDocNumber")]
    public int LastDocNumber { get; init; }

    /// <summary>The report (Z) number (PDF p.29, p.31).</summary>
    [JsonPropertyName("reportNumber")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int ReportNumber { get; init; }

    [JsonPropertyName("shiftOpenAtUtc")]
    public string? ShiftOpenAtUtc { get; init; }
}

/// <summary>
/// Per-currency totals inside a report, broken down by operation type
/// (sale / moneyBack / rollback / correction / deposit / withdraw / prepay / creditpay) (PDF p.29, p.30).
/// </summary>
internal sealed record OmnisoftReportCurrency
{
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    // Sale totals
    [JsonPropertyName("saleCount")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int SaleCount { get; init; }

    [JsonPropertyName("saleSum")]
    public double SaleSum { get; init; }

    [JsonPropertyName("saleCashSum")]
    public double SaleCashSum { get; init; }

    [JsonPropertyName("saleCashlessSum")]
    public double SaleCashlessSum { get; init; }

    [JsonPropertyName("saleBonusSum")]
    public double SaleBonusSum { get; init; }

    [JsonPropertyName("saleCreditSum")]
    public double SaleCreditSum { get; init; }

    [JsonPropertyName("salePrepaymentSum")]
    public double SalePrepaymentSum { get; init; }

    [JsonPropertyName("saleVatAmounts")]
    public IReadOnlyList<OmnisoftVatAmount>? SaleVatAmounts { get; init; }

    // Money-back (refund) totals
    [JsonPropertyName("moneyBackCount")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int MoneyBackCount { get; init; }

    [JsonPropertyName("moneyBackSum")]
    public double MoneyBackSum { get; init; }

    [JsonPropertyName("moneyBackCashSum")]
    public double MoneyBackCashSum { get; init; }

    [JsonPropertyName("moneyBackCashlessSum")]
    public double MoneyBackCashlessSum { get; init; }

    [JsonPropertyName("moneyBackVatAmounts")]
    public IReadOnlyList<OmnisoftVatAmount>? MoneyBackVatAmounts { get; init; }

    // Cash drawer movements
    [JsonPropertyName("depositCount")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int DepositCount { get; init; }

    [JsonPropertyName("depositSum")]
    public double DepositSum { get; init; }

    [JsonPropertyName("withdrawCount")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int WithdrawCount { get; init; }

    [JsonPropertyName("withdrawSum")]
    public double WithdrawSum { get; init; }

    // Rollback (cancellation) totals
    [JsonPropertyName("rollbackCount")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int RollbackCount { get; init; }

    [JsonPropertyName("rollbackSum")]
    public double RollbackSum { get; init; }

    // Correction totals
    [JsonPropertyName("correctionCount")]
    [JsonConverter(typeof(FlexibleIntConverter))]
    public int CorrectionCount { get; init; }

    [JsonPropertyName("correctionSum")]
    public double CorrectionSum { get; init; }
}
