using Microsoft.Extensions.DependencyInjection;
using HttpClientLifetimes.Tests.Support;
using OrderApi.Clients;

namespace HttpClientLifetimes.Tests;

[Trait("Kind", "LoopbackTcp")]
public class LoopbackPoolingTests
{
    [Fact]
    public async Task Per_request_strategy_opens_a_new_tcp_connection_for_each_call()
    {
        await using var server = await LoopbackHttpServer.StartAsync("inventory-a");
        var client = new PerRequestInventoryClient(BaseUri(server), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2));

        for (var i = 0; i < 4; i++)
        {
            var result = await client.GetStockAsync("WIDGET-1", $"c{i}", CancellationToken.None);
            Assert.Equal("inventory-a", result.InventoryInstanceId);
        }

        Assert.Equal(4, server.AcceptedConnections);
        Assert.Equal(4, server.RequestCount);
    }

    [Fact]
    public async Task Long_lived_client_reuses_one_tcp_connection_for_sequential_requests()
    {
        await using var server = await LoopbackHttpServer.StartAsync("inventory-a");
        using var client = new LongLivedInventoryClient(BaseUri(server), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2));

        for (var i = 0; i < 6; i++)
        {
            await client.GetStockAsync("WIDGET-1", $"c{i}", CancellationToken.None);
        }

        Assert.Equal(1, server.AcceptedConnections);
        Assert.Equal(6, server.RequestCount);
        Assert.Equal(6, server.RequestsPerConnection[1]);
    }

    [Fact]
    public async Task Factory_client_reuses_one_tcp_connection_across_distinct_wrappers()
    {
        await using var server = await LoopbackHttpServer.StartAsync("inventory-a");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient(NamedInventoryGateway.Name, client =>
        {
            client.BaseAddress = BaseUri(server);
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestVersion = HttpVersion.Version11;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            UseCookies = false
        })
        .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        HttpClient? first = null;
        HttpClient? second = null;
        try
        {
            for (var i = 0; i < 5; i++)
            {
                var client = factory.CreateClient(NamedInventoryGateway.Name);
                first ??= client;
                second ??= i == 1 ? client : null;
                using var response = await client.GetAsync("api/inventory/WIDGET-1");
                response.EnsureSuccessStatusCode();
                if (!ReferenceEquals(client, first))
                {
                    client.Dispose();
                }
            }
        }
        finally
        {
            first?.Dispose();
        }

        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(1, server.AcceptedConnections);
        Assert.Equal(5, server.RequestCount);
    }

    [Fact]
    public async Task Long_lived_pool_absorbs_later_sequential_calls_after_a_concurrent_burst()
    {
        await using var server = await LoopbackHttpServer.StartAsync("inventory-a");
        using var client = new LongLivedInventoryClient(BaseUri(server), TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2));

        for (var i = 0; i < 3; i++)
        {
            await client.GetStockAsync("WIDGET-1", $"warm-{i}", CancellationToken.None);
        }

        Assert.Equal(1, server.AcceptedConnections);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            client.GetStockAsync("WIDGET-1", $"burst-{i}", CancellationToken.None)));

        var afterBurst = server.AcceptedConnections;
        Assert.InRange(afterBurst, 1, 5);

        for (var i = 0; i < 6; i++)
        {
            await client.GetStockAsync("WIDGET-1", $"again-{i}", CancellationToken.None);
        }

        // A replacement connection can be opened if one pooled socket was closed.
        // Six further requests must not open six new connections.
        var extraConnections = server.AcceptedConnections - afterBurst;
        Assert.InRange(extraConnections, 0, 2);
        Assert.Equal(3 + 4 + 6, server.RequestCount);
    }

    private static Uri BaseUri(LoopbackHttpServer server) => new($"http://127.0.0.1:{server.Port}/");
}
