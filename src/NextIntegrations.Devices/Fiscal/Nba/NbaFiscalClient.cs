using System.Text.Json;

namespace NextIntegrations.Devices.Fiscal.Nba;

/// <summary>
/// Client for the NBA "fiscalbox" API (online NKA / e-kassa.az) spoken over a raw ZeroMQ REQ socket via
/// <see cref="INbaTransport"/>. Each call is one JSON round-trip of the shape
/// <c>{ "parameters": {…}, "operationId": "&lt;op&gt;", "version": 1 }</c> → <c>{ "code", "data", "message", "info" }</c>
/// (ZeroMQ API v1.2 fiscalbox §3, §5, §6). Call <see cref="LoginAsync"/> first (except <see cref="GetInfoAsync"/>,
/// which needs no auth); the returned <c>access_token</c> is cached and attached to every subsequent call.
/// </summary>
/// <remarks>
/// Richer document types (rollback, money_back, correction, prepay, creditpay) and the periodic/control
/// reports live on the protocol but are not all surfaced yet — the subset here covers the <c>IFiscalDevice</c>
/// port plus admin test-connection and shift management. Not thread-safe for concurrent calls that share the
/// cached token; a fiscal register is a single-threaded device and the transport serializes round-trips.
/// </remarks>
public sealed class NbaFiscalClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // Items are named in Azerbaijani/Russian — UTF-8 is mandatory. Keep property names verbatim
        // (DTOs carry JsonPropertyName); disabling the camelCase policy avoids surprises.
        PropertyNamingPolicy = null,
    };

    private readonly INbaTransport _transport;
    private string? _accessToken;

    /// <summary>Creates a client over a transport (ZeroMQ in production; a double in tests).</summary>
    /// <param name="transport">The request/response transport to the fiscalbox.</param>
    public NbaFiscalClient(INbaTransport transport)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
    }

    /// <summary>True once <see cref="LoginAsync"/> has cached an access token.</summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    /// <summary>
    /// Gets token / cashbox / company registration information. Needs no authorization, so it is the
    /// natural "test connection" probe for the admin configuration screen (fiscalbox §6.1.1).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<NbaDeviceInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        NbaResponse<NbaInfoData> response =
            await SendAsync<object, NbaInfoData>("getInfo", parameters: null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response.Code, response.Message, response.Info);

        NbaInfoData d = response.Data
            ?? throw new NbaFiscalException(response.Code, "getInfo returned no data");
        return new NbaDeviceInfo(
            d.CompanyName,
            d.CompanyTaxNumber,
            d.ObjectName,
            d.ObjectAddress,
            d.ObjectTaxNumber,
            d.CashboxTaxNumber,
            d.CashboxFactoryNumber,
            d.CashregisterFactoryNumber,
            d.CashregisterModel,
            d.FirmwareVersion,
            d.State,
            d.NotBefore,
            d.NotAfter,
            d.LastOnlineTime,
            d.LastDocNumber,
            d.QrCodeUrl);
    }

    /// <summary>
    /// Authorizes in the token and caches the returned <c>access_token</c> for later calls (fiscalbox §6.1.2).
    /// Up to 3 wrong pins lock the token (error 405); the factory number must match the token certificate.
    /// </summary>
    /// <param name="pin">Token pin code.</param>
    /// <param name="role">Login role (typically "user").</param>
    /// <param name="cashregisterFactoryNumber">Cash register factory/serial number (must match the certificate).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session access token (also cached internally).</returns>
    public async Task<string> LoginAsync(
        string pin,
        string role,
        string cashregisterFactoryNumber,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(pin);
        ArgumentException.ThrowIfNullOrEmpty(role);
        ArgumentException.ThrowIfNullOrEmpty(cashregisterFactoryNumber);

        NbaLoginParameters parameters = new()
        {
            Pin = pin,
            Role = role,
            CashregisterFactoryNumber = cashregisterFactoryNumber,
        };

        NbaResponse<NbaLoginData> response =
            await SendAsync<NbaLoginParameters, NbaLoginData>("toLogin", parameters, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response.Code, response.Message, response.Info);

        string? token = response.Data?.AccessToken;
        if (string.IsNullOrEmpty(token))
        {
            throw new NbaFiscalException(response.Code, response.Message ?? "toLogin returned no access_token");
        }

        _accessToken = token;
        return token;
    }

    /// <summary>Exits the token authorization session and clears the cached token (fiscalbox §6.1.3).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        NbaResponse<object> response = await SendAsync<NbaTokenParameters, object>(
            "toLogout",
            new NbaTokenParameters { AccessToken = RequireToken() },
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response.Code, response.Message, response.Info);
        _accessToken = null;
    }

    /// <summary>Returns the current shift state — open/closed and open time (fiscalbox §6.1.4).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<NbaShiftStatus> GetShiftStatusAsync(CancellationToken cancellationToken = default)
    {
        NbaResponse<NbaShiftStatusData> response = await SendAsync<NbaTokenParameters, NbaShiftStatusData>(
            "getShiftStatus",
            new NbaTokenParameters { AccessToken = RequireToken() },
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response.Code, response.Message, response.Info);
        return new NbaShiftStatus(response.Data?.ShiftOpen ?? false, response.Data?.ShiftOpenTime);
    }

    /// <summary>Opens a fiscal shift (fiscalbox §6.1.5).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task OpenShiftAsync(CancellationToken cancellationToken = default)
    {
        NbaResponse<object> response = await SendAsync<NbaTokenParameters, object>(
            "openShift",
            new NbaTokenParameters { AccessToken = RequireToken() },
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response.Code, response.Message, response.Info);
    }

    /// <summary>
    /// Registers a sale (<c>doc_type</c> "sale") and returns its fiscal proof (fiscalbox §6.1.6).
    /// </summary>
    /// <param name="cashier">Cashier name printed on the receipt.</param>
    /// <param name="currency">Document currency code, e.g. "AZN".</param>
    /// <param name="items">Sale line items (at least one).</param>
    /// <param name="payment">Payment split (cash/cashless/bonus + tendered cash).</param>
    /// <param name="vatAmounts">VAT totals grouped by rate; computed from <paramref name="items"/> when null.</param>
    /// <param name="bankSettlement">Optional cashless bank reference (single RRN or per-bank transactions).</param>
    /// <param name="bonusCardNumber">Optional bonus card number when part of the sale is paid by bonuses.</param>
    /// <param name="previousDocumentNumber">Optional anti-double-punch guard (previous <c>document_number</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<NbaFiscalResult> SaleAsync(
        string cashier,
        string currency,
        IReadOnlyList<NbaSaleItem> items,
        NbaPayment payment,
        IReadOnlyList<NbaVatLine>? vatAmounts = null,
        NbaBankSettlement? bankSettlement = null,
        string? bonusCardNumber = null,
        int? previousDocumentNumber = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(cashier);
        ArgumentException.ThrowIfNullOrEmpty(currency);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(payment);
        if (items.Count == 0)
        {
            throw new ArgumentException("A sale must contain at least one item.", nameof(items));
        }

        IReadOnlyList<NbaItem> wireItems = MapItems(items);
        IReadOnlyList<NbaVatAmount> vat = MapVat(vatAmounts) ?? AggregateVat(items);
        decimal goodsSum = items.Sum(static i => i.Sum);
        decimal changeSum = payment.IncomingCash > payment.Cash ? payment.IncomingCash - payment.Cash : 0m;

        NbaCreateDocumentParameters parameters = new()
        {
            AccessToken = RequireToken(),
            DocType = "sale",
            PrevDocNumber = previousDocumentNumber,
            Data = new NbaDocumentData
            {
                Cashier = cashier,
                Currency = currency,
                Rrn = bankSettlement?.Rrn,
                Transactions = MapTransactions(bankSettlement?.Transactions),
                BonusCardNumber = string.IsNullOrWhiteSpace(bonusCardNumber) ? null : bonusCardNumber,
                Items = wireItems,
                Sum = ToDouble(goodsSum),
                CashSum = ToDouble(payment.Cash),
                CashlessSum = ToDouble(payment.Cashless),
                BonusSum = ToDouble(payment.Bonus),
                PrepaymentSum = 0,
                CreditSum = 0,
                IncomingSum = ToDouble(payment.IncomingCash),
                ChangeSum = ToDouble(changeSum),
                VatAmounts = vat,
            },
        };

        return await CreateDocumentAsync(parameters, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Generates an X-Report — mid-shift totals without closing the shift (fiscalbox §6, getXReport).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<NbaReport> GetXReportAsync(CancellationToken cancellationToken = default) =>
        ReportAsync("getXReport", cancellationToken);

    /// <summary>Closes the fiscal shift and returns the Z-Report — ends the fiscal day (fiscalbox §6, closeShift).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<NbaReport> CloseShiftAsync(CancellationToken cancellationToken = default) =>
        ReportAsync("closeShift", cancellationToken);

    private async Task<NbaReport> ReportAsync(string operationId, CancellationToken cancellationToken)
    {
        NbaResponse<NbaReportData> response = await SendAsync<NbaTokenParameters, NbaReportData>(
            operationId,
            new NbaTokenParameters { AccessToken = RequireToken() },
            cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response.Code, response.Message, response.Info);

        NbaReportData d = response.Data
            ?? throw new NbaFiscalException(response.Code, $"{operationId} returned no data");
        return new NbaReport(
            d.ReportNumber,
            d.DocumentId,
            d.FirstDocNumber,
            d.LastDocNumber,
            d.ShiftOpenAtUtc,
            d.CreatedAtUtc);
    }

    private async Task<NbaFiscalResult> CreateDocumentAsync(
        NbaCreateDocumentParameters parameters,
        CancellationToken cancellationToken)
    {
        NbaResponse<NbaCreateDocumentData> response = await SendAsync<NbaCreateDocumentParameters, NbaCreateDocumentData>(
            "createDocument", parameters, cancellationToken).ConfigureAwait(false);

        // Code 1205: the document was created and IS fiscal, but item names could not be saved on the token.
        // The spec returns the full data payload in this case — treat it as success and flag it (fiscalbox §6.1.6).
        bool itemNamesNotSaved = response.Code == 1205;
        if (response.Code != 0 && !itemNamesNotSaved)
        {
            throw new NbaFiscalException(response.Code, response.Message, response.Info);
        }

        NbaCreateDocumentData d = response.Data
            ?? throw new NbaFiscalException(response.Code, "createDocument returned no data");
        return new NbaFiscalResult(
            d.DocumentId ?? string.Empty,
            d.DocumentNumber,
            d.ShiftDocumentNumber,
            d.ShortDocumentId ?? string.Empty,
            itemNamesNotSaved);
    }

    private async Task<NbaResponse<TData>> SendAsync<TParameters, TData>(
        string operationId,
        TParameters? parameters,
        CancellationToken cancellationToken)
    {
        NbaEnvelope<TParameters> envelope = new()
        {
            OperationId = operationId,
            Parameters = parameters,
            Version = 1,
        };

        string requestJson = JsonSerializer.Serialize(envelope, SerializerOptions);
        string responseJson = await _transport.SendReceiveAsync(requestJson, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new NbaFiscalException("NBA fiscalbox returned an empty response body.");
        }

        try
        {
            return JsonSerializer.Deserialize<NbaResponse<TData>>(responseJson, SerializerOptions)
                ?? throw new NbaFiscalException("NBA fiscalbox returned a null response body.");
        }
        catch (JsonException ex)
        {
            throw new NbaFiscalException($"NBA fiscalbox returned an unparseable response: {ex.Message}");
        }
    }

    private string RequireToken() =>
        _accessToken ?? throw new NbaFiscalException("Not authenticated: call LoginAsync first.");

    private static void EnsureSuccess(int code, string? message, string? info)
    {
        if (code != 0)
        {
            throw new NbaFiscalException(code, message, info);
        }
    }

    private NbaItem[] MapItems(IReadOnlyList<NbaSaleItem> items) =>
        items.Select(static i => new NbaItem
        {
            ItemName = i.Name,
            ItemCodeType = (int)i.CodeType,
            ItemCode = i.Code,
            ItemQuantityType = (int)i.QuantityType,
            ItemQuantity = ToDouble(i.Quantity),
            ItemPrice = ToDouble(i.Price),
            ItemSum = ToDouble(i.Sum),
            ItemVatPercent = ToDouble(i.VatPercent),
        }).ToArray();

    private static NbaVatAmount[]? MapVat(IReadOnlyList<NbaVatLine>? vatAmounts) =>
        vatAmounts?.Select(static v => new NbaVatAmount
        {
            VatSum = ToDouble(v.VatSum),
            VatPercent = ToDouble(v.VatPercent),
        }).ToArray();

    private static NbaVatAmount[] AggregateVat(IReadOnlyList<NbaSaleItem> items) =>
        items
            .GroupBy(static i => i.VatPercent)
            .Select(static g => new NbaVatAmount
            {
                VatSum = ToDouble(g.Sum(static i => i.Sum)),
                VatPercent = ToDouble(g.Key),
            })
            .ToArray();

    private static NbaTransaction[]? MapTransactions(IReadOnlyList<NbaBankTransaction>? transactions) =>
        transactions is null || transactions.Count == 0
            ? null
            : transactions.Select(static t => new NbaTransaction
            {
                Rrn = t.Rrn,
                ApprovalCode = t.ApprovalCode,
                Amount = ToDouble(t.Amount),
                Type = t.Type switch
                {
                    NbaTransactionKind.Reverse => "REVERSE",
                    NbaTransactionKind.Refund => "REFUND",
                    _ => "PURCHASE",
                },
            }).ToArray();

    private static double ToDouble(decimal value) => (double)value;
}
