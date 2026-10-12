using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Yarp.ReverseProxy.Forwarder;

namespace FinSightIQ.Gateway.Tests.Helpers;

public sealed class RecordedRequest
{
    public required string Method { get; init; }
    public required Uri RequestUri { get; init; }
    public required Dictionary<string, string[]> Headers { get; init; }
    public required string? Body { get; init; }
}

public sealed class TestDownstreamHandler : HttpMessageHandler
{
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

    public bool SimulateFailure { get; set; }

    public bool SimulateTimeout { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content != null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        var recorded = new RecordedRequest
        {
            Method = request.Method.Method,
            RequestUri = request.RequestUri ?? new Uri("http://localhost"),
            Headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToArray()),
            Body = body
        };

        Requests.Enqueue(recorded);

        if (SimulateTimeout)
        {
            throw new OperationCanceledException("Simulated downstream timeout.");
        }

        if (SimulateFailure)
        {
            throw new HttpRequestException("Simulated downstream connection failure.");
        }

        if (ResponseFactory != null)
        {
            return ResponseFactory(request);
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"status\":\"success\"}", Encoding.UTF8, "application/json")
        };
    }
}

public sealed class TestForwarderHttpClientFactory : IForwarderHttpClientFactory
{
    private readonly TestDownstreamHandler _handler;

    public TestForwarderHttpClientFactory(TestDownstreamHandler handler)
    {
        _handler = handler;
    }

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context)
    {
        return new HttpMessageInvoker(_handler);
    }
}
