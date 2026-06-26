using NextIntegrations.Devices.Transport;

namespace NextIntegrations.Devices.EscPos;

/// <summary>
/// Real cash drawer: most drawers are wired to the receipt printer and opened with the ESC/POS
/// drawer-kick command, so this sends the kick over the same transport as the printer.
/// </summary>
public sealed class EscPosCashDrawer
{
    private readonly IDeviceTransport _transport;

    public EscPosCashDrawer(IDeviceTransport transport) => _transport = transport;

    public Task OpenAsync(CancellationToken cancellationToken = default) =>
        _transport.SendAsync(EscPosDocument.DrawerKick, cancellationToken);
}
