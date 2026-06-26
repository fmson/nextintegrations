namespace NextIntegrations.Devices.EscPos;

/// <summary>One printed line item. Amounts are in major currency units (e.g. 12.50).</summary>
public sealed record EscPosReceiptLine(string Name, decimal Quantity, decimal UnitPrice, decimal LineTotal);

/// <summary>
/// App-neutral receipt model handed to <see cref="EscPosDocument.BuildReceipt"/>. Each POS head maps its
/// own completed-sale type onto this, so the ESC/POS rendering is shared and identical across heads.
/// All amounts are in major currency units (manat), not minor units.
/// </summary>
public sealed record EscPosReceipt
{
    public string? StoreName { get; init; }

    public string? StoreAddress { get; init; }

    public string DocumentNumber { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Cashier label printed on the slip (name or id).</summary>
    public string Cashier { get; init; } = string.Empty;

    public IReadOnlyList<EscPosReceiptLine> Lines { get; init; } = [];

    public decimal Subtotal { get; init; }

    public decimal Discount { get; init; }

    public decimal Vat { get; init; }

    public decimal Total { get; init; }

    public string PaymentMethod { get; init; } = string.Empty;

    public decimal AmountPaid { get; init; }

    public decimal Change { get; init; }

    public string? FiscalToken { get; init; }

    public long? ZNumber { get; init; }
}
