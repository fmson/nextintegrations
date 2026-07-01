namespace NextIntegrations.Devices.Fiscal.Nba;

/// <summary>
/// Raised when the NBA "fiscalbox" API returns a non-success response code, or when an operation is
/// attempted before a successful <c>toLogin</c>. The <see cref="Code"/> maps to the protocol error table
/// (ZeroMQ API v1.2 fiscalbox §8 "Error Codes", p.60–65). Note that code <c>1205</c> ("document created,
/// but item names were not saved") is treated as success by <see cref="NbaFiscalClient"/> and is not raised.
/// </summary>
public sealed class NbaFiscalException : Exception
{
    /// <summary>Creates an exception for a protocol error response.</summary>
    /// <param name="code">The protocol response/error code (0 = success).</param>
    /// <param name="message">The error message; falls back to a generic text when null/empty.</param>
    /// <param name="info">Optional additional information from the response.</param>
    public NbaFiscalException(int code, string? message, string? info = null)
        : base(BuildMessage(code, message, info))
    {
        Code = code;
        Info = info;
    }

    /// <summary>Creates an exception with an explicit message and no protocol code.</summary>
    /// <param name="message">The error message.</param>
    public NbaFiscalException(string message)
        : base(message)
    {
    }

    /// <summary>The protocol response/error code (0 = success); -1 when not derived from a coded response.</summary>
    public int Code { get; } = -1;

    /// <summary>The optional <c>info</c> field from the failing response.</summary>
    public string? Info { get; }

    private static string BuildMessage(int code, string? message, string? info)
    {
        string text = string.IsNullOrWhiteSpace(message) ? "NBA fiscal operation failed" : message;
        string suffix = string.IsNullOrWhiteSpace(info) ? string.Empty : $" ({info})";
        return $"{text} [code {code}]{suffix}";
    }
}
