namespace NextIntegrations.Devices.Fiscal.Nba;

/// <summary>
/// The request/response transport used by <see cref="NbaFiscalClient"/>. The production implementation
/// (<see cref="NetMqNbaTransport"/>) is a raw ZeroMQ (ZMTP 3.x) REQ socket; tests supply an in-memory
/// or in-process REP double. This split keeps the client — envelope building, error mapping, session
/// lifecycle — fully unit-testable without any device or ZeroMQ dependency.
/// </summary>
public interface INbaTransport : IDisposable
{
    /// <summary>
    /// Sends one JSON request and returns the single JSON response (one REQ→REP round-trip). Implementations
    /// must serialize concurrent calls — a fiscal register is a single-threaded device.
    /// </summary>
    /// <param name="requestJson">The request envelope, already serialized to JSON.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The response envelope JSON.</returns>
    /// <exception cref="NbaFiscalException">The device was unreachable or did not answer within the timeout.</exception>
    Task<string> SendReceiveAsync(string requestJson, CancellationToken cancellationToken = default);
}
