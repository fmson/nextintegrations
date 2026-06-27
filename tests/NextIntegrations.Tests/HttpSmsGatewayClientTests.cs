using System.Net;
using System.Text.Json;
using NextIntegrations.Devices.Sms;
using Xunit;

namespace NextIntegrations.Tests;

public sealed class HttpSmsGatewayClientTests
{
    private static SmsGatewayOptions Options(SmsGatewayTransport transport = SmsGatewayTransport.GetQuery) => new()
    {
        BaseAddress = new Uri("https://sms.example.az/api/send"),
        Login = "shop",
        Password = "s3cret",
        Sender = "NextShop",
        Transport = transport,
    };

    private static HttpSmsGatewayClient Client(StubHttpMessageHandler handler, SmsGatewayOptions options) =>
        new(new HttpClient(handler), options);

    [Fact]
    public async Task GetQuery_BuildsEscapedUrl_AndAcceptsOn200()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, "OK 12345");
        HttpSmsGatewayClient client = Client(handler, Options());

        SmsDispatchResult result = await client.SendAsync(new SmsText("+994501234567", "Salam dünya"));

        Assert.True(result.Accepted);
        Assert.Equal(200, result.StatusCode);

        CapturedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sent.Method);
        string url = sent.Uri!.AbsoluteUri; // escaped/canonical form (ToString() unescapes for display)
        Assert.StartsWith("https://sms.example.az/api/send?", url, StringComparison.Ordinal);
        Assert.Contains("user=shop", url, StringComparison.Ordinal);
        Assert.Contains("password=s3cret", url, StringComparison.Ordinal);
        Assert.Contains("from=NextShop", url, StringComparison.Ordinal);
        Assert.Contains("to=%2B994501234567", url, StringComparison.Ordinal); // '+' escaped
        Assert.Contains("text=Salam%20d%C3%BCnya", url, StringComparison.Ordinal); // space + 'ü' escaped
    }

    [Fact]
    public async Task PostForm_SendsFormEncodedBody()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, "sent");
        HttpSmsGatewayClient client = Client(handler, Options(SmsGatewayTransport.PostForm));

        await client.SendAsync(new SmsText("994700000000", "Hi"));

        CapturedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Contains("to=994700000000", sent.Body, StringComparison.Ordinal);
        Assert.Contains("text=Hi", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostJson_SendsJsonBodyWithMappedFields()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, "{\"status\":\"ok\"}");
        SmsGatewayOptions options = Options(SmsGatewayTransport.PostJson) with
        {
            RecipientField = "msisdn",
            TextField = "message",
        };
        HttpSmsGatewayClient client = Client(handler, options);

        await client.SendAsync(new SmsText("994551112233", "Endirim!"));

        CapturedRequest sent = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, sent.Method);
        JsonElement json = JsonDocument.Parse(sent.Body).RootElement;
        Assert.Equal("994551112233", json.GetProperty("msisdn").GetString());
        Assert.Equal("Endirim!", json.GetProperty("message").GetString());
        Assert.Equal("NextShop", json.GetProperty("from").GetString());
    }

    [Fact]
    public async Task NonSuccessStatus_IsRejected()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.InternalServerError, "boom");
        HttpSmsGatewayClient client = Client(handler, Options());

        SmsDispatchResult result = await client.SendAsync(new SmsText("99450", "x"));

        Assert.False(result.Accepted);
        Assert.Equal(500, result.StatusCode);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SuccessBodyContains_RejectsWhenMarkerMissing()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, "ERROR: blocked");
        SmsGatewayOptions options = Options() with { SuccessBodyContains = "OK" };
        HttpSmsGatewayClient client = Client(handler, options);

        SmsDispatchResult result = await client.SendAsync(new SmsText("99450", "x"));

        Assert.False(result.Accepted); // HTTP 200 but body lacks the success marker
    }

    [Fact]
    public async Task SendBulk_ReturnsAcceptedCount()
    {
        StubHttpMessageHandler handler = new();
        handler.EnqueueResponse(HttpStatusCode.OK, "ok");
        handler.EnqueueResponse(HttpStatusCode.BadGateway, "fail");
        handler.EnqueueResponse(HttpStatusCode.OK, "ok");
        HttpSmsGatewayClient client = Client(handler, Options());

        int accepted = await client.SendBulkAsync(
        [
            new SmsText("99450", "a"),
            new SmsText("99451", "b"),
            new SmsText("99452", "c"),
        ]);

        Assert.Equal(2, accepted);
        Assert.Equal(3, handler.Requests.Count);
    }
}
