using System.Globalization;

namespace NextIntegrations.Devices.Barcodes;

/// <summary>Whether a variable-measure (scale) label embeds a price or a weight.</summary>
public enum MeasureMode
{
    None,
    Price,
    Weight
}

/// <summary>
/// The decoded contents of a variable-measure EAN-13 (a label printed by a weighing scale):
/// the item code (PLU) and either the embedded price (in minor units) or weight (in grams).
/// </summary>
public readonly record struct VariableMeasure(MeasureMode Mode, int Plu, int EmbeddedValue)
{
    public bool IsRecognized => Mode != MeasureMode.None;

    /// <summary>Embedded weight as kilograms (weight mode only; otherwise zero).</summary>
    public decimal WeightKg => Mode == MeasureMode.Weight ? EmbeddedValue / 1000m : 0m;

    /// <summary>Embedded price in minor units / qəpik (price mode only; otherwise zero).</summary>
    public int PriceMinorUnits => Mode == MeasureMode.Price ? EmbeddedValue : 0;
}

/// <summary>
/// EAN-13 helpers: check-digit math, internal-barcode allocation for products that ship without a
/// barcode, and variable-measure (scale) labels that embed a PLU plus a weight or price. This is the
/// neutral, hardware-free barcode <em>format</em> — the scanner/scale devices speak it, the lib decodes
/// it, and the app receives the plain numbers (<see cref="VariableMeasure"/>) to map onto its catalogue.
///
/// GS1 reserves the prefixes "02" and "20"–"29" for in-store / restricted distribution. We use:
/// <list type="bullet">
///   <item>"29" + 10-digit sequence  → internal barcode for a barcode-less product;</item>
///   <item>"20" + PLU(5) + price(5)  → variable-measure label, embedded price (qəpik);</item>
///   <item>"21" + PLU(5) + weight(5) → variable-measure label, embedded weight (grams).</item>
/// </list>
/// Pure and deterministic — fully unit-testable, no hardware.
/// </summary>
public static class BarcodeService
{
    private const string InternalPrefix = "29";
    private const string PricePrefix = "20";
    private const string WeightPrefix = "21";
    private const int MaxPlu = 99_999;
    private const int MaxEmbedded = 99_999;

    /// <summary>Computes the EAN-13 check digit from the first 12 digits.</summary>
    public static int Ean13CheckDigit(ReadOnlySpan<char> first12)
    {
        if (first12.Length != 12)
        {
            throw new ArgumentException("EAN-13 gövdəsi 12 rəqəm olmalıdır.", nameof(first12));
        }

        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int digit = first12[i] - '0';
            if ((uint)digit > 9)
            {
                throw new ArgumentException("Yalnız rəqəmlər icazəlidir.", nameof(first12));
            }

            sum += i % 2 == 0 ? digit : digit * 3;
        }

