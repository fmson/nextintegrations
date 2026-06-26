using System.Globalization;
using System.Text;

namespace NextIntegrations.Devices.EscPos;

/// <summary>
/// Builds ESC/POS byte streams (open standard) for receipt/label printing and the cash-drawer kick.
/// Pure and deterministic so it can be unit-tested without hardware, and app-neutral so every Horeca
/// POS head shares the exact same rendering.
/// </summary>
public static class EscPosDocument
{
    // Drawer kick on pin 2: ESC p 0 t1 t2 (1B 70 00 19 FA) — pulse ~25/250 ms.
    public static readonly byte[] DrawerKick = [0x1B, 0x70, 0x00, 0x19, 0xFA];

    private static readonly byte[] Init = [0x1B, 0x40];          // ESC @
    private static readonly byte[] AlignLeft = [0x1B, 0x61, 0x00];
    private static readonly byte[] AlignCenter = [0x1B, 0x61, 0x01];
    private static readonly byte[] BoldOn = [0x1B, 0x45, 0x01];
    private static readonly byte[] BoldOff = [0x1B, 0x45, 0x00];
    private static readonly byte[] FeedAndCut = [0x1D, 0x56, 0x42, 0x00]; // GS V 66 0 — feed + partial cut

    public static byte[] BuildReceipt(EscPosReceipt receipt, int charactersPerLine = 48)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        int width = charactersPerLine < 16 ? 16 : charactersPerLine;

        using var buffer = new MemoryStream();
        buffer.Write(Init);

        buffer.Write(AlignCenter);
        if (!string.IsNullOrWhiteSpace(receipt.StoreName))
        {
            buffer.Write(BoldOn);
            WriteLine(buffer, receipt.StoreName);
            buffer.Write(BoldOff);
        }

        if (!string.IsNullOrWhiteSpace(receipt.StoreAddress))
        {
            WriteLine(buffer, receipt.StoreAddress);
        }

        buffer.Write(AlignLeft);

        WriteLine(buffer, $"Cek: {receipt.DocumentNumber}");
        WriteLine(buffer, receipt.CreatedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        WriteLine(buffer, $"Kassir: {receipt.Cashier}");
        WriteLine(buffer, new string('-', width));

        foreach (EscPosReceiptLine line in receipt.Lines)
        {
            WriteLine(buffer, line.Name);
            string left = $"  {Quantity(line.Quantity)} x {Amount(line.UnitPrice)}";
            WriteLine(buffer, TwoColumns(left, Amount(line.LineTotal), width));
        }

        WriteLine(buffer, new string('-', width));
        WriteLine(buffer, TwoColumns("Ara cem", Amount(receipt.Subtotal), width));
        if (receipt.Discount != 0)
        {
            WriteLine(buffer, TwoColumns("Endirim", Amount(receipt.Discount), width));
        }

        WriteLine(buffer, TwoColumns("EDV", Amount(receipt.Vat), width));
        buffer.Write(BoldOn);
        WriteLine(buffer, TwoColumns("YEKUN", Amount(receipt.Total), width));
        buffer.Write(BoldOff);

        WriteLine(buffer, TwoColumns("Odenis", receipt.PaymentMethod, width));
        WriteLine(buffer, TwoColumns("Odenilen", Amount(receipt.AmountPaid), width));
        WriteLine(buffer, TwoColumns("Qaytarilan", Amount(receipt.Change), width));

        if (!string.IsNullOrEmpty(receipt.FiscalToken))
        {
            WriteLine(buffer, new string('-', width));
            WriteLine(buffer, $"Fiskal: {receipt.FiscalToken}");
            if (receipt.ZNumber is { } z)
            {
                WriteLine(buffer, $"Z: {z}");
            }
        }

        WriteLine(buffer, new string('-', width));
        buffer.Write(AlignCenter);
        WriteLine(buffer, "Tesekkur edirik!");
        buffer.Write(AlignLeft);

        buffer.Write([0x0A, 0x0A, 0x0A]); // feed before cut
        buffer.Write(FeedAndCut);
        return buffer.ToArray();
    }

