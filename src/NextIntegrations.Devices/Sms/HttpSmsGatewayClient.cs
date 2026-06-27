using System.Text;
using System.Text.Json;

namespace NextIntegrations.Devices.Sms;

/// <summary>
/// Generic HTTP client for an SMS gateway. It builds the request from <see cref="SmsGatewayOptions"/>
/// (GET query, POST form, or POST JSON), sends it, and reports whether the gateway accepted the message.
/// Transport failures (network down, timeout) are returned as a rejected <see cref="SmsDispatchResult"/>
/// rather than thrown, so a bulk campaign keeps going. The app supplies the endpoint + credentials via the
/// options; this library owns the connection and returns its result.
/// </summary>
public sealed class HttpSmsGatewayClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly SmsGatewayOptions _options;

    /// <summary>Creates the client over an injected <see cref="HttpClient"/> and the gateway options.</summary>
    public HttpSmsGatewayClient(HttpClient httpClient, SmsGatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        _options = options;
    }

    /// <summary>Sends one message and reports whether the gateway accepted it.</summary>
    public async Task<SmsDispatchResult> SendAsync(SmsText message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        try
        {
            using HttpRequestMessage request = BuildRequest(message);
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            int status = (int)response.StatusCode;
            bool statusOk = _options.SuccessStatusCodes.Contains(status);
            bool bodyOk = string.IsNullOrEmpty(_options.SuccessBodyContains)
                || body.Contains(_options.SuccessBodyContains, StringComparison.OrdinalIgnoreCase);

            return statusOk && bodyOk
                ? SmsDispatchResult.Ok(status, body)
                : SmsDispatchResult.Fail(status, body, $"Gateway rejected the message (HTTP {status}).");
        }
        catch (HttpRequestException ex)
        {
            return SmsDispatchResult.Fail(null, null, ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SmsDispatchResult.Fail(null, null, "Gateway request timed out.");
        }
    }

    /// <summary>Sends many messages in sequence; returns how many the gateway accepted.</summary>
    public async Task<int> SendBulkAsync(IReadOnlyList<SmsText> messages, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        int accepted = 0;
        foreach (SmsText message in messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SmsDispatchResult result = await SendAsync(message, cancellationToken).ConfigureAwait(false);
            if (result.Accepted)
            {
                accepted++;
            }
        }

        return accepted;
    }

    private HttpRequestMessage BuildRequest(SmsText message)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal)
        {
            [_options.LoginField] = _options.Login,
            [_options.PasswordField] = _options.Password,
            [_options.SenderField] = _options.Sender,
            [_options.RecipientField] = message.Recipient,
            [_options.TextField] = message.Text,
        };

        if (_options.ExtraParameters is not null)
        {
            foreach (KeyValuePair<string, string> extra in _options.ExtraParameters)
            {
                parameters[extra.Key] = extra.Value;
            }
        }

        return _options.Transport switch
        {
            SmsGatewayTransport.PostForm => new HttpRequestMessage(HttpMethod.Post, _options.BaseAddress)
            {
                Content = new FormUrlEncodedContent(parameters),
            },
            SmsGatewayTransport.PostJson => new HttpRequestMessage(HttpMethod.Post, _options.BaseAddress)
            {
                Content = new StringContent(JsonSerializer.Serialize(parameters, JsonOptions), Encoding.UTF8, "application/json"),
            },
            _ => new HttpRequestMessage(HttpMethod.Get, BuildQueryUri(parameters)),
        };
    }

    private Uri BuildQueryUri(IReadOnlyDictionary<string, string> parameters)
    {
        StringBuilder query = new();
        foreach (KeyValuePair<string, string> parameter in parameters)
        {
            query.Append(query.Length == 0 ? '?' : '&');
            query.Append(Uri.EscapeDataString(parameter.Key));
            query.Append('=');
            query.Append(Uri.EscapeDataString(parameter.Value));
        }

        return new Uri(_options.BaseAddress, query.ToString());
    }
}
