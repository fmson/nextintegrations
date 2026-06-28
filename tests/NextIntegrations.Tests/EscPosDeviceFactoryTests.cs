using NextIntegrations.Devices.Abstractions;
using NextIntegrations.Devices.EscPos;
using Xunit;

namespace NextIntegrations.Tests;

/// <summary>The config→device factory: the lib builds the transport + device from a neutral config so the
/// app never holds a transport. (Network transport is lazy — constructing a device does not connect.)</summary>
public sealed class EscPosDeviceFactoryTests
{
    private static DeviceConfig NetworkPrinter() => new()
    {
        Role = DeviceRole.Printer,
        Protocol = DeviceProtocol.EscPos,
        Connection = DeviceConnectionKind.Network,
        Host = "192.168.1.50",
        Port = 9100,
        CharactersPerLine = 42,
    };

    [Fact]
    public void Builds_AllEscPosDevices_FromConfig()
    {
        DeviceConfig config = NetworkPrinter();
        Assert.NotNull(EscPosDeviceFactory.ReceiptPrinter(config));
        Assert.NotNull(EscPosDeviceFactory.LabelPrinter(config));
        Assert.NotNull(EscPosDeviceFactory.CashDrawer(config));
        Assert.NotNull(EscPosDeviceFactory.CustomerDisplay(config));
    }

    [Fact]
    public void NullConfig_Throws() =>
        Assert.Throws<ArgumentNullException>(() => EscPosDeviceFactory.ReceiptPrinter(null!));
}
