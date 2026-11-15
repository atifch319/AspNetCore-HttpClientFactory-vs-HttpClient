using Microsoft.Extensions.Options;
using OrderApi.Configuration;

namespace OrderApi.Clients;

/// <summary>
/// Valid production alternative: one HttpClient and one SocketsHttpHandler for the application lifetime.
/// Register this type as a singleton. The container disposes it during shutdown.
/// PooledConnectionLifetime recycles connections that are eligible for reuse. It does not cancel an active request.
/// DNS is consulted when a new connection is established. This client does not watch DNS TTL.
/// </summary>
public sealed class LongLivedInventoryClient : IDisposable
{
    public const string StrategyName = "long-lived";

    private readonly HttpClient _http;
    private int _disposed;

    public LongLivedInventoryClient(IOptions<InventoryOptions> options)
        : this(
            BaseAddressRules.Normalize(options.Value.BaseAddress),
            TimeSpan.FromSeconds(options.Value.TimeoutSeconds),
            TimeSpan.FromSeconds(options.Value.PooledConnectionLifetimeSeconds))
    {
    }

    public LongLivedInventoryClient(
        Uri baseAddress,
        TimeSpan timeout,
        TimeSpan pooledConnectionLifetime,
        HttpMessageHandler? primaryHandler = null)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var pipeline = new InstanceIdHandler
        {
            InnerHandler = primaryHandler ?? HandlerDefaults.Create(pooledConnectionLifetime)
        };

        _http = new HttpClient(pipeline, disposeHandler: true)
        {
            BaseAddress = baseAddress,
            Timeout = timeout,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OrderApi/1.0 (long-lived)");
    }

    public Task<InventoryCallResult> GetStockAsync(string sku, string correlationId, CancellationToken cancellationToken) =>
        InventoryHttp.GetStockAsync(_http, sku, correlationId, cancellationToken);

    public Task<InventoryCallResult> ReserveAsync(
        string sku,
        int quantity,
        string correlationId,
        CancellationToken cancellationToken) =>
        InventoryHttp.ReserveAsync(_http, sku, quantity, correlationId, cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _http.Dispose();
        }
    }
}
