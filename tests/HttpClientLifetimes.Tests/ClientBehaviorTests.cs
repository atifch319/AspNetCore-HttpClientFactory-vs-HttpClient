using System.Net;
using HttpClientLifetimes.Tests.Support;
using OrderApi.Clients;

namespace HttpClientLifetimes.Tests;

[Trait("Kind", "FakeHandler")]
public class ClientBehaviorTests
{
    private static readonly Uri BaseAddress = new("http://127.0.0.1:5080/");

    [Fact]
    public async Task Per_request_client_creates_and_disposes_a_handler_for_each_call()
    {
        var created = new List<TrackingHandler>();
        var client = new PerRequestInventoryClient(BaseAddress, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(2), () =>
        {
            var handler = new TrackingHandler();
            created.Add(handler);
            return handler;
        });

        await client.GetStockAsync("WIDGET-1", "c1", CancellationToken.None);
        await client.GetStockAsync("WIDGET-1", "c2", CancellationToken.None);

        Assert.Equal(2, created.Count);
        Assert.NotEqual(created[0].Id, created[1].Id);
        Assert.All(created, handler => Assert.True(handler.Disposed));
    }

    [Fact]
    public async Task Per_request_client_disposes_the_handler_when_the_status_code_is_an_error()
    {
        TrackingHandler? created = null;
        var client = new PerRequestInventoryClient(BaseAddress, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(2), () =>
        {
            created = new TrackingHandler { StatusCode = HttpStatusCode.InternalServerError, Body = """{"title":"down"}""" };
            return created;
        });

        var error = await Assert.ThrowsAsync<InventoryCallException>(() =>
            client.GetStockAsync("WIDGET-1", "c1", CancellationToken.None));

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.Contains("down", error.Body, StringComparison.Ordinal);
        Assert.True(created!.Disposed);
    }

    [Fact]
    public async Task Relative_url_is_appended_to_the_base_address()
    {
        var handler = new TrackingHandler();
        var client = new PerRequestInventoryClient(BaseAddress, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(2), () => handler);

        var result = await client.GetStockAsync("WIDGET 1", "corr", CancellationToken.None);

        Assert.Equal("http://127.0.0.1:5080/api/inventory/WIDGET%201", result.RequestUri.AbsoluteUri);
    }

    [Fact]
    public void Leading_slash_would_replace_a_base_path()
    {
        var appended = new Uri(new Uri("http://127.0.0.1:5080/api/"), "inventory/WIDGET-1");
        var replaced = new Uri(new Uri("http://127.0.0.1:5080/api/"), "/inventory/WIDGET-1");

        Assert.Equal("http://127.0.0.1:5080/api/inventory/WIDGET-1", appended.AbsoluteUri);
        Assert.Equal("http://127.0.0.1:5080/inventory/WIDGET-1", replaced.AbsoluteUri);
    }

    [Fact]
    public async Task Http_404_and_500_are_returned_as_inventory_failures()
    {
        var client = new FactoryInventoryClient(CreateClient(new TrackingHandler
        {
            StatusCode = HttpStatusCode.NotFound,
            Body = """{"title":"missing"}"""
        }));

        var missing = await Assert.ThrowsAsync<InventoryCallException>(() =>
            client.GetStockAsync("MISSING", "c", CancellationToken.None));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var failing = new FactoryInventoryClient(CreateClient(new TrackingHandler
        {
            StatusCode = HttpStatusCode.BadGateway,
            Body = """{"title":"bad gateway"}"""
        }));
        var failure = await Assert.ThrowsAsync<InventoryCallException>(() =>
            failing.GetStockAsync("WIDGET-1", "c", CancellationToken.None));
        Assert.Equal(HttpStatusCode.BadGateway, failure.StatusCode);
    }

    [Fact]
    public async Task Caller_cancellation_is_distinct_from_the_client_timeout()
    {
        var handler = new TrackingHandler { WaitUntilCanceled = true };
        using var cts = new CancellationTokenSource();
        var client = new FactoryInventoryClient(CreateClient(handler, TimeSpan.FromSeconds(5)));

        var call = client.GetStockAsync("WIDGET-1", "c", cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.True(cts.IsCancellationRequested);
        Assert.Contains(handler.Requests, request => request.CancellationToken.IsCancellationRequested);
    }

    [Fact]
    public async Task Client_timeout_is_reported_when_the_caller_token_is_not_canceled()
    {
        var handler = new TrackingHandler { WaitUntilCanceled = true };
        var client = new FactoryInventoryClient(CreateClient(handler, TimeSpan.FromMilliseconds(200)));

        using var caller = new CancellationTokenSource();
        var error = await Assert.ThrowsAsync<InventoryTimeoutException>(() =>
            client.GetStockAsync("WIDGET-1", "c", caller.Token));

        Assert.False(caller.IsCancellationRequested);
        Assert.Contains("HttpClient.Timeout", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parallel_calls_keep_distinct_correlation_ids_on_one_shared_client()
    {
        var handler = new TrackingHandler();
        using var client = new LongLivedInventoryClient(BaseAddress, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(2), handler);
        var ids = Enumerable.Range(0, 32).Select(index => $"c-{index}").ToArray();

        await Task.WhenAll(ids.Select(id => client.GetStockAsync("WIDGET-1", id, CancellationToken.None)));

        var observed = handler.Requests.Select(request => Assert.Single(request.CorrelationIds)).Order().ToArray();
        Assert.Equal(ids.Order(), observed);
    }

    [Fact]
    public void Long_lived_client_can_be_disposed_twice()
    {
        var client = new LongLivedInventoryClient(BaseAddress, TimeSpan.FromSeconds(2), TimeSpan.FromMinutes(2), new TrackingHandler());

        client.Dispose();
        client.Dispose();
    }

    [Fact]
    public void Endpoint_directory_accepts_only_explicit_ip_endpoints()
    {
        var directory = new EndpointDirectory(new Uri("http://127.0.0.1:5080/"));
        directory.Set(new Uri("http://127.0.0.1:5081/"));

        Assert.Equal("http://127.0.0.1:5081/", directory.Current.AbsoluteUri);
        Assert.Throws<ArgumentException>(() => directory.Set(new Uri("http://inventory.local:5080/")));
        Assert.Throws<ArgumentException>(() => new EndpointDirectory(new Uri("http://127.0.0.1/")));
    }

    private static HttpClient CreateClient(HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        var pipeline = new InstanceIdHandler { InnerHandler = handler };
        return new HttpClient(pipeline, disposeHandler: true)
        {
            BaseAddress = BaseAddress,
            Timeout = timeout ?? TimeSpan.FromSeconds(5)
        };
    }
}