        return (10 - (sum % 10)) % 10;
    }

    /// <summary>Appends the check digit to a 12-digit body, returning a full 13-digit EAN-13.</summary>
    public static string CompleteEan13(string first12)
    {
        ArgumentNullException.ThrowIfNull(first12);
        return first12 + Ean13CheckDigit(first12).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>True when <paramref name="code"/> is 13 digits with a valid EAN-13 check digit.</summary>
    public static bool IsValidEan13(string? code)
    {
        if (code is not { Length: 13 })
        {
            return false;
        }

        for (int i = 0; i < 13; i++)
        {
            if (!char.IsAsciiDigit(code[i]))
            {
                return false;
            }
        }

        return Ean13CheckDigit(code.AsSpan(0, 12)) == code[12] - '0';
    }

    /// <summary>Allocates an internal EAN-13 ("29" + 10-digit sequence + check) for a barcode-less product.</summary>
    public static string GenerateInternal(long sequence)
    {
        if (sequence is < 0 or > 9_999_999_999)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }

        return CompleteEan13(InternalPrefix + sequence.ToString("D10", CultureInfo.InvariantCulture));
    }

    /// <summary>Builds a price-embedded scale label: "20" + PLU(5) + price-in-qəpik(5) + check.</summary>
    public static string BuildPriceLabel(int plu, decimal price)
    {
        int minor = checked((int)Math.Round(price * 100m, MidpointRounding.AwayFromZero));
        return Build(PricePrefix, plu, minor);
    }

    /// <summary>Builds a weight-embedded scale label: "21" + PLU(5) + weight-in-grams(5) + check.</summary>
    public static string BuildWeightLabel(int plu, decimal weightKg)
    {
        int grams = checked((int)Math.Round(weightKg * 1000m, MidpointRounding.AwayFromZero));
        return Build(WeightPrefix, plu, grams);
    }

    /// <summary>
    /// Decodes a scanned barcode as a variable-measure label. Returns <see cref="MeasureMode.None"/>
    /// when it is not a recognised "20"/"21" scale label (e.g. an ordinary product barcode).
    /// </summary>
    public static VariableMeasure ParseVariableMeasure(string? code)
    {
        if (!IsValidEan13(code))
        {
            return default;
        }

        MeasureMode mode = code!.AsSpan(0, 2) switch
        {
            "20" => MeasureMode.Price,
            "21" => MeasureMode.Weight,
            _ => MeasureMode.None
        };

        if (mode == MeasureMode.None)
        {
            return default;
        }

        int plu = int.Parse(code.AsSpan(2, 5), CultureInfo.InvariantCulture);
        int value = int.Parse(code.AsSpan(7, 5), CultureInfo.InvariantCulture);
        return new VariableMeasure(mode, plu, value);
    }

    // EAN-13 module encodings (7 modules per digit). '1' = bar (dark), '0' = space.
    private static readonly string[] LeftOdd =
    [
        "0001101", "0011001", "0010011", "0111101", "0100011",
        "0110001", "0101111", "0111011", "0110111", "0001011"
    ];

    private static readonly string[] LeftEven =
    [
        "0100111", "0110011", "0011011", "0100001", "0011101",
        "0111001", "0000101", "0010001", "0001001", "0010111"
    ];

    private static readonly string[] Right =
    [
        "1110010", "1100110", "1101100", "1000010", "1011100",
        "1001110", "1010000", "1000100", "1001000", "1110100"
    ];

    // Parity pattern for the six left digits, selected by the first digit. 'L' = odd, 'G' = even.
    private static readonly string[] Parity =
    [
        "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG",
        "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL"
    ];

    /// <summary>
    /// Renders an EAN-13 to its 95 bar/space modules (true = dark bar), including the start, centre and
    /// end guard bars. Drives the on-screen label preview; throws if the code isn't a valid EAN-13.
    /// </summary>
    public static bool[] Ean13Modules(string code)
    {
        if (!IsValidEan13(code))
        {
            throw new ArgumentException("Düzgün EAN-13 deyil.", nameof(code));
        }

        var modules = new List<bool>(95);
        void Append(string pattern)
        {
            foreach (char c in pattern)
            {
                modules.Add(c == '1');
            }
        }

        string parity = Parity[code[0] - '0'];
        Append("101"); // start guard
        for (int i = 1; i <= 6; i++)
        {
            int digit = code[i] - '0';
            Append(parity[i - 1] == 'L' ? LeftOdd[digit] : LeftEven[digit]);
        }

        Append("01010"); // centre guard
        for (int i = 7; i <= 12; i++)
        {
            Append(Right[code[i] - '0']);
        }

        Append("101"); // end guard
        return [.. modules];
    }

    private static string Build(string prefix, int plu, int value)
    {
        if (plu is < 0 or > MaxPlu)
        {
            throw new ArgumentOutOfRangeException(nameof(plu));
        }

        if (value is < 0 or > MaxEmbedded)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        string body = prefix
            + plu.ToString("D5", CultureInfo.InvariantCulture)
            + value.ToString("D5", CultureInfo.InvariantCulture);
        return CompleteEan13(body);
    }
}
