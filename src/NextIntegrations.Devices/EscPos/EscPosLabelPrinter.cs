using NextIntegrations.Devices.Transport;

namespace NextIntegrations.Devices.EscPos;

/// <summary>Renders a price/barcode label as ESC/POS (GS k barcode) and sends it over the transport.</summary>
public sealed class EscPosLabelPrinter
{
    private readonly IDeviceTransport _transport;

    public EscPosLabelPrinter(IDeviceTransport transport) => _transport = transport;

    public Task PrintLabelAsync(
        string name, string ean13, string priceText, int copies = 1, CancellationToken cancellationToken = default)
    {
        byte[] document = EscPosDocument.BuildLabel(name, ean13, priceText, copies);
        return _transport.SendAsync(document, cancellationToken);
    }
}
