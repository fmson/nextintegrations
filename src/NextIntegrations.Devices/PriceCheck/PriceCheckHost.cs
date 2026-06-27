using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace NextIntegrations.Devices.PriceCheck;

/// <summary>
/// A tiny HTTP server the in-store price-checker ("qiymət oxuma cihazı") queries over the LAN. The device
/// is configured with the POS head's IP + port and requests <c>GET /price?barcode=NNN</c>; it gets back the
/// product name + price (JSON, or <c>?fmt=text</c> for two-line dumb displays). Built on a raw
/// <see cref="TcpListener"/> so it needs no Windows URL-ACL/admin reservation. App-neutral: the price data
/// comes from the supplied <see cref="PriceLookup"/>, so the host (and its wire format) is shared by every head.
/// </summary>
public sealed class PriceCheckHost : IDisposable
{
    // Web defaults (camelCase) + relaxed encoder so Azerbaijani names go out as literal UTF-8 (not \uXXXX),
    // which dumb price-checker firmwares render correctly.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly PriceLookup _lookup;
    private readonly int _port;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private int _boundPort;

    public PriceCheckHost(PriceLookup lookup, int port)
    {
        _lookup = lookup;
        _port = port;
    }

    /// <summary>The actual listening port (resolved after <see cref="Start"/> when the requested port was 0).</summary>
    public int Port => _boundPort > 0 ? _boundPort : _port;

    public bool IsRunning => _listener is not null;

    /// <summary>Starts listening on all interfaces. Throws <see cref="SocketException"/> if the port is taken.</summary>
    public void Start()
    {
        if (_listener is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, _port);
        _listener.Start();
        _boundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        TcpListener listener = _listener!;
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                break; // stopped / disposed
            }

            _ = HandleAsync(client, ct);
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                using NetworkStream stream = client.GetStream();
                stream.ReadTimeout = 4000;
                stream.WriteTimeout = 4000;

                string? requestLine = await ReadRequestLineAsync(stream, ct).ConfigureAwait(false);
                if (requestLine is null)
                {
                    return;
                }

                (string path, IReadOnlyDictionary<string, string> query) = ParseTarget(requestLine);

                if (path is "/price" or "/api/price")
                {
                    string code = Pick(query, "barcode") ?? Pick(query, "sku") ?? Pick(query, "q") ?? string.Empty;
                    PriceCheckResult result = await _lookup(code, ct).ConfigureAwait(false);
                    bool text = string.Equals(Pick(query, "fmt"), "text", StringComparison.OrdinalIgnoreCase);
                    if (text)
                    {
                        string body = result.Found
                            ? $"{result.Name}\n{result.Price.ToString("0.00", CultureInfo.InvariantCulture)} {result.Currency}"
                            : "Tapilmadi";
                        await WriteAsync(stream, 200, "text/plain; charset=utf-8", body, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        string json = JsonSerializer.Serialize(
                            new PriceCheckPayload(result.Found, result.Sku, result.Name, result.PriceMinor, result.Price, result.Unit, result.Currency),
                            JsonOptions);
                        await WriteAsync(stream, 200, "application/json; charset=utf-8", json, ct).ConfigureAwait(false);
                    }
                }
                else if (path is "/" or "/health")
                {
                    await WriteAsync(stream, 200, "text/plain; charset=utf-8", "price-check OK", ct).ConfigureAwait(false);
                }
                else
                {
                    await WriteAsync(stream, 404, "text/plain; charset=utf-8", "Not found", ct).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // Per-connection failure (device dropped, timeout) — ignore, keep serving others.
        }
    }

    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] buffer = new byte[8192];
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            int nl = Array.IndexOf(buffer, (byte)'\n', 0, total);
            if (nl >= 0)
            {
                int end = nl > 0 && buffer[nl - 1] == (byte)'\r' ? nl - 1 : nl;
                return Encoding.ASCII.GetString(buffer, 0, end);
            }
        }

        return total > 0 ? Encoding.ASCII.GetString(buffer, 0, total) : null;
    }

    private static (string Path, IReadOnlyDictionary<string, string> Query) ParseTarget(string requestLine)
    {
        // "GET /price?barcode=123 HTTP/1.1"
        string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string target = parts.Length >= 2 ? parts[1] : "/";
        int q = target.IndexOf('?', StringComparison.Ordinal);
        string path = q >= 0 ? target[..q] : target;
        Dictionary<string, string> query = new(StringComparer.OrdinalIgnoreCase);
        if (q >= 0)
        {
            foreach (string pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=', StringComparison.Ordinal);
                string key = eq >= 0 ? pair[..eq] : pair;
                string value = eq >= 0 ? pair[(eq + 1)..] : string.Empty;
                query[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value.Replace('+', ' '));
            }
        }

        return (path, query);
    }

    private static string? Pick(IReadOnlyDictionary<string, string> query, string key) =>
        query.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static async Task WriteAsync(NetworkStream stream, int status, string contentType, string body, CancellationToken ct)
    {
        byte[] payload = Encoding.UTF8.GetBytes(body);
        string reason = status == 200 ? "OK" : status == 404 ? "Not Found" : "Error";
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nAccess-Control-Allow-Origin: *\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");

        byte[] headBytes = Encoding.ASCII.GetBytes(head);
        await stream.WriteAsync(headBytes, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
        }
        catch (SocketException)
        {
            // ignore
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _listener = null;
        }
    }

    private sealed record PriceCheckPayload(bool Found, string Sku, string Name, long PriceMinor, decimal Price, string Unit, string Currency);
}
