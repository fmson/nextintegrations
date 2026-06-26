namespace NextIntegrations.Devices.Transport;

/// <summary>A raw byte sink to a device (printer, drawer-via-printer, …), independent of wiring.</summary>
public interface IDeviceTransport
{
    Task SendAsync(byte[] data, CancellationToken cancellationToken = default);
}
