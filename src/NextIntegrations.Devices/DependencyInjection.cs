using NextIntegrations.Devices.Abstractions;
using NextIntegrations.Devices.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace NextIntegrations.Devices;

/// <summary>
/// Registers the shared device layer: the database-backed registry ("baza", a shared SQLite DB across
/// heads) and the reachability probe. App-specific adapters (printer/drawer/display bound to each app's
/// ports and license gating) are composed in each app's composition root on top of these.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Adds <see cref="IDeviceRegistry"/> (shared <c>devices.db</c>) and <see cref="IDeviceProbe"/>.
    /// Pass <paramref name="databasePath"/> to override the shared default DB path (mainly for tests).
    /// </summary>
    public static IServiceCollection AddNextIntegrationsDevices(this IServiceCollection services, string? databasePath = null)
    {
        services.AddSingleton<IDeviceRegistry>(_ => SqliteDeviceRegistry.Load(databasePath));
        services.AddSingleton<IDeviceProbe, DeviceProbe>();
        return services;
    }
}
