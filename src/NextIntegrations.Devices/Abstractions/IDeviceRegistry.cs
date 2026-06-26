namespace NextIntegrations.Devices.Abstractions;

/// <summary>
/// Persisted catalog of configured devices (the device "baza"), shared across Horeca POS heads.
/// Adapters are resolved from it, so adding a device here makes it active on the next resolve.
/// The default implementation (JsonDeviceRegistry) is file-backed at a shared path so every app
/// sees the same devices and any newly added integration at once.
/// </summary>
public interface IDeviceRegistry
{
    /// <summary>
    /// When true, real devices are bypassed and simulators are used — the app runs fully locally.
    /// Device configs are kept untouched, so turning this off restores real-device operation.
    /// Default true. Administrator-only toggle.
    /// </summary>
    bool SimulateMode { get; }

    void SetSimulateMode(bool value);

    IReadOnlyList<DeviceConfig> GetAll();

    /// <summary>The first enabled device for a role, or null when none is configured (use the simulator).</summary>
    DeviceConfig? GetForRole(DeviceRole role);

    void AddOrUpdate(DeviceConfig device);

    void Remove(string id);

    /// <summary>Replaces the entire device set (used by config restore — not a merge).</summary>
    void ReplaceAll(IReadOnlyList<DeviceConfig> devices);
}
