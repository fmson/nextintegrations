using NetMQ;
using NetMQ.Sockets;
using NextIntegrations.Devices.Fiscal.Nba;
using Xunit;

namespace NextIntegrations.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that runs only when <c>NBA_ZEROMQ_TESTS=1</c>. The live in-process ZeroMQ
/// tests below pass (verified), but NetMQ's global-context threads keep the test host alive on exit and can
/// hang a plain <c>dotnet test</c> run / CI. Gating them keeps the default suite hang-free while leaving the
/// transport proof runnable on demand: <c>NBA_ZEROMQ_TESTS=1 dotnet test</c>.
/// </summary>
public sealed class ZeroMqFactAttribute : FactAttribute
{
    public ZeroMqFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NBA_ZEROMQ_TESTS") != "1")
        {
            Skip = "Set NBA_ZEROMQ_TESTS=1 to run the live in-process ZeroMQ transport tests.";
        }
    }
}

/// <summary>
/// Proves the real ZeroMQ transport (<see cref="NetMqNbaTransport"/>) speaks the wire the fiscalbox uses,
/// by round-tripping against an in-process ZeroMQ REP double — no external device. This is the "simulation"
/// the client's protocol tests sit on top of: here the actual ZMTP REQ↔REP handshake and framing run.
/// Opt-in (see <see cref="ZeroMqFactAttribute"/>) so NetMQ's exit-time threads never hang the default suite.
/// </summary>
[Collection("netmq")]
public sealed class NetMqNbaTransportTests
{
    private static Task RunReplyServerAsync(ResponseSocket server, Action<string> onRequest, string reply) =>
        Task.Run(() =>
        {
            if (server.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? request) && request is not null)
            {
                onRequest(request);
                server.SendFrame(reply);
            }
        });

    [ZeroMqFact]
    public async Task SendReceiveAsync_RoundTripsOverRealZeroMq()
    {
        using ResponseSocket server = new();
        server.Options.Linger = TimeSpan.Zero;
        int port = server.BindRandomPort("tcp://127.0.0.1");

        string? captured = null;
        Task serverTask = RunReplyServerAsync(server, req => captured = req, """{"code":0,"message":"pong"}""");

        using NetMqNbaTransport transport = new($"tcp://127.0.0.1:{port}", TimeSpan.FromSeconds(5));
        string response = await transport.SendReceiveAsync("""{"operationId":"getInfo","version":1}""");

        await serverTask;
        Assert.Equal("""{"operationId":"getInfo","version":1}""", captured);
        Assert.Contains("\"code\":0", response, StringComparison.Ordinal);
    }

    [ZeroMqFact]
    public async Task Client_OverRealZeroMq_GetInfoRoundTrips()
    {
        const string infoReply =
            "{\"data\":{\"company_name\":\"CYBERNET\",\"state\":\"ACTIVE\",\"last_doc_number\":156," +
            "\"qr_code_url\":\"https://monitoring.e-kassa.az/#/index?doc=\"},\"code\":0,\"message\":\"ok\"}";

        using ResponseSocket server = new();
        server.Options.Linger = TimeSpan.Zero;
        int port = server.BindRandomPort("tcp://127.0.0.1");

        string? captured = null;
        Task serverTask = RunReplyServerAsync(server, req => captured = req, infoReply);

        using NetMqNbaTransport transport = new($"tcp://127.0.0.1:{port}", TimeSpan.FromSeconds(5));
        NbaFiscalClient client = new(transport);

        NbaDeviceInfo info = await client.GetInfoAsync();

        await serverTask;
        Assert.NotNull(captured);
        Assert.Contains("getInfo", captured!, StringComparison.Ordinal);
        Assert.Equal("CYBERNET", info.CompanyName);
        Assert.Equal("ACTIVE", info.State);
        Assert.Equal(156, info.LastDocNumber);
    }

    [ZeroMqFact]
    public async Task Client_OverRealZeroMq_FullLoginThenSale()
    {
        const string loginReply = "{\"data\":{\"access_token\":\"TOK==\"},\"code\":0,\"message\":\"ok\"}";
        const string saleReply =
            "{\"data\":{\"document_id\":\"FID123\",\"document_number\":42,\"shift_document_number\":1," +
            "\"short_document_id\":\"FID123\"},\"code\":0,\"message\":\"ok\"}";

        using ResponseSocket server = new();
        server.Options.Linger = TimeSpan.Zero;
        int port = server.BindRandomPort("tcp://127.0.0.1");

        List<string> captured = [];
        // A tiny scripted fiscalbox: reply to toLogin with a token, then to createDocument with a fiscal id.
        Task serverTask = Task.Run(() =>
        {
            for (int i = 0; i < 2; i++)
            {
                if (!server.TryReceiveFrameString(TimeSpan.FromSeconds(5), out string? req) || req is null)
                {
                    return;
                }

                captured.Add(req);
                server.SendFrame(req.Contains("toLogin", StringComparison.Ordinal) ? loginReply : saleReply);
            }
        });

        using NetMqNbaTransport transport = new($"tcp://127.0.0.1:{port}", TimeSpan.FromSeconds(5));
        NbaFiscalClient client = new(transport);

        await client.LoginAsync("95481354", "user", "P1170828642");
        NbaFiscalResult result = await client.SaleAsync(
            "Aleks", "AZN",
            [new NbaSaleItem("TEST-1", "1564854651", NbaItemCodeKind.Ean8, 3m, NbaQuantityKind.Pieces, 5m, 15m, 18m)],
            new NbaPayment(Cash: 15m, IncomingCash: 20m));

        await serverTask;
        Assert.Equal(2, captured.Count);
        Assert.Contains("toLogin", captured[0], StringComparison.Ordinal);
        Assert.Contains("createDocument", captured[1], StringComparison.Ordinal);
        Assert.Equal("FID123", result.DocumentId);
        Assert.Equal(42, result.DocumentNumber);
    }

    [ZeroMqFact]
    public async Task SendReceiveAsync_NoReply_TimesOutAsFiscalException()
    {
        // An idle REP socket that never answers → the REQ round-trip must time out (not hang).
        using ResponseSocket idle = new();
        idle.Options.Linger = TimeSpan.Zero;
        int port = idle.BindRandomPort("tcp://127.0.0.1");

        using NetMqNbaTransport transport = new($"tcp://127.0.0.1:{port}", TimeSpan.FromMilliseconds(400));

        NbaFiscalException ex = await Assert.ThrowsAsync<NbaFiscalException>(() =>
            transport.SendReceiveAsync("""{"operationId":"getInfo","version":1}"""));
        Assert.Contains("no response", ex.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// Shuts the shared NetMQ runtime down once, after all ZeroMQ-touching tests, so the test host process
/// exits cleanly instead of lingering on NetMQ's global-context threads (which otherwise keep the process
/// alive and hang the test run). <c>Cleanup(block: true)</c> joins those threads; all sockets in these tests
/// are disposed via <c>using</c>, so it returns promptly.
/// </summary>
public sealed class NetMqCleanupFixture : IDisposable
{
    private static bool Enabled => Environment.GetEnvironmentVariable("NBA_ZEROMQ_TESTS") == "1";

    public NetMqCleanupFixture()
    {
        if (Enabled)
        {
            NetMQConfig.Linger = TimeSpan.Zero;
        }
    }

    public void Dispose()
    {
        if (!Enabled)
        {
            return; // ZeroMQ tests were skipped → NetMQ was never used → nothing to tear down.
        }

        try
        {
            NetMQConfig.Cleanup(block: true);
        }
        catch (Exception)
        {
            // Best-effort teardown — a failure here must never fail the test run.
        }
    }
}

/// <summary>Groups the NetMQ tests so <see cref="NetMqCleanupFixture"/> runs their shared teardown.</summary>
[CollectionDefinition("netmq")]
public sealed class NetMqCollection : ICollectionFixture<NetMqCleanupFixture>;
