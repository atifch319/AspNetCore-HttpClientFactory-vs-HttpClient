using Microsoft.Extensions.DependencyInjection;
using OrderApi.Clients;

namespace HttpClientLifetimes.Tests;

[Trait("Kind", "FakeHandler")]
public class FactoryLifetimeTests
{
    [Fact]
    public async Task CreateClient_returns_distinct_wrappers_that_share_the_current_handler()
    {
        using var provider = BuildFactory(TimeSpan.FromMinutes(5), out var handlers);
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var first = factory.CreateClient("probe");
        var second = factory.CreateClient("probe");
        using var firstResponse = await first.GetAsync("api/inventory/WIDGET-1");
        using var secondResponse = await second.GetAsync("api/inventory/WIDGET-1");

        Assert.NotSame(first, second);
        Assert.Equal(
            firstResponse.Headers.GetValues("X-Tracking-Handler").Single(),
            secondResponse.Headers.GetValues("X-Tracking-Handler").Single());
        Assert.Single(handlers);
        first.Dispose();
        second.Dispose();
        Assert.False(handlers.Single().Disposed);
    }

    [Fact(Timeout = 20_000)]
    public async Task HandlerLifetime_does_not_replace_a_handler_still_held_by_an_existing_client()
    {
        using var provider = BuildFactory(TimeSpan.FromSeconds(1), out var handlers);
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var held = factory.CreateClient("probe");
        using (var first = await held.GetAsync("api/inventory/WIDGET-1"))
        {
            var original = first.Headers.GetValues("X-Tracking-Handler").Single();

            string? rotated = null;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && rotated is null)
            {
                var next = factory.CreateClient("probe");
                using var response = await next.GetAsync("api/inventory/WIDGET-1");
                var handlerId = response.Headers.GetValues("X-Tracking-Handler").Single();
                next.Dispose();
                if (!string.Equals(handlerId, original, StringComparison.Ordinal))
                {
                    rotated = handlerId;
                    break;
                }

                await Task.Delay(50);
            }

            using var stillHeld = await held.GetAsync("api/inventory/WIDGET-1");
            Assert.NotNull(rotated);
            Assert.Equal(original, stillHeld.Headers.GetValues("X-Tracking-Handler").Single());
            Assert.NotEqual(original, rotated);
        }

        held.Dispose();
        Assert.True(handlers.Count >= 2);
    }

    [Fact(Timeout = 20_000)]
    public async Task Singleton_that_captures_a_typed_client_keeps_its_handler_after_rotation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient<FactoryInventoryClient>("captured", client =>
        {
            client.BaseAddress = new Uri("http://127.0.0.1/");
            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .AddHttpMessageHandler(() => new InstanceIdHandler())
        .ConfigurePrimaryHttpMessageHandler(() => new Support.TrackingHandler())
        .SetHandlerLifetime(TimeSpan.FromSeconds(1));
        services.AddSingleton<CapturingSingleton>();

        using var provider = services.BuildServiceProvider();
        var singleton = provider.GetRequiredService<CapturingSingleton>();
        var before = await singleton.Client.GetStockAsync("WIDGET-1", "before", CancellationToken.None);

        string? rotated = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && rotated is null)
        {
            var fresh = provider.GetRequiredService<FactoryInventoryClient>();
            var result = await fresh.GetStockAsync("WIDGET-1", "fresh", CancellationToken.None);
            if (!string.Equals(result.HandlerInstanceId, before.HandlerInstanceId, StringComparison.Ordinal))
            {
                rotated = result.HandlerInstanceId;
                break;
            }

            await Task.Delay(50);
        }

        var after = await singleton.Client.GetStockAsync("WIDGET-1", "after", CancellationToken.None);
        Assert.NotNull(rotated);
        Assert.Equal(before.HandlerInstanceId, after.HandlerInstanceId);
        Assert.NotEqual(before.HandlerInstanceId, rotated);
    }

    private static ServiceProvider BuildFactory(TimeSpan handlerLifetime, out List<Support.TrackingHandler> handlers)
    {
        var created = new List<Support.TrackingHandler>();
        handlers = created;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient("probe", client =>
        {
            client.BaseAddress = new Uri("http://127.0.0.1/");
            client.Timeout = TimeSpan.FromSeconds(5);
        })
        .ConfigurePrimaryHttpMessageHandler(() =>
        {
            var handler = new Support.TrackingHandler();
            lock (created)
            {
                created.Add(handler);
            }

            return handler;
        })
        .SetHandlerLifetime(handlerLifetime);

        return services.BuildServiceProvider();
    }

    private sealed class CapturingSingleton
    {
        public CapturingSingleton(FactoryInventoryClient client) => Client = client;

        public FactoryInventoryClient Client { get; }
    }
}
