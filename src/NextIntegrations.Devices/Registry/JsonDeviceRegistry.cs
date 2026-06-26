using System.Text.Json;
using System.Text.Json.Serialization;
using NextIntegrations.Devices.Abstractions;

namespace NextIntegrations.Devices.Registry;

/// <summary>
/// File-backed device registry (cross-platform) shared by every Horeca POS head. Persists the simulate
/// flag and the device list to <c>devices.json</c> under a shared app-data folder, so a device added in
/// one app is seen by the others. Changes apply on next resolve. Toggling simulate keeps device configs
/// intact — it only bypasses real hardware.
/// </summary>
public sealed class JsonDeviceRegistry : IDeviceRegistry
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private sealed class RegistryData
    {
        public bool SimulateMode { get; set; } = true; // default: run locally, real devices bypassed
        public List<DeviceConfig> Devices { get; set; } = [];
    }

    private readonly object _gate = new();
    private readonly RegistryData _data;
    private readonly string _filePath;

    private JsonDeviceRegistry(RegistryData data, string filePath)
    {
        _data = data;
        _filePath = filePath;
    }

    /// <summary>Loads the registry from <paramref name="filePath"/> (shared app-data path when null).</summary>
    public static JsonDeviceRegistry Load(string? filePath = null)
    {
        // Legacy migration only bootstraps the shared default location; an explicit path is used verbatim.
        string path = filePath ?? DefaultFilePath();
        if (filePath is null)
        {
            MigrateLegacyIfNeeded(path);
        }
        try
        {
            if (File.Exists(path))
            {
                RegistryData? loaded = JsonSerializer.Deserialize<RegistryData>(File.ReadAllText(path), Options);
                if (loaded is not null)
                {
                    loaded.Devices ??= [];
                    return new JsonDeviceRegistry(loaded, path);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt/locked registry → start with defaults (simulate on, no devices).
        }

        return new JsonDeviceRegistry(new RegistryData(), path);
    }

    public bool SimulateMode
    {
        get
        {
            lock (_gate)
            {
                return _data.SimulateMode;
            }
        }
    }

    public void SetSimulateMode(bool value)
    {
        lock (_gate)
        {
            if (_data.SimulateMode == value)
            {
                return;
            }

            _data.SimulateMode = value;
            Save();
        }
    }

    public IReadOnlyList<DeviceConfig> GetAll()
    {
        lock (_gate)
        {
            return _data.Devices.ToList();
        }
    }

    public DeviceConfig? GetForRole(DeviceRole role)
    {
        lock (_gate)
        {
            return _data.Devices.Find(device => device.Role == role && device.Enabled);
        }
    }

    public void AddOrUpdate(DeviceConfig device)
    {
        ArgumentNullException.ThrowIfNull(device);
        lock (_gate)
        {
            _data.Devices.RemoveAll(existing => existing.Id == device.Id);
            _data.Devices.Add(device);
            Save();
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            if (_data.Devices.RemoveAll(device => device.Id == id) > 0)
            {
                Save();
            }
        }
    }

    public void ReplaceAll(IReadOnlyList<DeviceConfig> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        lock (_gate)
        {
            _data.Devices.Clear();
            _data.Devices.AddRange(devices);
            Save();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_data, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Non-fatal: the change just won't persist this run.
        }
    }

    /// <summary>The shared registry file: <c>LocalApplicationData/Horeca/devices.json</c>.</summary>
    public static string DefaultFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Horeca",
        "devices.json");

    /// <summary>
    /// One-time migration: if the shared registry does not exist yet but a legacy per-app
    /// <c>NextCashier/devices.json</c> does, copy it into the shared location so existing device
    /// configuration carries over and is then visible to every head.
    /// </summary>
    private static void MigrateLegacyIfNeeded(string targetPath)
    {
        try
        {
            if (File.Exists(targetPath))
            {
                return;
            }

            string legacy = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NextCashier",
                "devices.json");
            if (File.Exists(legacy))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(legacy, targetPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Migration is best-effort; a fresh registry is created if it fails.
        }
    }
}
