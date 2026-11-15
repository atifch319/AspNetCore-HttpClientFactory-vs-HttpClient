using Microsoft.Extensions.Options;
using OrderApi.Configuration;

namespace OrderApi.Clients;

/// <summary>
/// Long-lived client whose ConnectCallback reads <see cref="EndpointDirectory"/> when a connection is opened.
/// Connection reuse keeps the already connected peer. PooledConnectionLifetime makes a later connection eligible to be replaced.
/// The logical request host is not resolved through operating-system DNS.
/// </summary>
public sealed class DnsSimulationClient : IDisposable
{
    public const string StrategyName = "dns-simulation";

    private readonly HttpClient _http;
    private readonly EndpointDirectory _directory;
    private int _disposed;

    public DnsSimulationClient(IOptions<DnsSimulationOptions> options, EndpointDirectory directory)
        : this(
            new Uri($"http://{options.Value.LogicalHost}/"),
            TimeSpan.FromSeconds(options.Value.PooledConnectionLifetimeSeconds),
            TimeSpan.FromSeconds(options.Value.TimeoutSeconds),
            directory)
    {
    }

    public DnsSimulationClient(Uri logicalBaseAddress, TimeSpan pooledConnectionLifetime, TimeSpan timeout, EndpointDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(logicalBaseAddress);
        ArgumentNullException.ThrowIfNull(directory);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _directory = directory;
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = pooledConnectionLifetime,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(5),
            UseCookies = false,
            ConnectCallback = (context, cancellationToken) => DirectoryConnector.ConnectAsync(directory, context, cancellationToken)
        };

        var pipeline = new InstanceIdHandler { InnerHandler = handler };
        _http = new HttpClient(pipeline, disposeHandler: true)
        {
            BaseAddress = logicalBaseAddress,
            Timeout = timeout,
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OrderApi/1.0 (dns-simulation)");
    }

    public Uri CurrentEndpoint => _directory.Current;

    public Task<InventoryCallResult> GetStockAsync(string sku, string correlationId, CancellationToken cancellationToken) =>
        InventoryHttp.GetStockAsync(_http, sku, correlationId, cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _http.Dispose();
        }
    }
}
