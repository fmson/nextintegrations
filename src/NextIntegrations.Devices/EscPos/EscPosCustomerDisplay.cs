using System.Text;
using NextIntegrations.Devices.Transport;

namespace NextIntegrations.Devices.EscPos;

/// <summary>
/// Two-line customer pole / VFD display (Epson DM-D and generic-compatible): ESC @ clears, then the two
/// lines are written with a CR/LF between them so the device wraps to its second row. Lines are truncated
/// to the configured width (a customer display is at most ~20 columns).
/// </summary>
public sealed class EscPosCustomerDisplay
{
    private static readonly byte[] Init = [0x1B, 0x40];     // ESC @ — initialise / clear
    private static readonly byte[] NewLine = [0x0D, 0x0A];  // CR LF — move to the second row

    private readonly IDeviceTransport _transport;
    private readonly int _width;

    public EscPosCustomerDisplay(IDeviceTransport transport, int charactersPerLine = 20)
    {
        _transport = transport;
        _width = Math.Clamp(charactersPerLine, 1, 20);
    }

    public Task ShowAsync(string primary, string secondary, CancellationToken cancellationToken = default)
    {
        using MemoryStream stream = new();
        stream.Write(Init);
        WriteLine(stream, primary);
        stream.Write(NewLine);
        WriteLine(stream, secondary);
        return _transport.SendAsync(stream.ToArray(), cancellationToken);
    }

    private void WriteLine(Stream stream, string? text)
    {
        string clean = Strip(text);
        if (clean.Length > _width)
        {
            clean = clean[.._width];
        }

        // Latin1 keeps a 1:1 byte mapping; VFD displays use their own code page for accented glyphs.
        stream.Write(Encoding.Latin1.GetBytes(clean));
    }

    private static string Strip(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        StringBuilder sb = new(text.Length);
        foreach (char c in text)
        {
            if (!char.IsControl(c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
