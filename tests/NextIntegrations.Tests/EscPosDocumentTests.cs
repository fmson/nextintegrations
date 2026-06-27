using System.Text;
using NextIntegrations.Devices.EscPos;
using Xunit;

namespace NextIntegrations.Tests;

/// <summary>Byte-level ESC/POS rendering — the single shared renderer used by every POS head.</summary>
public sealed class EscPosDocumentTests
{
    private static EscPosReceipt SampleReceipt(string? storeName = "ACME") => new()
    {
        StoreName = storeName,
        DocumentNumber = "DOC-1001",
        CreatedAt = DateTimeOffset.UtcNow,
        Cashier = "001",
        Lines = [new EscPosReceiptLine("Alma", 2m, 2.40m, 4.80m)],
        Subtotal = 4.80m,
        Vat = 0.73m,
        Discount = 0m,
        Total = 4.80m,
        PaymentMethod = "Cash",
        AmountPaid = 5.00m,
        Change = 0.20m,
    };

    [Fact]
    public void DrawerKick_IsStandardEscPosCommand() =>
        Assert.Equal(new byte[] { 0x1B, 0x70, 0x00, 0x19, 0xFA }, EscPosDocument.DrawerKick);

    [Fact]
    public void BuildReceipt_StartsWithInit_EndsWithCut_AndContainsKeyFields()
    {
        byte[] doc = EscPosDocument.BuildReceipt(SampleReceipt());

        Assert.Equal(0x1B, doc[0]); // ESC
        Assert.Equal(0x40, doc[1]); // @  (init)
        Assert.Equal(new byte[] { 0x1D, 0x56, 0x42, 0x00 }, doc[^4..]); // GS V 66 0 (cut)

        string text = Encoding.Latin1.GetString(doc);
        Assert.Contains("DOC-1001", text, StringComparison.Ordinal);
        Assert.Contains("YEKUN", text, StringComparison.Ordinal);
        Assert.Contains("4.80", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReceipt_StripsControlBytes_FromTextFields()
    {
        string injected = "ACME" + (char)0x1B + "@Store";
        byte[] doc = EscPosDocument.BuildReceipt(SampleReceipt(injected));

        string text = Encoding.Latin1.GetString(doc);
        Assert.Contains("ACME@Store", text, StringComparison.Ordinal);   // control byte removed, letters kept
        Assert.DoesNotContain(injected, text, StringComparison.Ordinal); // the injected ESC is gone
    }

    [Fact]
    public void BuildLabel_EmitsEan13BarcodeCommand_PerCopy()
    {
        byte[] doc = EscPosDocument.BuildLabel("Test Mal", "2000000000017", "2.50", copies: 2);

        Assert.Equal(0x1B, doc[0]);
        Assert.Equal(0x40, doc[1]);
        Assert.Equal(new byte[] { 0x1D, 0x56, 0x42, 0x00 }, doc[^4..]);

        int barcodeCommands = CountSubsequence(doc, [0x1D, 0x6B, 0x02]); // GS k 2 (EAN-13), once per copy
        Assert.Equal(2, barcodeCommands);
        Assert.Contains("200000000001", Encoding.ASCII.GetString(doc), StringComparison.Ordinal); // 12 data digits
    }

    [Fact]
    public void BuildLabel_RejectsNonEan13Length() =>
        Assert.Throws<ArgumentException>(() => EscPosDocument.BuildLabel("x", "123", "1.00"));

    private static int CountSubsequence(byte[] haystack, byte[] needle)
    {
        int count = 0;
        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                count++;
            }
        }

        return count;
    }
}
