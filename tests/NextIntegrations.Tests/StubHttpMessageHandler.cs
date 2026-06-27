using System.Net;

namespace NextIntegrations.Tests;

/// <summary>
/// A scripted <see cref="HttpMessageHandler"/> for testing the Omnisoft client without a real device:
/// it captures every request's URI + body and returns canned JSON responses in order.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<string> _responses = new();

    /// <summary>The captured requests, in the order they were sent.</summary>
    public List<CapturedRequest> Requests { get; } = [];

    /// <summary>Queues a JSON body to return for the next request (HTTP 200).</summary>
    public StubHttpMessageHandler EnqueueJson(string json)
    {
        _responses.Enqueue(json);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Requests.Add(new CapturedRequest(request.Method, request.RequestUri, body));

        string responseJson = _responses.Count > 0 ? _responses.Dequeue() : "{\"code\":0}";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
        };
    }
}

/// <summary>A single captured outbound request.</summary>
internal sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string Body);
