using NextIntegrations.Devices.Abstractions;
using NextIntegrations.Devices.Transport;

namespace NextIntegrations.Devices.EscPos;

/// <summary>
/// Builds a ready-to-use ESC/POS device straight from a neutral <see cref="DeviceConfig"/>. The library
/// creates the transport (network/serial) and owns the connection, so a consuming app only ever passes
/// config and calls high-level operations — it never holds a transport or emits raw bytes itself.
/// </summary>
public static class EscPosDeviceFactory
{
    /// <summary>A receipt printer wired to the configured connection.</summary>
    public static EscPosReceiptPrinter ReceiptPrinter(DeviceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new EscPosReceiptPrinter(DeviceTransportFactory.Create(config), CharactersPerLine(config));
    }

    /// <summary>A label printer wired to the configured connection.</summary>
    public static EscPosLabelPrinter LabelPrinter(DeviceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new EscPosLabelPrinter(DeviceTransportFactory.Create(config));
    }

    /// <summary>A cash drawer wired to the configured connection.</summary>
    public static EscPosCashDrawer CashDrawer(DeviceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new EscPosCashDrawer(DeviceTransportFactory.Create(config));
    }

    /// <summary>A customer pole/VFD display wired to the configured connection.</summary>
    public static EscPosCustomerDisplay CustomerDisplay(DeviceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        return new EscPosCustomerDisplay(DeviceTransportFactory.Create(config), CharactersPerLine(config));
    }

    private static int CharactersPerLine(DeviceConfig config) => config.CharactersPerLine > 0 ? config.CharactersPerLine : 48;
}
