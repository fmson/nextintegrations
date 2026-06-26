using NextIntegrations.Devices.Transport;

namespace NextIntegrations.Devices.EscPos;

/// <summary>Renders a neutral receipt model as ESC/POS and sends it over the device transport.</summary>
public sealed class EscPosReceiptPrinter
{
    private readonly IDeviceTransport _transport;
    private readonly int _charactersPerLine;

    public EscPosReceiptPrinter(IDeviceTransport transport, int charactersPerLine = 48)
    {
        _transport = transport;
        _charactersPerLine = charactersPerLine;
    }

    public Task PrintAsync(EscPosReceipt receipt, CancellationToken cancellationToken = default)
    {
        byte[] document = EscPosDocument.BuildReceipt(receipt, _charactersPerLine);
        return _transport.SendAsync(document, cancellationToken);
    }

    /// <summary>Prints the Settings self-test slip.</summary>
    public Task PrintTestAsync(string? brand = null, CancellationToken cancellationToken = default)
    {
        byte[] document = EscPosDocument.BuildTest(brand, _charactersPerLine);
        return _transport.SendAsync(document, cancellationToken);
    }
}
