using System.Text.Json.Serialization;

namespace NextIntegrations.Devices.Fiscal.Nba;

// Response DTO records for the NBA "fiscalbox" API.
// Envelope: { "code": <int>, "data": {...}, "message": "...", "info": "..." } where code == 0 means
// success for every operation (including toLogin, which returns the access_token inside "data").
// Page citations refer to ZeroMQ API v1.2 fiscalbox.

/// <summary>The shared response envelope carried by every operation (fiscalbox §3).</summary>
/// <typeparam name="TData">The <c>data</c> payload type for this operation (may be absent).</typeparam>
internal sealed record NbaResponse<TData>
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("data")]
    public TData? Data { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("info")]
    public string? Info { get; init; }
}

/// <summary>The <c>data</c> payload of a <c>toLogin</c> response — carries the session access token (fiscalbox §6.1.2).</summary>
internal sealed record NbaLoginData
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }
}

/// <summary>Token information payload returned by <c>getInfo</c> (fiscalbox §6.1.1).</summary>
internal sealed record NbaInfoData
{
    [JsonPropertyName("company_tax_number")]
    public string? CompanyTaxNumber { get; init; }

    [JsonPropertyName("company_name")]
    public string? CompanyName { get; init; }

    [JsonPropertyName("object_tax_number")]
    public string? ObjectTaxNumber { get; init; }

    [JsonPropertyName("object_name")]
    public string? ObjectName { get; init; }

    [JsonPropertyName("object_address")]
    public string? ObjectAddress { get; init; }

    [JsonPropertyName("cashbox_tax_number")]
    public string? CashboxTaxNumber { get; init; }

    [JsonPropertyName("cashbox_factory_number")]
    public string? CashboxFactoryNumber { get; init; }

    [JsonPropertyName("firmware_version")]
    public string? FirmwareVersion { get; init; }

    [JsonPropertyName("cashregister_factory_number")]
    public string? CashregisterFactoryNumber { get; init; }

    [JsonPropertyName("cashregister_model")]
    public string? CashregisterModel { get; init; }

    /// <summary>e-kassa.az monitoring URL prefix; the document id is appended for the receipt QR.</summary>
    [JsonPropertyName("qr_code_url")]
    public string? QrCodeUrl { get; init; }

    /// <summary>Certificate validity lower bound (fiscalbox §6.1.1).</summary>
    [JsonPropertyName("not_before")]
    public string? NotBefore { get; init; }

    /// <summary>Certificate validity upper bound — enforced by the token (fiscalbox §6.1.1).</summary>
    [JsonPropertyName("not_after")]
    public string? NotAfter { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    /// <summary>Last time the register was online (updated ~every 3 minutes) (fiscalbox §6.1.1 comment).</summary>
    [JsonPropertyName("last_online_time")]
    public string? LastOnlineTime { get; init; }

    [JsonPropertyName("last_doc_number")]
    [JsonConverter(typeof(NbaFlexibleIntConverter))]
    public int LastDocNumber { get; init; }
}

/// <summary>The <c>data</c> payload of a <c>getShiftStatus</c> response (fiscalbox §6.1.4).</summary>
internal sealed record NbaShiftStatusData
{
    [JsonPropertyName("shift_open")]
    public bool ShiftOpen { get; init; }

    [JsonPropertyName("shift_open_time")]
    public string? ShiftOpenTime { get; init; }
}

/// <summary>
/// The <c>data</c> payload of a <c>createDocument</c> response — the fiscal proof of registration
/// (fiscalbox §6.1.6). <c>document_id</c> is the full fiscal identifier used for later rollback/refund.
/// </summary>
internal sealed record NbaCreateDocumentData
{
    /// <summary>Full fiscal identifier / SKMN — used as <c>parentDocument</c> later; drives the receipt QR.</summary>
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    /// <summary>Per-token human-readable document number (fiscalbox §6.1.6).</summary>
    [JsonPropertyName("document_number")]
    [JsonConverter(typeof(NbaFlexibleIntConverter))]
    public int DocumentNumber { get; init; }

    /// <summary>Shift-scoped document sequence number (fiscalbox §6.1.6).</summary>
    [JsonPropertyName("shift_document_number")]
    [JsonConverter(typeof(NbaFlexibleIntConverter))]
    public int ShiftDocumentNumber { get; init; }

    /// <summary>Short fiscal identifier — printed as "Fiskal ID" (fiscalbox §9).</summary>
    [JsonPropertyName("short_document_id")]
    public string? ShortDocumentId { get; init; }
}

/// <summary>
/// The <c>data</c> payload of an <c>getXReport</c> / <c>closeShift</c> (Z-Report) response
/// (fiscalbox §6 report section). Only the headline / identifier fields are modelled; the full
/// per-operation breakdown is tolerated and ignored.
/// </summary>
internal sealed record NbaReportData
{
    /// <summary>Empty on an X-Report; the closing document's fiscal id on the Z-Report.</summary>
    [JsonPropertyName("document_id")]
    public string? DocumentId { get; init; }

    [JsonPropertyName("reportNumber")]
    [JsonConverter(typeof(NbaFlexibleIntConverter))]
    public int ReportNumber { get; init; }

    [JsonPropertyName("firstDocNumber")]
    [JsonConverter(typeof(NbaFlexibleIntConverter))]
    public int FirstDocNumber { get; init; }

    [JsonPropertyName("lastDocNumber")]
    [JsonConverter(typeof(NbaFlexibleIntConverter))]
    public int LastDocNumber { get; init; }

    [JsonPropertyName("shiftOpenAtUtc")]
    public string? ShiftOpenAtUtc { get; init; }

    [JsonPropertyName("createdAtUtc")]
    public string? CreatedAtUtc { get; init; }
}
