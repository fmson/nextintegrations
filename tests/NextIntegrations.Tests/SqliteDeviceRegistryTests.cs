using NextIntegrations.Devices.Abstractions;
using NextIntegrations.Devices.Registry;
using Xunit;

namespace NextIntegrations.Tests;

public sealed class SqliteDeviceRegistryTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"ni-devices-{Guid.NewGuid():N}.db");

    [Fact]
    public void AddOrUpdate_PersistsAndResolvesByRole_AcrossReloads()
    {
        var registry = SqliteDeviceRegistry.Load(_dbPath);
        registry.AddOrUpdate(new DeviceConfig
        {
            Id = "printer-1",
            Role = DeviceRole.Printer,
            Protocol = DeviceProtocol.EscPos,
            Connection = DeviceConnectionKind.Network,
            Name = "Kassa printeri",
            Host = "10.0.0.5",
            Port = 9100
        });

        // A fresh load from the same DB sees it — i.e. another app would too.
        var reloaded = SqliteDeviceRegistry.Load(_dbPath);
        DeviceConfig? printer = reloaded.GetForRole(DeviceRole.Printer);

        Assert.NotNull(printer);
        Assert.Equal("10.0.0.5", printer!.Host);
        Assert.Single(reloaded.GetAll());
        Assert.Null(reloaded.GetForRole(DeviceRole.CashDrawer));
    }

    [Fact]
    public void Update_OverwritesSameId()
    {
        var registry = SqliteDeviceRegistry.Load(_dbPath);
        var device = new DeviceConfig { Id = "d1", Role = DeviceRole.Printer, Name = "A", Host = "1.1.1.1" };
        registry.AddOrUpdate(device);
        registry.AddOrUpdate(device with { Name = "B", Host = "2.2.2.2" });

        Assert.Single(registry.GetAll());
        Assert.Equal("2.2.2.2", registry.GetForRole(DeviceRole.Printer)!.Host);
    }

    [Fact]
    public void Remove_DeletesDevice()
    {
        var registry = SqliteDeviceRegistry.Load(_dbPath);
        registry.AddOrUpdate(new DeviceConfig { Id = "d1", Role = DeviceRole.Scanner, Name = "S" });
        registry.Remove("d1");
        Assert.Empty(registry.GetAll());
    }

    [Fact]
    public void SimulateMode_DefaultsTrue_AndPersists()
    {
        var registry = SqliteDeviceRegistry.Load(_dbPath);
        Assert.True(registry.SimulateMode);

        registry.SetSimulateMode(false);
        Assert.False(SqliteDeviceRegistry.Load(_dbPath).SimulateMode);
    }

    [Fact]
    public void ReplaceAll_SwapsTheWholeSet()
    {
        var registry = SqliteDeviceRegistry.Load(_dbPath);
        registry.AddOrUpdate(new DeviceConfig { Id = "old", Role = DeviceRole.Printer, Name = "Old" });

        registry.ReplaceAll([
            new DeviceConfig { Id = "n1", Role = DeviceRole.CashDrawer, Name = "Drawer" },
            new DeviceConfig { Id = "n2", Role = DeviceRole.Fiscal, Name = "Fiscal" }
        ]);

        Assert.Equal(2, registry.GetAll().Count);
        Assert.Null(registry.GetForRole(DeviceRole.Printer));
        Assert.NotNull(registry.GetForRole(DeviceRole.Fiscal));
    }

    public void Dispose()
    {
        foreach (string file in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
