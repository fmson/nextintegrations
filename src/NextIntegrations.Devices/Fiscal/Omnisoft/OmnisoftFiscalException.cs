namespace NextIntegrations.Devices.Fiscal.Omnisoft;

/// <summary>
/// Raised when the Omnisoft fiscal API returns a non-success response code, or when an operation is
/// attempted before a successful <c>Login</c>. The <see cref="Code"/> maps to the protocol error tables
/// (PDF §9 token/error codes p.58–62, §10 API error codes p.63).
/// </summary>
public sealed class OmnisoftFiscalException : Exception
{
    /// <summary>Creates an exception for a protocol error response.</summary>
    /// <param name="code">The protocol response/error code (0 = success).</param>
    /// <param name="message">The error message; falls back to a code-derived text when null/empty.</param>
    /// <param name="info">Optional additional information from the response.</param>
    public OmnisoftFiscalException(int code, string? message, string? info = null)
        : base(BuildMessage(code, message, info))
    {
        Code = code;
        Info = info;
    }

    /// <summary>Creates an exception with an explicit message and no protocol code.</summary>
    /// <param name="message">The error message.</param>
    public OmnisoftFiscalException(string message)
        : base(message)
    {
    }

    /// <summary>The protocol response/error code (0 = success); -1 when not derived from a coded response.</summary>
    public int Code { get; } = -1;

    /// <summary>The optional <c>info</c> field from the failing response.</summary>
    public string? Info { get; }

    private static string BuildMessage(int code, string? message, string? info)
    {
        string text = string.IsNullOrWhiteSpace(message) ? "Omnisoft fiscal operation failed" : message;
        string suffix = string.IsNullOrWhiteSpace(info) ? string.Empty : $" ({info})";
        return $"{text} [code {code}]{suffix}";
    }
}
