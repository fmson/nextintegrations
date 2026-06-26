using System.Net.Sockets;

namespace NextIntegrations.Devices.Transport;

/// <summary>
/// Sends bytes to a network device over raw TCP (e.g. an ESC/POS printer on port 9100).
/// Opens a connection per job, which most receipt printers expect. Works on every platform.
/// </summary>
public sealed class NetworkDeviceTransport : IDeviceTransport
{
    private readonly string _host;
    private readonly int _port;
    private readonly TimeSpan _timeout;

    public NetworkDeviceTransport(string host, int port, TimeSpan? timeout = null)
    {
        _host = host;
        _port = port;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
    }

    public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(_host, _port, linked.Token).ConfigureAwait(false);
        await using NetworkStream stream = client.GetStream();
        await stream.WriteAsync(data, linked.Token).ConfigureAwait(false);
        await stream.FlushAsync(linked.Token).ConfigureAwait(false);
    }
}
