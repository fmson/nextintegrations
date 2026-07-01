using NetMQ;
using NetMQ.Sockets;

namespace NextIntegrations.Devices.Fiscal.Nba;

/// <summary>
/// Production <see cref="INbaTransport"/> over a raw ZeroMQ (ZMTP 3.x) REQ socket — the wire the NBA
/// "fiscalbox" speaks (default <c>tcp://&lt;ip&gt;:26767</c>). Each round-trip uses a fresh, short-lived
/// <see cref="RequestSocket"/> (the "Lazy Pirate" pattern): this sidesteps the REQ socket's strict
/// send→receive state machine (a lost reply otherwise wedges the socket) and any thread-affinity issues,
/// at the cost of a cheap TCP reconnect per request — negligible at fiscal-receipt frequency.
/// </summary>
/// <remarks>
/// Calls are serialized by a semaphore so the single physical device is never driven concurrently. NetMQ
/// is a managed ZeroMQ implementation (no native <c>libzmq</c>), so this works on Windows, Linux and macOS.
/// </remarks>
public sealed class NetMqNbaTransport : INbaTransport
{
    private readonly string _address;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Creates the transport for a ZeroMQ endpoint.</summary>
    /// <param name="address">The connect address, e.g. <c>tcp://127.0.0.1:26767</c>.</param>
    /// <param name="timeout">Per-request send/receive timeout.</param>
    public NetMqNbaTransport(string address, TimeSpan timeout)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        _address = address;
        _timeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : timeout;
    }

    /// <inheritdoc />
    public async Task<string> SendReceiveAsync(string requestJson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestJson);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => SendReceiveCore(requestJson), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string SendReceiveCore(string requestJson)
    {
        // A fresh socket per call: created, used and disposed on this single thread — no REQ-stuck state,
        // no cross-thread socket access. Linger 0 so Dispose never blocks on undeliverable frames.
        using RequestSocket socket = new();
        socket.Options.Linger = TimeSpan.Zero;
        socket.Connect(_address);

        if (!socket.TrySendFrame(_timeout, requestJson))
        {
            throw new NbaFiscalException(
                $"NBA fiscalbox: request send timed out after {_timeout.TotalSeconds:0.#}s (device unreachable at {_address}?).");
        }

        if (!socket.TryReceiveFrameString(_timeout, out string? reply) || reply is null)
        {
            throw new NbaFiscalException(
                $"NBA fiscalbox: no response within {_timeout.TotalSeconds:0.#}s from {_address}.");
        }

        return reply;
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();
}
