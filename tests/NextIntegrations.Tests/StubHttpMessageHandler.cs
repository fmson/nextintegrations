using System.Net;

namespace NextIntegrations.Tests;

/// <summary>
/// A scripted <see cref="HttpMessageHandler"/> for testing the Omnisoft client without a real device:
/// it captures every request's URI + body and returns canned JSON responses in order.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Response> _responses = new();

    /// <summary>The captured requests, in the order they were sent.</summary>
    public List<CapturedRequest> Requests { get; } = [];

    /// <summary>Queues a JSON body to return for the next request (HTTP 200).</summary>
    public StubHttpMessageHandler EnqueueJson(string json)
    {
        _responses.Enqueue(new Response(HttpStatusCode.OK, json));
        return this;
    }

    /// <summary>Queues a response with an explicit status code and body for the next request.</summary>
    public StubHttpMessageHandler EnqueueResponse(HttpStatusCode statusCode, string body)
    {
        _responses.Enqueue(new Response(statusCode, body));
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

        Response response = _responses.Count > 0 ? _responses.Dequeue() : new Response(HttpStatusCode.OK, "{\"code\":0}");
        return new HttpResponseMessage(response.StatusCode)
        {
            Content = new StringContent(response.Body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private sealed record Response(HttpStatusCode StatusCode, string Body);
}

/// <summary>A single captured outbound request.</summary>
internal sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string Body);
