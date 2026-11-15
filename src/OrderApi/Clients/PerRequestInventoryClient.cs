using Microsoft.Extensions.Options;
using OrderApi.Configuration;

namespace OrderApi.Clients;

/// <summary>
/// Educational anti-pattern for a long-running service: a new HttpClient and a new handler for every outgoing call.
/// Creating one HttpClient for a short-lived process, or one long-lived HttpClient for a process, is a different situation.
/// </summary>
public sealed class PerRequestInventoryClient
{
    public const string StrategyName = "per-request";

    private readonly Uri _baseAddress;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _pooledConnectionLifetime;
    private readonly Func<HttpMessageHandler> _createPrimaryHandler;

    public PerRequestInventoryClient(IOptions<InventoryOptions> options)
        : this(
            BaseAddressRules.Normalize(options.Value.BaseAddress),
            TimeSpan.FromSeconds(options.Value.TimeoutSeconds),
            TimeSpan.FromSeconds(options.Value.PooledConnectionLifetimeSeconds))
    {
    }

    public PerRequestInventoryClient(
        Uri baseAddress,
        TimeSpan timeout,
        TimeSpan pooledConnectionLifetime,
        Func<HttpMessageHandler>? primaryHandlerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _baseAddress = baseAddress;
        _timeout = timeout;
        _pooledConnectionLifetime = pooledConnectionLifetime;
        // PooledConnectionLifetime is set on the handler so the configuration is visible.
        // It cannot preserve connections: the handler is disposed at the end of the call, and the pool goes with it.
        _createPrimaryHandler = primaryHandlerFactory ?? (() => HandlerDefaults.Create(_pooledConnectionLifetime));
    }

    public Task<InventoryCallResult> GetStockAsync(string sku, string correlationId, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, InventoryRoutes.Stock(sku), correlationId, content: null, cancellationToken);

    public Task<InventoryCallResult> ReserveAsync(
        string sku,
        int quantity,
        string correlationId,
        CancellationToken cancellationToken) =>
        SendWithClientAsync(client => InventoryHttp.ReserveAsync(client, sku, quantity, correlationId, cancellationToken));

    private Task<InventoryCallResult> SendAsync(
        HttpMethod method,
        string relativeUri,
        string correlationId,
        HttpContent? content,
        CancellationToken cancellationToken) =>
        SendWithClientAsync(client => InventoryHttp.SendAsync(client, method, relativeUri, correlationId, content, cancellationToken));

    private async Task<InventoryCallResult> SendWithClientAsync(Func<HttpClient, Task<InventoryCallResult>> send)
    {
        var pipeline = new InstanceIdHandler
        {
            InnerHandler = _createPrimaryHandler()
        };

        using var client = new HttpClient(pipeline, disposeHandler: true)
        {
            BaseAddress = _baseAddress,
            Timeout = _timeout,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OrderApi/1.0 (per-request)");

        return await send(client).ConfigureAwait(false);
    }
}
