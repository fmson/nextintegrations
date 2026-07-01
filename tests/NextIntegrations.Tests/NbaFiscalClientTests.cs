using System.Text.Json;
using NextIntegrations.Devices.Fiscal.Nba;
using Xunit;

namespace NextIntegrations.Tests;

/// <summary>
/// Unit tests for <see cref="NbaFiscalClient"/> driven by a fake <see cref="INbaTransport"/> — no ZeroMQ,
/// no device. Request envelopes are asserted against the ZeroMQ API v1.2 fiscalbox spec examples, and the
/// canned responses are the spec's own example payloads (§5, §6.1). Transport-level ZeroMQ is proved
/// separately in <see cref="NetMqNbaTransportTests"/>.
/// </summary>
public sealed class NbaFiscalClientTests
{
    private const string SampleToken = "CECERlo9yRItsutk+h7XmA==";

    private sealed class FakeTransport : INbaTransport
    {
        private readonly Queue<string> _responses = new();

        public List<string> Requests { get; } = [];

        public Func<string, string>? Responder { get; set; }

        public FakeTransport Enqueue(string responseJson)
        {
            _responses.Enqueue(responseJson);
            return this;
        }

        public Task<string> SendReceiveAsync(string requestJson, CancellationToken cancellationToken = default)
        {
            Requests.Add(requestJson);
            string response = Responder?.Invoke(requestJson) ?? _responses.Dequeue();
            return Task.FromResult(response);
        }

        public void Dispose()
        {
        }
    }

    private static JsonElement Root(string json) => JsonDocument.Parse(json).RootElement;

    private static NbaSaleItem Item(string name, decimal qty, decimal price, decimal vatPercent) =>
        new(name, "1564854651", NbaItemCodeKind.Ean8, qty, NbaQuantityKind.Pieces, price, qty * price, vatPercent);

    // ---- getInfo (no auth) ---------------------------------------------------

    [Fact]
    public async Task GetInfoAsync_SendsNoParameters_AndParsesRegistration()
    {
        FakeTransport transport = new();
        transport.Enqueue("""
            {"data":{"company_tax_number":"9900050571","company_name":"CYBERNET",
            "object_tax_number":"9900050571-59002","object_name":"Cybernet MMC & Metin",
            "object_address":"AZ5000 GEDEBEY","cashbox_tax_number":"test_02391",
            "cashbox_factory_number":"2B87FD","firmware_version":"2.43.1",
            "cashregister_factory_number":"all","cashregister_model":"All Model",
            "qr_code_url":"https://monitoring.e-kassa.az/#/index?doc=",
            "not_before":"2024-11-25T08:12:57Z","not_after":"2027-11-25T08:13:27Z","state":"ACTIVE",
            "last_online_time":"2025-06-21T13:17:46Z","oldest_document_time":null,"last_doc_number":156,
            "production_uuid":"55554944"},"code":0,"message":"Successful operation"}
            """);
        NbaFiscalClient client = new(transport);

        NbaDeviceInfo info = await client.GetInfoAsync();

        // getInfo carries no "parameters" at all — just operationId + version.
        JsonElement request = Root(Assert.Single(transport.Requests));
        Assert.Equal("getInfo", request.GetProperty("operationId").GetString());
        Assert.Equal(1, request.GetProperty("version").GetInt32());
        Assert.False(request.TryGetProperty("parameters", out _));

        Assert.Equal("9900050571", info.CompanyTaxNumber);
        Assert.Equal("CYBERNET", info.CompanyName);
        Assert.Equal("ACTIVE", info.State);
        Assert.Equal("2.43.1", info.FirmwareVersion);
        Assert.Equal(156, info.LastDocNumber);
        Assert.Equal("https://monitoring.e-kassa.az/#/index?doc=", info.QrCodeUrl);
    }

    // ---- toLogin -------------------------------------------------------------

    [Fact]
    public async Task LoginAsync_SendsPinRoleFactory_AndCachesTokenFromData()
    {
        FakeTransport transport = new();
        transport.Enqueue($$"""{"data":{"access_token":"{{SampleToken}}"},"code":0,"message":"Success operation"}""");
        NbaFiscalClient client = new(transport);

        string token = await client.LoginAsync("95481354", "user", "P1170828642");

        Assert.Equal(SampleToken, token);
        Assert.True(client.IsAuthenticated);

        JsonElement parameters = Root(Assert.Single(transport.Requests)).GetProperty("parameters");
        Assert.Equal("toLogin", Root(transport.Requests[0]).GetProperty("operationId").GetString());
        Assert.Equal("95481354", parameters.GetProperty("pin").GetString());
        Assert.Equal("user", parameters.GetProperty("role").GetString());
        Assert.Equal("P1170828642", parameters.GetProperty("cashregister_factory_number").GetString());
    }

