namespace NextIntegrations.Devices.Sms;

/// <summary>One outbound text message: the destination number and the body.</summary>
public sealed record SmsText(string Recipient, string Text);

/// <summary>The gateway's answer for one send attempt.</summary>
/// <param name="Accepted">True when the gateway accepted the message for delivery.</param>
/// <param name="StatusCode">The HTTP status code, or null when the request never completed (transport error/timeout).</param>
/// <param name="Response">The raw response body, when available.</param>
/// <param name="Error">A human-readable failure reason when <paramref name="Accepted"/> is false; null on success.</param>
public sealed record SmsDispatchResult(bool Accepted, int? StatusCode, string? Response, string? Error)
{
    /// <summary>An accepted result.</summary>
    public static SmsDispatchResult Ok(int statusCode, string? response) => new(true, statusCode, response, null);

    /// <summary>A rejected result.</summary>
    public static SmsDispatchResult Fail(int? statusCode, string? response, string error) =>
        new(false, statusCode, response, error);
}
