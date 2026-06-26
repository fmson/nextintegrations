using System.IO.Ports;
using System.Net.Sockets;
using NextIntegrations.Devices.Abstractions;

namespace NextIntegrations.Devices;

/// <summary>
/// Reachability check per transport: TCP connect for network devices; port presence for serial/USB.
/// Bluetooth probing is platform-specific and reported as unreachable until a per-platform adapter exists.
/// </summary>
public sealed class DeviceProbe : IDeviceProbe
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);

    public async Task<bool> CanReachAsync(DeviceConfig device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        try
        {
            switch (device.Connection)
            {
                case DeviceConnectionKind.Network:
                    using (var client = new TcpClient())
                    using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        cts.CancelAfter(ConnectTimeout);
                        await client.ConnectAsync(
                            string.IsNullOrWhiteSpace(device.Host) ? "127.0.0.1" : device.Host,
                            device.Port == 0 ? 9100 : device.Port,
                            cts.Token).ConfigureAwait(false);
                        return client.Connected;
                    }

                case DeviceConnectionKind.Serial or DeviceConnectionKind.Usb:
                    string? port = !string.IsNullOrWhiteSpace(device.SerialPort) ? device.SerialPort : device.Address;
                    if (string.IsNullOrWhiteSpace(port))
                    {
                        return false;
                    }

                    bool listed = SerialPort.GetPortNames().Contains(port, StringComparer.OrdinalIgnoreCase);
                    return listed || File.Exists(port); // /dev/cu.* and similar may not be in GetPortNames

                default:
                    return false; // Bluetooth / None: not probeable yet
            }
        }
#pragma warning disable CA1031 // Any probe failure simply means "unreachable" — never throw to the monitor.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }
}