    [Fact]
    public async Task LoginAsync_IncorrectPin_ThrowsWithCode404()
    {
        FakeTransport transport = new();
        transport.Enqueue("""{"code":404,"message":"incorrect pin code"}""");
        NbaFiscalClient client = new(transport);

        NbaFiscalException ex =
            await Assert.ThrowsAsync<NbaFiscalException>(() => client.LoginAsync("0000", "user", "F1"));
        Assert.Equal(404, ex.Code);
        Assert.False(client.IsAuthenticated);
    }

    [Fact]
    public async Task Operation_BeforeLogin_Throws_AndSendsNothing()
    {
        FakeTransport transport = new();
        NbaFiscalClient client = new(transport);

        await Assert.ThrowsAsync<NbaFiscalException>(() => client.OpenShiftAsync());
        Assert.Empty(transport.Requests);
    }

    // ---- createDocument: cash sale ------------------------------------------

    [Fact]
    public async Task SaleAsync_Cash_EmitsCreateDocumentEnvelope_AndComputesChange()
    {
        FakeTransport transport = new();
        transport
            .Enqueue($$"""{"data":{"access_token":"{{SampleToken}}"},"code":0,"message":"ok"}""")
            .Enqueue("""
                {"data":{"document_id":"EsVkkzPE1Tvav2AvKzuhNzqYf6LzFKBnUKPwBzf4UNVw","document_number":121,
                "shift_document_number":3,"short_document_id":"EsVkkzPE1Tva"},"code":0,"message":"Successful operation"}
                """);
        NbaFiscalClient client = new(transport);
        await client.LoginAsync("95481354", "user", "P1");

        // 3 × 5.00 = 15.00, buyer tenders 20.00 cash → change 5.00 (spec §6.1.6.3.1.a).
        NbaFiscalResult result = await client.SaleAsync(
            cashier: "Aleks",
            currency: "AZN",
            items: [Item("TEST-1", 3m, 5m, 18m)],
            payment: new NbaPayment(Cash: 15m, IncomingCash: 20m),
            previousDocumentNumber: 120);

        JsonElement req = Root(transport.Requests[1]);
        Assert.Equal("createDocument", req.GetProperty("operationId").GetString());
        JsonElement p = req.GetProperty("parameters");
        Assert.Equal(SampleToken, p.GetProperty("access_token").GetString());
        Assert.Equal("sale", p.GetProperty("doc_type").GetString());
        Assert.Equal(120, p.GetProperty("prev_doc_number").GetInt32());

        JsonElement data = p.GetProperty("data");
        Assert.Equal("Aleks", data.GetProperty("cashier").GetString());
        Assert.Equal("AZN", data.GetProperty("currency").GetString());
        Assert.Equal(15.0, data.GetProperty("sum").GetDouble());
        Assert.Equal(15.0, data.GetProperty("cashSum").GetDouble());
        Assert.Equal(0.0, data.GetProperty("cashlessSum").GetDouble());
        Assert.Equal(0.0, data.GetProperty("bonusSum").GetDouble());
        Assert.Equal(20.0, data.GetProperty("incomingSum").GetDouble());
        Assert.Equal(5.0, data.GetProperty("changeSum").GetDouble());
        // No bank/bonus refs on a cash sale → those keys are omitted.
        Assert.False(data.TryGetProperty("rrn", out _));
        Assert.False(data.TryGetProperty("transactions", out _));
        Assert.False(data.TryGetProperty("bonusCardNumber", out _));

        JsonElement item = data.GetProperty("items")[0];
        Assert.Equal("TEST-1", item.GetProperty("itemName").GetString());
        Assert.Equal(1, item.GetProperty("itemCodeType").GetInt32()); // EAN8
        Assert.Equal(3.0, item.GetProperty("itemQuantity").GetDouble());
        Assert.Equal(5.0, item.GetProperty("itemPrice").GetDouble());
        Assert.Equal(15.0, item.GetProperty("itemSum").GetDouble());
        Assert.Equal(18.0, item.GetProperty("itemVatPercent").GetDouble());

        JsonElement vat = data.GetProperty("vatAmounts")[0];
        Assert.Equal(15.0, vat.GetProperty("vatSum").GetDouble());
        Assert.Equal(18.0, vat.GetProperty("vatPercent").GetDouble());

        Assert.Equal("EsVkkzPE1Tvav2AvKzuhNzqYf6LzFKBnUKPwBzf4UNVw", result.DocumentId);
        Assert.Equal(121, result.DocumentNumber);
        Assert.Equal(3, result.ShiftDocumentNumber);
        Assert.Equal("EsVkkzPE1Tva", result.ShortDocumentId);
        Assert.False(result.ItemNamesNotSaved);
    }

