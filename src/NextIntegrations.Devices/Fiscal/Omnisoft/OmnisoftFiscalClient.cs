using System.Net.Http.Json;
using System.Text.Json;

namespace NextIntegrations.Devices.Fiscal.Omnisoft;

/// <summary>
/// HTTP/JSON client for the Omnisoft / "Omnicashier" fiscal API (OmniTech TPS575 online cash register).
/// All operations POST a single JSON envelope to the device's <c>/v2</c> endpoint
/// (base e.g. <c>http://192.168.1.103:8989/v2</c>, PDF p.3 §2.1). Call <see cref="LoginAsync"/> first;
/// the returned <c>access_token</c> is cached and attached to every subsequent call (PDF p.7 §5).
/// </summary>
/// <remarks>
/// Richer fiscal operations (refund, X-report, correction, drawer, deposit/withdraw) live here and
/// are surfaced to the rest of the app through <see cref="OmnisoftFiscalDevice"/>, which adapts the
/// subset needed by <c>IFiscalDevice</c>. This type is not thread-safe for concurrent calls that
/// share the cached token; serialize calls per device (a fiscal register is a single-threaded device).
/// </remarks>
public sealed class OmnisoftFiscalClient
{
    /// <summary>The single API endpoint path appended to the configured base address (PDF p.3 §2.1).</summary>
    private const string Endpoint = "v2";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        // The device names items in Azerbaijani/Russian — UTF-8 is mandatory (PDF p.22 §6.1.8).
        // System.Text.Json emits UTF-8 by default; keep property names verbatim (DTOs carry JsonPropertyName).
        PropertyNamingPolicy = null,
    };

    private readonly HttpClient _httpClient;
    private string? _accessToken;

    /// <summary>Creates a client over an injected <see cref="HttpClient"/> whose base address is the device's <c>http://&lt;ip&gt;:8989/</c>.</summary>
    /// <param name="httpClient">The transport; its <see cref="HttpClient.BaseAddress"/> should end in a trailing slash.</param>
    public OmnisoftFiscalClient(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
    }

    /// <summary>True once <see cref="LoginAsync"/> has cached an access token.</summary>
    public bool IsAuthenticated => !string.IsNullOrEmpty(_accessToken);

    /// <summary>
    /// Authenticates with an API-grade account and caches the returned <c>access_token</c> for later calls
    /// (check_type 40, PDF p.7, p.20). Test units accept <c>SuperApi</c>/<c>123</c>; production users are
    /// issued by OmniTech support.
    /// </summary>
    /// <param name="name">API-grade account username.</param>
    /// <param name="password">API-grade account password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The session access token (also cached internally).</returns>
    /// <exception cref="OmnisoftFiscalException">No access token was returned.</exception>
    public async Task<string> LoginAsync(string name, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(password);

        OmnisoftSimpleRequest request = new()
        {
            RequestData = new OmnisoftSimpleRequestData
            {
                CheckData = new OmnisoftCheckData { CheckType = (int)OmnisoftCheckType.Login },
                Name = name,
                Password = password,
            },
        };

        OmnisoftLoginResponse response =
            await PostAsync<OmnisoftSimpleRequest, OmnisoftLoginResponse>(request, cancellationToken).ConfigureAwait(false);

        // Login success is signalled by the presence of access_token, not code == 0 (PDF p.20 shows code:1).
        if (string.IsNullOrEmpty(response.AccessToken))
        {
            throw new OmnisoftFiscalException(response.Code, response.Message ?? "Login failed: no access_token returned");
        }

        _accessToken = response.AccessToken;
        return _accessToken;
    }

    /// <summary>
    /// Gets token / cashbox / company information and the last document number (check_type 41, PDF p.17, p.19).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OmnisoftDeviceInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        OmnisoftSimpleRequest request = BuildSimpleRequest(OmnisoftCheckType.GetInfo);
        OmnisoftInfoResponse response =
            await PostAsync<OmnisoftSimpleRequest, OmnisoftInfoResponse>(request, cancellationToken).ConfigureAwait(false);

        EnsureSuccess(response.Code, response.Message);
        OmnisoftInfoData data = response.Data
            ?? throw new OmnisoftFiscalException(response.Code, "Get info returned no data");

        return new OmnisoftDeviceInfo(
            data.CompanyName,
            data.CompanyTaxNumber,
            data.CashboxFactoryNumber,
            data.CashregisterFactoryNumber,
            data.FirmwareVersion,
            data.LastDocNumber,
            data.State,
            data.NotBefore,
            data.NotAfter,
            data.QrCodeUrl);
    }

    /// <summary>Returns the current fiscal shift state (check_type 14, PDF p.20).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OmnisoftShiftStatus> GetShiftStatusAsync(CancellationToken cancellationToken = default)
    {
        OmnisoftSimpleRequest request = BuildSimpleRequest(OmnisoftCheckType.ShiftStatus);
        OmnisoftShiftStatusResponse response =
            await PostAsync<OmnisoftSimpleRequest, OmnisoftShiftStatusResponse>(request, cancellationToken).ConfigureAwait(false);

        EnsureSuccess(response.Code, response.Message);
        return new OmnisoftShiftStatus(response.ShiftStatus, response.Serial, response.ShiftOpenTime);
    }

    /// <summary>Opens a new fiscal shift (check_type 15, PDF p.20).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task OpenShiftAsync(CancellationToken cancellationToken = default) =>
        ExecuteAckAsync(OmnisoftCheckType.OpenShift, cancellationToken);

    /// <summary>
    /// Registers a sale and returns its fiscal proof (check_type 1, <c>doc_type</c> "sale", PDF p.8, p.24).
    /// </summary>
    /// <param name="cashier">Cashier name printed on the receipt.</param>
    /// <param name="currency">Document currency code, e.g. "AZN".</param>
    /// <param name="items">Sale line items.</param>
    /// <param name="payment">Payment split (cash/cashless/bonus + tendered cash).</param>
    /// <param name="vatAmounts">VAT totals grouped by rate; computed from <paramref name="items"/> when null.</param>
    /// <param name="internalReference">Optional client transaction id echoed in Transaction history (PDF p.23).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OmnisoftFiscalResult> SaleAsync(
        string cashier,
        string currency,
        IReadOnlyList<OmnisoftSaleItem> items,
        OmnisoftPayment payment,
        IReadOnlyList<OmnisoftVatLine>? vatAmounts = null,
        string? internalReference = null,
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

        IReadOnlyList<OmnisoftItem> wireItems = MapItems(items);
        IReadOnlyList<OmnisoftVatAmount> vat = MapVat(vatAmounts) ?? AggregateVat(items);

        OmnisoftDocumentRequest request = new()
        {
            RequestData = new OmnisoftDocumentRequestData
            {
                AccessToken = RequireToken(),
                IntRef = internalReference,
                TokenData = new OmnisoftTokenData
                {
                    Parameters = new OmnisoftParameters
                    {
                        DocType = "sale",
                        Data = new OmnisoftDocumentData
                        {
                            Cashier = cashier,
                            Currency = currency,
                            Items = wireItems,
                            Sum = ToDouble(payment.Cash + payment.Cashless + payment.Bonus),
                            CashSum = ToDouble(payment.Cash),
                            CashlessSum = ToDouble(payment.Cashless),
                            BonusSum = ToDouble(payment.Bonus),
                            IncomingSum = ToDouble(payment.IncomingCash),
                            VatAmounts = vat,
                        },
                    },
                },
                CheckData = new OmnisoftCheckData { CheckType = (int)OmnisoftCheckType.Sale },
            },
        };

        return await CreateDocumentAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Registers a refund of a previously fiscalized sale (check_type 100, <c>doc_type</c> "money_back",
    /// PDF p.9, p.23). The original sale's <c>long_id</c>/<c>short_id</c>/<c>document_number</c> link the refund.
    /// </summary>
    /// <param name="cashier">Cashier name printed on the receipt.</param>
    /// <param name="currency">Document currency code.</param>
    /// <param name="items">Items being refunded.</param>
    /// <param name="payment">How the refund is paid back (cash/cashless/bonus).</param>
    /// <param name="parentLongId">The original sale's <c>long_id</c> (PDF p.9 "parentDocument").</param>
    /// <param name="originalDocumentNumber">The original sale's document number (PDF p.9).</param>
    /// <param name="originalShortId">The original sale's <c>short_id</c> (PDF p.24).</param>
    /// <param name="vatAmounts">VAT totals grouped by rate; computed from <paramref name="items"/> when null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<OmnisoftFiscalResult> RefundAsync(
        string cashier,
        string currency,
        IReadOnlyList<OmnisoftSaleItem> items,
        OmnisoftPayment payment,
        string parentLongId,
        string originalDocumentNumber,
        string? originalShortId = null,
        IReadOnlyList<OmnisoftVatLine>? vatAmounts = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(cashier);
        ArgumentException.ThrowIfNullOrEmpty(currency);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentException.ThrowIfNullOrEmpty(parentLongId);
        ArgumentException.ThrowIfNullOrEmpty(originalDocumentNumber);
        if (items.Count == 0)
        {
            throw new ArgumentException("A refund must contain at least one item.", nameof(items));
        }

        IReadOnlyList<OmnisoftItem> wireItems = MapItems(items);
        IReadOnlyList<OmnisoftVatAmount> vat = MapVat(vatAmounts) ?? AggregateVat(items);

        OmnisoftDocumentRequest request = new()
        {
            RequestData = new OmnisoftDocumentRequestData
            {
                AccessToken = RequireToken(),
                TokenData = new OmnisoftTokenData
                {
                    Parameters = new OmnisoftParameters
                    {
                        DocType = "money_back",
                        Data = new OmnisoftDocumentData
                        {
                            Cashier = cashier,
                            Currency = currency,
                            Items = wireItems,
                            Sum = ToDouble(payment.Cash + payment.Cashless + payment.Bonus),
                            CashSum = ToDouble(payment.Cash),
                            CashlessSum = ToDouble(payment.Cashless),
                            BonusSum = ToDouble(payment.Bonus),
                            IncomingSum = ToDouble(payment.IncomingCash),
                            VatAmounts = vat,
                            ParentDocument = parentLongId,
                            RefundDocumentNumber = originalDocumentNumber,
                            RefundShortDocumentId = originalShortId,
                            LastOperationAtUtc = string.Empty,
                        },
                    },
                },
                CheckData = new OmnisoftCheckData { CheckType = (int)OmnisoftCheckType.MoneyBack },
            },
        };

        return await CreateDocumentAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels a sale fiscalized within the current open shift, by its <c>long_id</c>
    /// (check_type 10, PDF p.9, p.23).
    /// </summary>
    /// <param name="longId">The sale's <c>long_id</c> (sent as <c>fiscalId</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task RollbackAsync(string longId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(longId);

        OmnisoftSimpleRequest request = new()
        {
            RequestData = new OmnisoftSimpleRequestData
            {
                AccessToken = RequireToken(),
                CheckData = new OmnisoftCheckData { CheckType = (int)OmnisoftCheckType.Rollback },
                FiscalId = longId,
            },
        };

        return ExecuteAckRequestAsync(request, cancellationToken);
    }

    /// <summary>
    /// Generates an X-Report — mid-shift totals without closing the shift (check_type 12, PDF p.10, p.29).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<OmnisoftReport> XReportAsync(CancellationToken cancellationToken = default) =>
        ReportAsync(OmnisoftCheckType.XReport, cancellationToken);

    /// <summary>
    /// Closes the fiscal shift and generates the Z-Report — ends the fiscal day (check_type 13, PDF p.10, p.30).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<OmnisoftReport> CloseShiftAsync(CancellationToken cancellationToken = default) =>
        ReportAsync(OmnisoftCheckType.CloseShiftZReport, cancellationToken);

    /// <summary>Opens the cash drawer (money box) over the fiscal API (check_type 28, PDF p.18).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task OpenDrawerAsync(CancellationToken cancellationToken = default) =>
        ExecuteAckAsync(OmnisoftCheckType.OpenMoneyBox, cancellationToken);

    /// <summary>Closes the cash drawer (money box) over the fiscal API (check_type 29, PDF p.18).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task CloseDrawerAsync(CancellationToken cancellationToken = default) =>
        ExecuteAckAsync(OmnisoftCheckType.CloseMoneyBox, cancellationToken);

    /// <summary>Records a cash deposit into the drawer (check_type 7, PDF p.7, p.23).</summary>
    /// <param name="amount">Cash amount deposited.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task DepositAsync(decimal amount, CancellationToken cancellationToken = default) =>
        ExecuteCashMovementAsync(OmnisoftCheckType.Deposit, amount, cancellationToken);

    /// <summary>Records a cash withdrawal from the drawer (check_type 8, PDF p.9, p.24).</summary>
    /// <param name="amount">Cash amount withdrawn.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WithdrawAsync(decimal amount, CancellationToken cancellationToken = default) =>
        ExecuteCashMovementAsync(OmnisoftCheckType.Withdraw, amount, cancellationToken);

    private async Task<OmnisoftReport> ReportAsync(OmnisoftCheckType checkType, CancellationToken cancellationToken)
    {
        OmnisoftSimpleRequest request = BuildSimpleRequest(checkType);
        OmnisoftReportResponse response =
            await PostAsync<OmnisoftSimpleRequest, OmnisoftReportResponse>(request, cancellationToken).ConfigureAwait(false);

        EnsureSuccess(response.Code, response.Message);
        OmnisoftReportData data = response.Data
            ?? throw new OmnisoftFiscalException(response.Code, "Report returned no data");

        IReadOnlyList<OmnisoftReportCurrencyTotals> currencies = (data.Currencies ?? [])
            .Select(static c => new OmnisoftReportCurrencyTotals(
                c.Currency,
                c.SaleCount,
                (decimal)c.SaleSum,
                (decimal)c.SaleCashSum,
                (decimal)c.SaleCashlessSum,
                c.MoneyBackCount,
                (decimal)c.MoneyBackSum,
                (decimal)c.DepositSum,
                (decimal)c.WithdrawSum))
            .ToArray();

        return new OmnisoftReport(
            data.ReportNumber,
            data.DocumentId,
            data.FirstDocNumber,
            data.LastDocNumber,
            data.ShiftOpenAtUtc,
            data.CreatedAtUtc,
            currencies);
    }

    private async Task<OmnisoftFiscalResult> CreateDocumentAsync(
        OmnisoftDocumentRequest request,
        CancellationToken cancellationToken)
    {
        OmnisoftDocumentResponse response =
            await PostAsync<OmnisoftDocumentRequest, OmnisoftDocumentResponse>(request, cancellationToken).ConfigureAwait(false);

        EnsureSuccess(response.Code, response.Message);
        return new OmnisoftFiscalResult(
            response.DocumentNumber,
            response.LongId ?? string.Empty,
            response.ShortId ?? string.Empty,
            response.ShiftDocumentNumber);
    }

    private async Task ExecuteCashMovementAsync(
        OmnisoftCheckType checkType,
        decimal amount,
        CancellationToken cancellationToken)
    {
        OmnisoftCashMovementRequest request = new()
        {
            RequestData = new OmnisoftCashMovementRequestData
            {
                AccessToken = RequireToken(),
                TokenData = new OmnisoftCashMovementTokenData
                {
                    Parameters = new OmnisoftCashMovementParameters
                    {
                        Data = new OmnisoftCashSum { CashSum = ToDouble(amount) },
                    },
                },
                CheckData = new OmnisoftCheckData { CheckType = (int)checkType },
            },
        };

        OmnisoftAckResponse response =
            await PostAsync<OmnisoftCashMovementRequest, OmnisoftAckResponse>(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response.Code, response.Message, response.Info);
    }

    private Task ExecuteAckAsync(OmnisoftCheckType checkType, CancellationToken cancellationToken) =>
        ExecuteAckRequestAsync(BuildSimpleRequest(checkType), cancellationToken);

    private async Task ExecuteAckRequestAsync(OmnisoftSimpleRequest request, CancellationToken cancellationToken)
    {
        OmnisoftAckResponse response =
            await PostAsync<OmnisoftSimpleRequest, OmnisoftAckResponse>(request, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response.Code, response.Message, response.Info);
    }

    private OmnisoftSimpleRequest BuildSimpleRequest(OmnisoftCheckType checkType) => new()
    {
        RequestData = new OmnisoftSimpleRequestData
        {
            AccessToken = RequireToken(),
            CheckData = new OmnisoftCheckData { CheckType = (int)checkType },
        },
    };

    private async Task<TResponse> PostAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken)
    {
        using HttpResponseMessage httpResponse =
            await _httpClient.PostAsJsonAsync(Endpoint, request, SerializerOptions, cancellationToken).ConfigureAwait(false);
        httpResponse.EnsureSuccessStatusCode();

        TResponse? response = await httpResponse.Content
            .ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken)
            .ConfigureAwait(false);

        return response ?? throw new OmnisoftFiscalException("Omnisoft fiscal API returned an empty response body.");
    }

    private string RequireToken() =>
        _accessToken ?? throw new OmnisoftFiscalException("Not authenticated: call LoginAsync first.");

    private static void EnsureSuccess(int code, string? message, string? info = null)
    {
        if (code != 0)
        {
            throw new OmnisoftFiscalException(code, message, info);
        }
    }

    private static OmnisoftItem[] MapItems(IReadOnlyList<OmnisoftSaleItem> items) =>
        items.Select(static i => new OmnisoftItem
        {
            ItemName = i.Name,
            ItemCodeType = (int)i.CodeType,
            ItemCode = i.Code,
            ItemQuantityType = (int)i.QuantityType,
            ItemQuantity = ToDouble(i.Quantity),
            ItemPrice = ToDouble(i.Price),
            ItemSum = ToDouble(i.Sum),
            ItemVatPercent = ToDouble(i.VatPercent),
            Discount = ToDouble(i.Discount),
        }).ToArray();

    private static OmnisoftVatAmount[]? MapVat(IReadOnlyList<OmnisoftVatLine>? vatAmounts) =>
        vatAmounts?.Select(static v => new OmnisoftVatAmount
        {
            VatSum = ToDouble(v.VatSum),
            VatPercent = ToDouble(v.VatPercent),
        }).ToArray();

    private static OmnisoftVatAmount[] AggregateVat(IReadOnlyList<OmnisoftSaleItem> items) =>
        items
            .GroupBy(static i => i.VatPercent)
            .Select(static g => new OmnisoftVatAmount
            {
                VatSum = ToDouble(g.Sum(static i => i.Sum)),
                VatPercent = ToDouble(g.Key),
            })
            .ToArray();

    private static double ToDouble(decimal value) => (double)value;
}
