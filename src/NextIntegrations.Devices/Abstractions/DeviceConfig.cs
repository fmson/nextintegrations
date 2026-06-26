namespace NextIntegrations.Devices.Abstractions;

/// <summary>What a configured device is used for.</summary>
public enum DeviceRole
{
    Printer,
    CashDrawer,
    Scanner,
    PaymentTerminal,
    Fiscal,
    CustomerDisplay
}

/// <summary>The command/wire protocol an adapter speaks. New protocols are added here.</summary>
public enum DeviceProtocol
{
    Simulated,
    EscPos,      // thermal receipt printers + drawer kick (open standard)
    HidKeyboard, // barcode scanners that act as a keyboard wedge
    BankEcr,     // semi-integrated bank POS terminal (vendor protocol)
    FiscalBox    // online fiscal device (NKA)
}

/// <summary>How the device is physically reached.</summary>
public enum DeviceConnectionKind
{
    None,
    Network,   // TCP/IP — works on every platform
    Serial,    // RS-232 / COM (desktop, Linux)
    Usb,       // platform-specific
    Bluetooth  // platform-specific
}

/// <summary>
/// One row of the device registry ("baza"): a device's role, protocol and connection.
/// Adding/enabling a row makes the matching adapter active — business logic is untouched.
/// Shared by every Horeca POS head, so the same configured device is visible to all of them.
/// </summary>
public sealed record DeviceConfig
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public DeviceRole Role { get; init; }

    public DeviceProtocol Protocol { get; init; } = DeviceProtocol.Simulated;

    public DeviceConnectionKind Connection { get; init; } = DeviceConnectionKind.None;

    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    // Network
    public string? Host { get; init; }
    public int Port { get; init; }

    // Serial
    public string? SerialPort { get; init; }
    public int BaudRate { get; init; } = 9600;

    // Bluetooth / USB
    public string? Address { get; init; }

    /// <summary>Receipt width in characters (80 mm ≈ 48, 58 mm ≈ 32).</summary>
    public int CharactersPerLine { get; init; } = 48;
}