    // ---- createDocument: cashless sale with per-bank transactions -----------

    [Fact]
    public async Task SaleAsync_Cashless_WithTransactions_EmitsBankArray()
    {
        FakeTransport transport = new();
        transport
            .Enqueue($$"""{"data":{"access_token":"{{SampleToken}}"},"code":0,"message":"ok"}""")
            .Enqueue("""
                {"data":{"document_id":"9JiuZ29hZ6XqPodgf","document_number":125,"shift_document_number":7,
                "short_document_id":"9JiuZ29hZ6Xq"},"code":0,"message":"Successful operation"}
                """);
        NbaFiscalClient client = new(transport);
        await client.LoginAsync("95481354", "user", "P1");

        NbaFiscalResult result = await client.SaleAsync(
            cashier: "Aleks",
            currency: "AZN",
            items: [Item("TEST-1", 3m, 5m, 18m)],
            payment: new NbaPayment(Cash: 0m, Cashless: 15m),
            bankSettlement: new NbaBankSettlement(
                Transactions: [new NbaBankTransaction("324015033958", 15m, NbaTransactionKind.Purchase, "786520")]));

        JsonElement data = Root(transport.Requests[1]).GetProperty("parameters").GetProperty("data");
        Assert.Equal(0.0, data.GetProperty("cashSum").GetDouble());
        Assert.Equal(15.0, data.GetProperty("cashlessSum").GetDouble());
        JsonElement tx = data.GetProperty("transactions")[0];
        Assert.Equal("324015033958", tx.GetProperty("rrn").GetString());
        Assert.Equal("786520", tx.GetProperty("approval_code").GetString());
        Assert.Equal(15.0, tx.GetProperty("amount").GetDouble());
        Assert.Equal("PURCHASE", tx.GetProperty("type").GetString());
        Assert.Equal(125, result.DocumentNumber);
    }

    // ---- createDocument: code 1205 (fiscal, but item names not saved) -------

    [Fact]
    public async Task SaleAsync_Code1205_IsTreatedAsSuccess_AndFlagged()
    {
        FakeTransport transport = new();
        transport
            .Enqueue($$"""{"data":{"access_token":"{{SampleToken}}"},"code":0,"message":"ok"}""")
            .Enqueue("""
                {"data":{"document_id":"ABC123","document_number":10,"shift_document_number":1,
                "short_document_id":"ABC123"},"code":1205,
                "message":"Document creating has been completed successfully, but it was unable to handle Item Names properly"}
                """);
        NbaFiscalClient client = new(transport);
        await client.LoginAsync("95481354", "user", "P1");

        NbaFiscalResult result = await client.SaleAsync(
            "Aleks", "AZN", [Item("TEST-1", 1m, 5m, 18m)], new NbaPayment(Cash: 5m, IncomingCash: 5m));

        Assert.Equal("ABC123", result.DocumentId); // fiscal id is valid
        Assert.True(result.ItemNamesNotSaved);      // but flagged for the caller to log
    }

    [Fact]
    public async Task SaleAsync_IllegalCharacter_1206_Throws()
    {
        FakeTransport transport = new();
        transport
            .Enqueue($$"""{"data":{"access_token":"{{SampleToken}}"},"code":0,"message":"ok"}""")
            .Enqueue("""{"code":1206,"message":"There is an illegal character(s) in item name(s)"}""");
        NbaFiscalClient client = new(transport);
        await client.LoginAsync("95481354", "user", "P1");

        NbaFiscalException ex = await Assert.ThrowsAsync<NbaFiscalException>(() =>
            client.SaleAsync("Aleks", "AZN", [Item("bad\tname", 1m, 5m, 18m)], new NbaPayment(Cash: 5m, IncomingCash: 5m)));
        Assert.Equal(1206, ex.Code);
    }

