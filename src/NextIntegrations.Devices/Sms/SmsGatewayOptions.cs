namespace NextIntegrations.Devices.Sms;

/// <summary>How the gateway expects the SMS parameters on the wire.</summary>
public enum SmsGatewayTransport
{
    /// <summary>Parameters appended to the URL as a query string on an HTTP GET (common for AZ HTTP gateways).</summary>
    GetQuery,

    /// <summary>Parameters sent as <c>application/x-www-form-urlencoded</c> on an HTTP POST.</summary>
    PostForm,

    /// <summary>Parameters sent as a flat JSON object on an HTTP POST.</summary>
    PostJson,
}

/// <summary>
/// Configuration for a generic HTTP SMS gateway. The consuming app fills these from its own UI/DB —
/// the operator enters the credentials, never the library — and the library uses them to build, send,
/// and judge the request. The field names are configurable so one client works with the various AZ HTTP
/// gateways (the common <c>user</c>/<c>password</c>/<c>from</c>/<c>to</c>/<c>text</c> style).
/// </summary>
public sealed record SmsGatewayOptions
{
    /// <summary>Gateway endpoint, e.g. <c>https://sms.example.az/api/send</c>.</summary>
    public required Uri BaseAddress { get; init; }

    /// <summary>Account login / username for the gateway.</summary>
    public required string Login { get; init; }

    /// <summary>Account password or API key for the gateway.</summary>
    public required string Password { get; init; }

    /// <summary>Registered sender id / originator shown to the recipient (e.g. the brand name).</summary>
    public required string Sender { get; init; }

    /// <summary>How the parameters are transported. Defaults to <see cref="SmsGatewayTransport.GetQuery"/>.</summary>
    public SmsGatewayTransport Transport { get; init; } = SmsGatewayTransport.GetQuery;

    /// <summary>Request parameter name carrying the login. Defaults to <c>user</c>.</summary>
    public string LoginField { get; init; } = "user";

    /// <summary>Request parameter name carrying the password. Defaults to <c>password</c>.</summary>
    public string PasswordField { get; init; } = "password";

    /// <summary>Request parameter name carrying the sender id. Defaults to <c>from</c>.</summary>
    public string SenderField { get; init; } = "from";

    /// <summary>Request parameter name carrying the recipient number. Defaults to <c>to</c>.</summary>
    public string RecipientField { get; init; } = "to";

    /// <summary>Request parameter name carrying the message text. Defaults to <c>text</c>.</summary>
    public string TextField { get; init; } = "text";

    /// <summary>Extra static parameters appended to every request (e.g. <c>format=json</c>). Optional.</summary>
    public IReadOnlyDictionary<string, string>? ExtraParameters { get; init; }

    /// <summary>HTTP status codes treated as "accepted". Defaults to 200 only.</summary>
    public IReadOnlyCollection<int> SuccessStatusCodes { get; init; } = [200];

    /// <summary>When set, the response body must also contain this text (case-insensitive) to count as accepted.</summary>
    public string? SuccessBodyContains { get; init; }

    /// <summary>Per-request timeout. Defaults to 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}
