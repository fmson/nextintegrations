namespace NextIntegrations.Devices.Abstractions;

/// <summary>Checks whether a configured device is currently reachable (for the connection monitor).</summary>
public interface IDeviceProbe
{
    Task<bool> CanReachAsync(DeviceConfig device, CancellationToken cancellationToken = default);
}