    // ---- VAT aggregation (mixed + 0%) ---------------------------------------

    [Fact]
    public async Task SaleAsync_MixedVatRates_AreGroupedIntoVatAmounts()
    {
        FakeTransport transport = new();
        transport
            .Enqueue($$"""{"data":{"access_token":"{{SampleToken}}"},"code":0,"message":"ok"}""")
            .Enqueue("""{"data":{"document_id":"X","document_number":1,"shift_document_number":1,"short_document_id":"X"},"code":0}""");
        NbaFiscalClient client = new(transport);
        await client.LoginAsync("95481354", "user", "P1");

        await client.SaleAsync(
            "Aleks", "AZN",
            [Item("A", 1m, 10m, 18m), Item("B", 1m, 8m, 18m), Item("C", 1m, 5m, 0m)],
            new NbaPayment(Cash: 23m, IncomingCash: 23m));

        JsonElement vatAmounts = Root(transport.Requests[1]).GetProperty("parameters").GetProperty("data").GetProperty("vatAmounts");
        Assert.Equal(2, vatAmounts.GetArrayLength()); // one 18% line, one 0% line
        double sum18 = vatAmounts.EnumerateArray().Single(v => v.GetProperty("vatPercent").GetDouble() == 18.0).GetProperty("vatSum").GetDouble();
        double sum0 = vatAmounts.EnumerateArray().Single(v => v.GetProperty("vatPercent").GetDouble() == 0.0).GetProperty("vatSum").GetDouble();
        Assert.Equal(18.0, sum18); // 10 + 8
        Assert.Equal(5.0, sum0);
    }

    // ---- shift status / close shift -----------------------------------------

    [Fact]
    public async Task GetShiftStatusAsync_ParsesOpenState()
    {
        FakeTransport transport = new();
        transport
            .Enqueue($$"""{"data":{"access_token":"{{SampleToken}}"},"code":0,"message":"ok"}""")
            .Enqueue("""{"data":{"shift_open":true,"shift_open_time":"2025-06-03T05:52:31Z"},"code":0,"message":"ok"}""");
        NbaFiscalClient client = new(transport);
        await client.LoginAsync("95481354", "user", "P1");

        NbaShiftStatus status = await client.GetShiftStatusAsync();

        Assert.True(status.IsOpen);
        Assert.Equal("2025-06-03T05:52:31Z", status.OpenedAt);
        Assert.Equal("getShiftStatus", Root(transport.Requests[1]).GetProperty("operationId").GetString());
    }

    [Fact]
    public async Task CloseShiftAsync_ParsesZReport_AndClearsNothing()
    {
        FakeTransport transport = new();
        transport
            .Enqueue($$"""{"data":{"access_token":"{{SampleToken}}"},"code":0,"message":"ok"}""")
            .Enqueue("""
                {"data":{"document_id":"ZDOC1234567890abc","reportNumber":7,"firstDocNumber":118,
                "lastDocNumber":127,"shiftOpenAtUtc":"2025-06-20T05:00:00Z","createdAtUtc":"2025-06-20T20:00:00Z"},
                "code":0,"message":"Successful operation"}
                """);
        NbaFiscalClient client = new(transport);
        await client.LoginAsync("95481354", "user", "P1");

        NbaReport report = await client.CloseShiftAsync();

        Assert.Equal(7, report.ReportNumber);
        Assert.Equal("ZDOC1234567890abc", report.DocumentId);
        Assert.Equal(118, report.FirstDocNumber);
        Assert.Equal(127, report.LastDocNumber);
        Assert.Equal("closeShift", Root(transport.Requests[1]).GetProperty("operationId").GetString());
    }

    [Fact]
    public async Task LogoutAsync_ClearsToken()
    {
        FakeTransport transport = new();
        transport
            .Enqueue($$"""{"data":{"access_token":"{{SampleToken}}"},"code":0,"message":"ok"}""")
            .Enqueue("""{"code":0,"message":"Success operation"}""");
        NbaFiscalClient client = new(transport);
        await client.LoginAsync("95481354", "user", "P1");
        Assert.True(client.IsAuthenticated);

        await client.LogoutAsync();

        Assert.False(client.IsAuthenticated);
        Assert.Equal(SampleToken, Root(transport.Requests[1]).GetProperty("parameters").GetProperty("access_token").GetString());
    }
}
