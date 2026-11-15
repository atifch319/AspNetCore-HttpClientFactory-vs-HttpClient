using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace HttpClientLifetimes.Tests.Support;

internal sealed record ObservedRequest(Uri? Uri, IReadOnlyList<string> CorrelationIds, CancellationToken CancellationToken);

internal sealed class TrackingHandler : HttpMessageHandler
{
    private int _disposed;

    public string Id { get; } = Guid.NewGuid().ToString("n");

    public bool Disposed => Volatile.Read(ref _disposed) == 1;

    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

    public string Body { get; set; } = """{"instanceId":"fake"}""";

    public bool WaitUntilCanceled { get; set; }

    public ConcurrentQueue<ObservedRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var correlationIds = request.Headers.TryGetValues("X-Correlation-Id", out var values)
            ? values.ToArray()
            : [];
        Requests.Enqueue(new ObservedRequest(request.RequestUri, correlationIds, cancellationToken));

        if (WaitUntilCanceled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }

        var response = new HttpResponseMessage(StatusCode)
        {
            Content = new StringContent(Body, Encoding.UTF8, "application/json"),
            RequestMessage = request
        };
        response.Headers.TryAddWithoutValidation("X-Tracking-Handler", Id);
        return response;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Interlocked.Exchange(ref _disposed, 1);
        }

        base.Dispose(disposing);
    }
}
