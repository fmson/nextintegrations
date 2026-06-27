using System.Text.Json;
using NextIntegrations.Devices.Fiscal.Omnisoft;
using Xunit;

namespace NextIntegrations.Tests;

public sealed class OmnisoftFiscalClientTests
{
    private const string SampleToken = "7dOF7HlXb/uG2J/FD+GtjA==";

    private static OmnisoftFiscalClient CreateClient(StubHttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://192.168.1.103:8989/") });

    private static JsonElement ParseBody(string body) => JsonDocument.Parse(body).RootElement;

    [Fact]
    public async Task LoginAsync_PostsToV2_AndStoresToken()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueJson($$"""{"access_token":"{{SampleToken}}","code":1,"message":"login success"}""");
        OmnisoftFiscalClient client = CreateClient(handler);

        string token = await client.LoginAsync("SuperApi", "123");

        Assert.Equal(SampleToken, token);
        Assert.True(client.IsAuthenticated);

        CapturedRequest login = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, login.Method);
        Assert.Equal("http://192.168.1.103:8989/v2", login.Uri!.ToString());

        JsonElement requestData = ParseBody(login.Body).GetProperty("requestData");
        Assert.Equal(40, requestData.GetProperty("checkData").GetProperty("check_type").GetInt32());
        Assert.Equal("SuperApi", requestData.GetProperty("name").GetString());
        Assert.Equal("123", requestData.GetProperty("password").GetString());
        // The Login request must NOT carry an access_token yet.
        Assert.False(requestData.TryGetProperty("access_token", out _));
    }

    [Fact]
    public async Task LoginAsync_WithoutAccessToken_Throws()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueJson("""{"code":401,"message":"Parse error: Invalid API key"}""");
        OmnisoftFiscalClient client = CreateClient(handler);

        OmnisoftFiscalException ex =
            await Assert.ThrowsAsync<OmnisoftFiscalException>(() => client.LoginAsync("bad", "creds"));
        Assert.Equal(401, ex.Code);
        Assert.False(client.IsAuthenticated);
    }

    [Fact]
    public async Task Operation_BeforeLogin_Throws()
    {
        StubHttpMessageHandler handler = new();
        OmnisoftFiscalClient client = CreateClient(handler);

        await Assert.ThrowsAsync<OmnisoftFiscalException>(() => client.OpenShiftAsync());
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SaleAsync_AttachesToken_AndEmitsCorrectCheckTypeAndFields()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueJson($$"""{"access_token":"{{SampleToken}}","code":1,"message":"login success"}""");
        handler.EnqueueJson("""
            {
              "code": 0,
              "document_number": 27,
              "long_id": "BX2RFjuzMF1v3j7bpSh6pnkLt4AYVXBAKnRRwmgKoqKP",
              "message": "Successful operation",
              "shift_document_number": 3,
              "short_id": "BX2RFjuzMF1v"
            }
            """);
        OmnisoftFiscalClient client = CreateClient(handler);
        await client.LoginAsync("SuperApi", "123");

        OmnisoftSaleItem[] items =
        [
            new("Shirt", "1564854651", OmnisoftItemCodeKind.Ean8, 2m, OmnisoftQuantityKind.Pieces, 50m, 100m, 18m),
            new("Trousers", "1894981988", OmnisoftItemCodeKind.Ean13, 1m, OmnisoftQuantityKind.Pieces, 100m, 100m, 18m),
        ];
        OmnisoftPayment payment = new(Cash: 100m, Cashless: 100m, Bonus: 0m, IncomingCash: 100m);

        OmnisoftFiscalResult result = await client.SaleAsync(
            cashier: "Mask",
            currency: "AZN",
            items: items,
            payment: payment,
            internalReference: "123456");

        // --- Fiscal proof parsed from the sample Sale response (PDF p.24) ---
        Assert.Equal(27, result.DocumentNumber);
        Assert.Equal("BX2RFjuzMF1v3j7bpSh6pnkLt4AYVXBAKnRRwmgKoqKP", result.LongId);
        Assert.Equal("BX2RFjuzMF1v", result.ShortId);
        Assert.Equal(3, result.ShiftDocumentNumber);

        // --- Request JSON shape (PDF p.8 "Sale") ---
        CapturedRequest sale = handler.Requests[1];
        JsonElement requestData = ParseBody(sale.Body).GetProperty("requestData");
        Assert.Equal(SampleToken, requestData.GetProperty("access_token").GetString());
        Assert.Equal("123456", requestData.GetProperty("int_ref").GetString());
        Assert.Equal(1, requestData.GetProperty("checkData").GetProperty("check_type").GetInt32());

        JsonElement parameters = requestData.GetProperty("tokenData").GetProperty("parameters");
        Assert.Equal("sale", parameters.GetProperty("doc_type").GetString());
        Assert.Equal("createDocument", requestData.GetProperty("tokenData").GetProperty("operationId").GetString());
        Assert.Equal(1, requestData.GetProperty("tokenData").GetProperty("version").GetInt32());

        JsonElement data = parameters.GetProperty("data");
        Assert.Equal("Mask", data.GetProperty("cashier").GetString());
        Assert.Equal("AZN", data.GetProperty("currency").GetString());
        Assert.Equal(200d, data.GetProperty("sum").GetDouble());
        Assert.Equal(100d, data.GetProperty("cashSum").GetDouble());
        Assert.Equal(100d, data.GetProperty("cashlessSum").GetDouble());
        Assert.Equal(100d, data.GetProperty("incomingSum").GetDouble());

        JsonElement itemsJson = data.GetProperty("items");
        Assert.Equal(2, itemsJson.GetArrayLength());
        JsonElement firstItem = itemsJson[0];
        Assert.Equal("Shirt", firstItem.GetProperty("itemName").GetString());
        Assert.Equal(1, firstItem.GetProperty("itemCodeType").GetInt32());
        Assert.Equal("1564854651", firstItem.GetProperty("itemCode").GetString());
        Assert.Equal(2d, firstItem.GetProperty("itemQuantity").GetDouble());
        Assert.Equal(50d, firstItem.GetProperty("itemPrice").GetDouble());
        Assert.Equal(100d, firstItem.GetProperty("itemSum").GetDouble());
        Assert.Equal(18d, firstItem.GetProperty("itemVatPercent").GetDouble());

        // vatAmounts auto-aggregated by rate => one 18% line totalling 200.
        JsonElement vat = data.GetProperty("vatAmounts");
        JsonElement vatLine = Assert.Single(vat.EnumerateArray().ToArray());
        Assert.Equal(18d, vatLine.GetProperty("vatPercent").GetDouble());
        Assert.Equal(200d, vatLine.GetProperty("vatSum").GetDouble());
    }

    [Fact]
    public async Task RefundAsync_EmitsCheckType100_AndParentLinkage()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueJson($$"""{"access_token":"{{SampleToken}}","code":1}""");
        handler.EnqueueJson("""{"code":0,"document_number":28,"long_id":"REFUNDLONG","short_id":"REFUNDSHORT","shift_document_number":4}""");
        OmnisoftFiscalClient client = CreateClient(handler);
        await client.LoginAsync("SuperApi", "123");

        OmnisoftSaleItem[] items =
        [
            new("test", "225555", OmnisoftItemCodeKind.PlainText, 1m, OmnisoftQuantityKind.Pieces, 25m, 25m, 18m),
        ];

        OmnisoftFiscalResult result = await client.RefundAsync(
            cashier: "fad",
            currency: "AZN",
            items: items,
            payment: new OmnisoftPayment(Cash: 0m, Cashless: 25m),
            parentLongId: "63YNLNiaWkFS9Q7XXwEY4fD25LiMEU2eE6xtoAPXXDju",
            originalDocumentNumber: "1",
            originalShortId: "63YNLNiaWkFS");

        Assert.Equal("REFUNDLONG", result.LongId);

        JsonElement requestData = ParseBody(handler.Requests[1].Body).GetProperty("requestData");
        Assert.Equal(100, requestData.GetProperty("checkData").GetProperty("check_type").GetInt32());

        JsonElement data = requestData.GetProperty("tokenData").GetProperty("parameters").GetProperty("data");
        Assert.Equal("money_back", requestData.GetProperty("tokenData").GetProperty("parameters").GetProperty("doc_type").GetString());
        Assert.Equal("63YNLNiaWkFS9Q7XXwEY4fD25LiMEU2eE6xtoAPXXDju", data.GetProperty("parentDocument").GetString());
        Assert.Equal("1", data.GetProperty("refund_document_number").GetString());
        Assert.Equal("63YNLNiaWkFS", data.GetProperty("refund_short_document_id").GetString());
    }

    [Fact]
    public async Task OpenShiftAsync_EmitsCheckType15_WithToken()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueJson($$"""{"access_token":"{{SampleToken}}","code":1}""");
        handler.EnqueueJson("""{"code":0,"message":"Successful operation"}""");
        OmnisoftFiscalClient client = CreateClient(handler);
        await client.LoginAsync("SuperApi", "123");

        await client.OpenShiftAsync();

        JsonElement requestData = ParseBody(handler.Requests[1].Body).GetProperty("requestData");
        Assert.Equal(15, requestData.GetProperty("checkData").GetProperty("check_type").GetInt32());
        Assert.Equal(SampleToken, requestData.GetProperty("access_token").GetString());
    }

    [Fact]
    public async Task OpenDrawerAsync_EmitsCheckType28()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueJson($$"""{"access_token":"{{SampleToken}}","code":1}""");
        handler.EnqueueJson("""{"code":0}""");
        OmnisoftFiscalClient client = CreateClient(handler);
        await client.LoginAsync("SuperApi", "123");

        await client.OpenDrawerAsync();

        JsonElement requestData = ParseBody(handler.Requests[1].Body).GetProperty("requestData");
        Assert.Equal(28, requestData.GetProperty("checkData").GetProperty("check_type").GetInt32());
    }

    [Fact]
    public async Task XReportAsync_EmitsCheckType12_AndParsesTotals()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueJson($$"""{"access_token":"{{SampleToken}}","code":1}""");
        handler.EnqueueJson(SampleReportJson(documentId: ""));
        OmnisoftFiscalClient client = CreateClient(handler);
        await client.LoginAsync("SuperApi", "123");

        OmnisoftReport report = await client.XReportAsync();

        JsonElement requestData = ParseBody(handler.Requests[1].Body).GetProperty("requestData");
        Assert.Equal(12, requestData.GetProperty("checkData").GetProperty("check_type").GetInt32());

        Assert.Equal(180, report.ReportNumber);
        Assert.Equal(762, report.FirstDocNumber);
        Assert.Equal(765, report.LastDocNumber);
        Assert.Equal(string.Empty, report.DocumentId);
        OmnisoftReportCurrencyTotals azn = Assert.Single(report.Currencies);
        Assert.Equal("AZN", azn.Currency);
        Assert.Equal(2, azn.SaleCount);
        Assert.Equal(2, azn.MoneyBackCount);
        Assert.Equal(100m, azn.MoneyBackSum);
    }

    [Fact]
    public async Task CloseShiftAsync_EmitsCheckType13_AndParsesZNumberAndDocumentId()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueJson($$"""{"access_token":"{{SampleToken}}","code":1}""");
        handler.EnqueueJson(SampleReportJson(documentId: "ABzrjw5jCLvc3odRkMMQd5M7SFXpC7qXh9ZQCTRvHtDV"));
        OmnisoftFiscalClient client = CreateClient(handler);
        await client.LoginAsync("SuperApi", "123");

        OmnisoftReport report = await client.CloseShiftAsync();

        JsonElement requestData = ParseBody(handler.Requests[1].Body).GetProperty("requestData");
        Assert.Equal(13, requestData.GetProperty("checkData").GetProperty("check_type").GetInt32());

        Assert.Equal(180, report.ReportNumber);
        Assert.Equal("ABzrjw5jCLvc3odRkMMQd5M7SFXpC7qXh9ZQCTRvHtDV", report.DocumentId);
    }

    [Fact]
    public async Task GetInfoAsync_ParsesTokenInformation()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueJson($$"""{"access_token":"{{SampleToken}}","code":1}""");
        handler.EnqueueJson("""
            {
              "code": 0,
              "data": {
                "cashbox_factory_number": "2938742983741098248193",
                "cashbox_tax_number": "test_00234234",
                "cashregister_factory_number": "all",
                "cashregister_model": "AGR",
                "company_name": "\"OMNITECH\" MƏHDUD MƏSULİYYƏTLİ CƏMİYYƏTİ",
                "company_tax_number": "1231234",
                "firmware_version": "2.20.0",
                "last_doc_number": 2612,
                "not_after": "2023-11-03T09:56:05Z",
                "not_before": "2020-11-03T09:55:35Z",
                "qr_code_url": "https://monitoring.e-kassa.az/#/index?doc=",
                "state": "ACTIVE"
              },
              "message": "Successful operation"
            }
            """);
        OmnisoftFiscalClient client = CreateClient(handler);
        await client.LoginAsync("SuperApi", "123");

        OmnisoftDeviceInfo info = await client.GetInfoAsync();

        Assert.Equal("\"OMNITECH\" MƏHDUD MƏSULİYYƏTLİ CƏMİYYƏTİ", info.CompanyName);
        Assert.Equal("1231234", info.CompanyTaxNumber);
        Assert.Equal("2.20.0", info.FirmwareVersion);
        Assert.Equal(2612, info.LastDocNumber);
        Assert.Equal("ACTIVE", info.State);
        Assert.Equal("https://monitoring.e-kassa.az/#/index?doc=", info.QrCodeUrl);

        JsonElement requestData = ParseBody(handler.Requests[1].Body).GetProperty("requestData");
        Assert.Equal(41, requestData.GetProperty("checkData").GetProperty("check_type").GetInt32());
    }

    [Fact]
    public async Task NonZeroResponseCode_ThrowsWithCode()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueJson($$"""{"access_token":"{{SampleToken}}","code":1}""");
        handler.EnqueueJson("""{"code":1312,"message":"Error: already refunded/rollbacked."}""");
        OmnisoftFiscalClient client = CreateClient(handler);
        await client.LoginAsync("SuperApi", "123");

        OmnisoftFiscalException ex =
            await Assert.ThrowsAsync<OmnisoftFiscalException>(() => client.OpenShiftAsync());
        Assert.Equal(1312, ex.Code);
    }

    // Sample X/Z report payload modelled on PDF p.29–31 (one AZN currency block).
    private static string SampleReportJson(string documentId) => $$"""
        {
          "_z": 0,
          "code": 0,
          "data": {
            "createdAtUtc": "2021-10-22T16:40:14",
            "currencies": [
              {
                "currency": "AZN",
                "moneyBackCashSum": 100.0,
                "moneyBackCount": 2,
                "moneyBackCreditSum": 2000.0,
                "moneyBackSum": 100.0,
                "saleCount": 2.0,
                "saleCreditSum": 2000.0,
                "salePrepaymentSum": 100.0,
                "saleSum": 0.0,
                "withdrawCount": 0,
                "withdrawSum": 0.0
              }
            ],
            "docCountToSend": 0,
            "document_id": "{{documentId}}",
            "firstDocNumber": 762,
            "lastDocNumber": 765,
            "reportNumber": 180,
            "shiftOpenAtUtc": "2021-10-22T13:52:37"
          },
          "message": "Successful operation"
        }
        """;
}
