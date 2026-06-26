using System.IO.Ports;

namespace NextIntegrations.Devices.Transport;

/// <summary>
/// Sends bytes over a serial / virtual-COM port. Most USB ESC/POS printers expose such a port
/// (Windows: COM3; Linux: /dev/ttyUSB0; macOS: /dev/tty.usbserial-xxxx). Desktop/Linux only.
/// </summary>
public sealed class SerialDeviceTransport : IDeviceTransport
{
    private readonly string _portName;
    private readonly int _baudRate;

    public SerialDeviceTransport(string portName, int baudRate)
    {
        _portName = portName;
        _baudRate = baudRate <= 0 ? 9600 : baudRate;
    }

    public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Serial ports are not available on this platform.");
        }

        using var port = new SerialPort(_portName, _baudRate);
        port.Open();
        await port.BaseStream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        await port.BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