    /// <summary>
    /// Builds a product price/barcode label (or several copies): product name, the EAN-13 barcode rendered
    /// by the printer (GS k), the human-readable number, and the price. Used for barcode-less products and
    /// scale labels. Each copy is fed and cut individually.
    /// </summary>
    public static byte[] BuildLabel(string name, string ean13, string priceText, int copies = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ean13);
        if (ean13.Length is not (12 or 13))
        {
            throw new ArgumentException("EAN-13 üçün 12 və ya 13 rəqəm gözlənilir.", nameof(ean13));
        }

        // The printer recomputes the check digit from the first 12 digits (GS k function A).
        string body = ean13[..12];
        int count = copies < 1 ? 1 : copies;

        using var buffer = new MemoryStream();
        buffer.Write(Init);
        for (int i = 0; i < count; i++)
        {
            buffer.Write(AlignCenter);
            buffer.Write(BoldOn);
            WriteLine(buffer, name ?? string.Empty);
            buffer.Write(BoldOff);

            buffer.Write([0x1D, 0x68, 0x50]);       // GS h 80  — barcode height
            buffer.Write([0x1D, 0x77, 0x02]);       // GS w 2   — module width
            buffer.Write([0x1D, 0x48, 0x02]);       // GS H 2   — HRI text below the bars
            buffer.Write([0x1D, 0x6B, 0x02]);       // GS k 2   — EAN-13
            buffer.Write(Encoding.ASCII.GetBytes(body));
            buffer.WriteByte(0x00);                 // NUL terminator for function A

            if (!string.IsNullOrWhiteSpace(priceText))
            {
                buffer.Write(BoldOn);
                WriteLine(buffer, priceText);
                buffer.Write(BoldOff);
            }

            buffer.Write(AlignLeft);
            buffer.Write([0x0A, 0x0A]); // feed before cut
            buffer.Write(FeedAndCut);
        }

        return buffer.ToArray();
    }

    /// <summary>A short self-test slip used by the Settings "test print" button.</summary>
    public static byte[] BuildTest(string? brand = null, int charactersPerLine = 48)
    {
        int width = charactersPerLine < 16 ? 16 : charactersPerLine;
        using var buffer = new MemoryStream();
        buffer.Write(Init);
        buffer.Write(AlignCenter);
        buffer.Write(BoldOn);
        WriteLine(buffer, string.IsNullOrWhiteSpace(brand) ? "Horeca POS" : brand);
        buffer.Write(BoldOff);
        WriteLine(buffer, "TEST CAP");
        buffer.Write(AlignLeft);
        WriteLine(buffer, new string('-', width));
        WriteLine(buffer, DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        WriteLine(buffer, "Printer ve qutu testi OK");
        buffer.Write([0x0A, 0x0A, 0x0A]);
        buffer.Write(FeedAndCut);
        return buffer.ToArray();
    }

    private static void WriteLine(Stream stream, string text)
    {
        // Strip control characters first so text fields (product/store names, etc.) cannot inject raw
        // ESC/POS command bytes into the stream. Latin1 then keeps a 1:1 byte mapping for the rest;
        // printer-specific codepage tuning (AZ/TR glyphs) is a follow-up.
        byte[] bytes = Encoding.Latin1.GetBytes(StripControl(text));
        stream.Write(bytes);
        stream.WriteByte(0x0A); // LF
    }

    private static string StripControl(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (!char.IsControl(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string TwoColumns(string left, string right, int width)
    {
        if (left.Length + right.Length + 1 > width)
        {
            int max = Math.Max(0, width - right.Length - 1);
            left = left.Length > max ? left[..max] : left;
        }

        int pad = Math.Max(1, width - left.Length - right.Length);
        return left + new string(' ', pad) + right;
    }

    private static string Amount(decimal amount) =>
        amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Quantity(decimal quantity) =>
        quantity.ToString("0.###", CultureInfo.InvariantCulture);
}
