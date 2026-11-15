namespace OrderApi.Clients;

/// <summary>
/// Stamps the handler pipeline that was built for this client.
/// IHttpClientFactory caches the pipeline, so the id stays stable until that pipeline is rotated.
/// A per-request handler gets a new id on every call because a new pipeline is built every time.
/// </summary>
public sealed class InstanceIdHandler : DelegatingHandler
{
    public const string HeaderName = "X-Handler-Instance";

    public static readonly HttpRequestOptionsKey<string> HandlerIdOption = new("orderapi.handler-instance");

    public string InstanceId { get; } = Guid.NewGuid().ToString("n");

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Options.Set(HandlerIdOption, InstanceId);
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.Headers.TryAddWithoutValidation(HeaderName, InstanceId);
        return response;
    }
}
