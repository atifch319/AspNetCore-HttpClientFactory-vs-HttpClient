using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using HttpClientLifetimes.Tests.Support;
using OrderApi.Clients;

namespace HttpClientLifetimes.Tests;

[Trait("Kind", "LoopbackTcp")]
public class DnsAndCookieTests
{
    [Fact(Timeout = 30_000)]
    public async Task Changed_endpoint_is_used_only_after_a_new_connection_is_established()
    {
        await using var serverA = await LoopbackHttpServer.StartAsync("A");
        await using var serverB = await LoopbackHttpServer.StartAsync("B");
        var directory = new EndpointDirectory(new Uri($"http://127.0.0.1:{serverA.Port}/"));
        using var client = new DnsSimulationClient(
            new Uri("http://inventory.local/"),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            directory);

        var first = await client.GetStockAsync("WIDGET-1", "c1", CancellationToken.None);
        Assert.Equal("A", first.InventoryInstanceId);
        Assert.Equal(1, serverA.AcceptedConnections);
        Assert.Equal(0, serverB.AcceptedConnections);

        directory.Set(new Uri($"http://127.0.0.1:{serverB.Port}/"));
        var second = await client.GetStockAsync("WIDGET-1", "c2", CancellationToken.None);
        Assert.Equal("A", second.InventoryInstanceId);
        Assert.Equal(1, serverA.AcceptedConnections);
        Assert.Equal(2, serverA.RequestCount);
        Assert.Equal(0, serverB.AcceptedConnections);

        InventoryCallResult? moved = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && moved is null)
        {
            var probe = await client.GetStockAsync("WIDGET-1", "c3", CancellationToken.None);
            if (probe.InventoryInstanceId == "B")
            {
                moved = probe;
                break;
            }

            await Task.Delay(100);
        }

        Assert.NotNull(moved);
        Assert.Equal(1, serverB.AcceptedConnections);
        Assert.DoesNotContain(serverA.Errors, _ => true);
        Assert.DoesNotContain(serverB.Errors, _ => true);
    }

    [Fact(Timeout = 20_000)]
    public async Task Pooled_connection_lifetime_does_not_abort_an_active_request()
    {
        await using var server = await LoopbackHttpServer.StartAsync((request, _) =>
        {
            var hold = request.Path.Contains("HOLD", StringComparison.OrdinalIgnoreCase);
            return Task.FromResult(LoopbackHttpServer.Json("A", request.ConnectionId, hold: hold));
        });
        var directory = new EndpointDirectory(new Uri($"http://127.0.0.1:{server.Port}/"));
        using var client = new DnsSimulationClient(
            new Uri("http://inventory.local/"),
            TimeSpan.FromMilliseconds(400),
            TimeSpan.FromSeconds(10),
            directory);

        await client.GetStockAsync("WIDGET-1", "warm", CancellationToken.None);
        var held = client.GetStockAsync("HOLD", "held", CancellationToken.None);
        await server.WaitForHoldAsync(CancellationToken.None);

        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromMilliseconds(900))
        {
            Assert.False(held.IsCompleted);
            await Task.Delay(50);
        }

        Assert.Equal(1, server.ReleaseHolds());
        var completed = await held;
        Assert.Equal("A", completed.InventoryInstanceId);
        Assert.Equal(200, completed.StatusCode);
    }

    [Fact(Timeout = 20_000)]
    public async Task Handler_rotation_starts_a_new_cookie_container_without_clearing_a_held_client()
    {
        await using var server = await LoopbackHttpServer.StartAsync((request, _) =>
        {
            var cookie = request.Headers.TryGetValue("Cookie", out var value) ? value : string.Empty;
            var plan = new LoopbackPlan(
                200,
                $$"""{"instanceId":"cookie","echo":"{{cookie}}"}""",
                Headers: [("Set-Cookie", "session=abc; Path=/")]);
            return Task.FromResult(plan);
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRotatingCookieClient(new Uri($"http://127.0.0.1:{server.Port}/"), TimeSpan.FromSeconds(1));
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        var held = factory.CreateClient(CookieHandlerExample.ClientName);
        var firstEcho = await ReadEchoAsync(held);
        var second = factory.CreateClient(CookieHandlerExample.ClientName);
        var secondEcho = await ReadEchoAsync(second);
        second.Dispose();

        Assert.Equal(string.Empty, firstEcho);
        Assert.Contains("session=abc", secondEcho, StringComparison.Ordinal);

        string? rotatedEcho = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && rotatedEcho is null)
        {
            var candidate = factory.CreateClient(CookieHandlerExample.ClientName);
            var echo = await ReadEchoAsync(candidate);
            candidate.Dispose();
            if (!echo.Contains("session=abc", StringComparison.Ordinal))
            {
                rotatedEcho = echo;
                break;
            }

            await Task.Delay(50);
        }

        var heldAgain = await ReadEchoAsync(held);
        held.Dispose();

        Assert.NotNull(rotatedEcho);
        Assert.DoesNotContain("session=abc", rotatedEcho, StringComparison.Ordinal);
        Assert.Contains("session=abc", heldAgain, StringComparison.Ordinal);
    }

    private static async Task<string> ReadEchoAsync(HttpClient client)
    {
        using var response = await client.GetAsync("api/inventory/WIDGET-1");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        using var document = System.Text.Json.JsonDocument.Parse(body);
        return document.RootElement.GetProperty("echo").GetString() ?? string.Empty;
    }
}
