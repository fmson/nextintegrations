using NextIntegrations.Devices.Abstractions;

namespace NextIntegrations.Devices.Transport;

/// <summary>
/// Builds the right transport for a device's connection. Network (TCP) is implemented for all
/// platforms; serial/USB/Bluetooth are platform-specific and added per head as devices arrive.
/// </summary>
public static class DeviceTransportFactory
{
    public static IDeviceTransport Create(DeviceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return config.Connection switch
        {
            DeviceConnectionKind.Network =>
                new NetworkDeviceTransport(
                    string.IsNullOrWhiteSpace(config.Host) ? "127.0.0.1" : config.Host,
                    config.Port == 0 ? 9100 : config.Port),

            // USB ESC/POS printers normally present a virtual COM/tty port, so both Serial and USB use it.
            DeviceConnectionKind.Serial or DeviceConnectionKind.Usb =>
                new SerialDeviceTransport(
                    PortNameOf(config) ?? throw new InvalidOperationException(
                        $"Device '{config.Name}' needs a serial/COM port (e.g. COM3, /dev/tty.usbserial-xxxx)."),
                    config.BaudRate),

            DeviceConnectionKind.Bluetooth =>
                throw new PlatformNotSupportedException(
                    "Bluetooth transport not wired yet — add a per-platform adapter (iOS CoreBluetooth/MFi, Android, Win)."),

            _ => throw new InvalidOperationException(
                $"Device '{config.Name}' ({config.Role}) has no connection configured.")
        };
    }

    private static string? PortNameOf(DeviceConfig config) =>
        !string.IsNullOrWhiteSpace(config.SerialPort) ? config.SerialPort
        : !string.IsNullOrWhiteSpace(config.Address) ? config.Address
        : null;
}
