using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HttpClientLifetimes.Tests.Support;
using InventoryApi;
using LoadGenerator;
using Microsoft.AspNetCore.Mvc.Testing;

namespace HttpClientLifetimes.Tests;

public class ApiTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    [Trait("Kind", "FakeHandler")]
    public async Task OpenApi_and_Swagger_ui_are_available()
    {
        await using var factory = new WebApplicationFactory<OrderApi.Program>();
        var client = factory.CreateClient();

        var openApi = await client.GetAsync("/openapi/v1.json");
        var swagger = await client.GetAsync("/swagger/index.html");
        var health = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);
        Assert.Equal(HttpStatusCode.OK, swagger.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        var document = await openApi.Content.ReadAsStringAsync();
        Assert.Contains("Order API", document, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Kind", "LoopbackTcp")]
    public async Task Inventory_api_reads_and_reserves_stock()
    {
        await using var host = await RunningInventoryApi.StartAsync("inventory-a");
        var client = host.Client;

        var stock = await client.GetFromJsonAsync<JsonElement>("/api/inventory/WIDGET-1", JsonOptions);
        Assert.Equal(10_000, stock.GetProperty("quantityOnHand").GetInt32());
        Assert.Equal("inventory-a", stock.GetProperty("instanceId").GetString());

        var missing = await client.GetAsync("/api/inventory/NO-SUCH-SKU");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var reserved = await client.PostAsJsonAsync("/api/reservations", new { sku = "GADGET-2", quantity = 2 }, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, reserved.StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/inventory/GADGET-2", JsonOptions);
        Assert.Equal(23, after.GetProperty("quantityOnHand").GetInt32());

        var conflict = await client.PostAsJsonAsync("/api/reservations", new { sku = "GADGET-2", quantity = 100 }, JsonOptions);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var unchanged = await client.GetFromJsonAsync<JsonElement>("/api/inventory/GADGET-2", JsonOptions);
        Assert.Equal(23, unchanged.GetProperty("quantityOnHand").GetInt32());
    }

    [Fact]
    [Trait("Kind", "LoopbackTcp")]
    public async Task Three_comparison_endpoints_and_named_wrappers_call_the_same_inventory()
    {
        await using var inventory = await LoopbackHttpServer.StartAsync("loopback");
        await using var api = await RunningOrderApi.StartAsync(inventory.Port);

        var perRequest = new List<DemoPayload>();
        var factory = new List<DemoPayload>();
        var longLived = new List<DemoPayload>();
        foreach (var strategy in new[] { "per-request", "factory", "long-lived" })
        {
            for (var i = 0; i < 2; i++)
            {
                var payload = await GetStockAsync(api.Client, strategy);
                Assert.True(payload.Success);
                Assert.Equal("loopback", payload.InventoryInstanceId);
                Assert.False(string.IsNullOrWhiteSpace(payload.HandlerInstanceId));
                (strategy switch
                {
                    "per-request" => perRequest,
                    "factory" => factory,
                    _ => longLived
                }).Add(payload);
            }
        }

        Assert.NotEqual(perRequest[0].HandlerInstanceId, perRequest[1].HandlerInstanceId);
        Assert.Equal(factory[0].HandlerInstanceId, factory[1].HandlerInstanceId);
        Assert.Equal(longLived[0].HandlerInstanceId, longLived[1].HandlerInstanceId);
        Assert.Equal(2 + 1 + 1, inventory.AcceptedConnections);

        var wrappers = await api.Client.GetFromJsonAsync<WrapperPayload>("/api/demo/named/wrappers/WIDGET-1", JsonOptions);
        Assert.NotNull(wrappers);
        Assert.False(wrappers.WrappersAreSameInstance);
        Assert.Equal(wrappers.First.HandlerInstanceId, wrappers.Second.HandlerInstanceId);
    }

    [Fact]
    [Trait("Kind", "LoopbackTcp")]
    public async Task Order_endpoint_reserves_through_the_factory_client_and_reports_missing_stock()
    {
        await using var inventory = await LoopbackHttpServer.StartAsync((request, _) =>
        {
            if (request.Method == "POST" && request.Path == "/api/reservations")
            {
                using var document = JsonDocument.Parse(request.Body);
                var sku = document.RootElement.GetProperty("sku").GetString();
                if (string.Equals(sku, "MISSING", StringComparison.Ordinal))
                {
                    return Task.FromResult(new LoopbackPlan(404, """{"title":"SKU was not found"}"""));
                }

                return Task.FromResult(new LoopbackPlan(200, """{"reservationId":"r1","sku":"WIDGET-1","quantity":1,"remaining":9999,"instanceId":"loopback"}"""));
            }

            return Task.FromResult(LoopbackHttpServer.Json("loopback", request.ConnectionId));
        });
        await using var api = await RunningOrderApi.StartAsync(inventory.Port);

        var created = await api.Client.PostAsJsonAsync("/api/orders", new { sku = "WIDGET-1", quantity = 1 }, JsonOptions);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var order = await created.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        var id = order.GetProperty("id").GetString();
        var fetched = await api.Client.GetAsync($"/api/orders/{id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);

        var missing = await api.Client.PostAsJsonAsync("/api/orders", new { sku = "MISSING", quantity = 1 }, JsonOptions);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var invalid = await api.Client.PostAsJsonAsync("/api/orders", new { sku = "", quantity = 0 }, JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    [Trait("Kind", "LoopbackTcp")]
    public async Task Dns_demo_endpoint_follows_the_directory_on_the_next_connection()
    {
        await using var serverA = await LoopbackHttpServer.StartAsync("A");
        await using var serverB = await LoopbackHttpServer.StartAsync("B");
        await using var api = await RunningOrderApi.StartAsync(
            serverA.Port,
            dns: true,
            dnsLifetimeSeconds: 1,
            dnsInitial: $"http://127.0.0.1:{serverA.Port}/");

        var first = await GetDnsAsync(api.Client);
        Assert.Equal("A", first.InventoryInstanceId);

        var update = await api.Client.PutAsJsonAsync(
            "/api/demo/dns/endpoint",
            new { baseAddress = $"http://127.0.0.1:{serverB.Port}/" },
            JsonOptions);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var second = await GetDnsAsync(api.Client);
        Assert.Equal("A", second.InventoryInstanceId);

        DemoPayload? moved = null;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && moved is null)
        {
            var probe = await GetDnsAsync(api.Client);
            if (probe.InventoryInstanceId == "B")
            {
                moved = probe;
                break;
            }

            await Task.Delay(100);
        }

        Assert.NotNull(moved);
        var rejected = await api.Client.PutAsJsonAsync(
            "/api/demo/dns/endpoint",
            new { baseAddress = "http://inventory.example:5080/" },
            JsonOptions);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
    }

    [Fact]
    public void Inventory_store_does_not_oversell_under_contention()
    {
        var store = new InventoryStore();
        var reserved = 0;
        Parallel.For(0, 40, _ =>
        {
            if (store.TryReserve("GADGET-2", 1).Status == ReserveStatus.Reserved)
            {
                Interlocked.Increment(ref reserved);
            }
        });

        Assert.Equal(25, reserved);
        Assert.True(store.TryGet("GADGET-2", out var remaining));
        Assert.Equal(0, remaining);
    }

    [Fact]
    [Trait("Kind", "LoopbackTcp")]
    public async Task Load_generator_collects_samples_for_all_three_strategies()
    {
        await using var inventory = await LoopbackHttpServer.StartAsync("loopback");
        await using var api = await RunningOrderApi.StartAsync(inventory.Port);
        var options = LoadOptions.Parse(
        [
            "--base-url", api.BaseUri.AbsoluteUri,
            "--concurrency", "2",
            "--duration", "1",
            "--warmup", "0"
        ]);

        var reports = await LoadRunner.RunAsync(options, CancellationToken.None);

        Assert.Equal(["per-request", "factory", "long-lived"], reports.Select(report => report.Strategy).ToArray());
        Assert.All(reports, report =>
        {
            Assert.True(report.Requests > 0);
            Assert.Equal(0, report.Failures);
            Assert.NotNull(report.P50Milliseconds);
        });
    }

    private static async Task<DemoPayload> GetStockAsync(HttpClient client, string strategy)
    {
        var response = await client.GetAsync($"/api/demo/{strategy}/stock/WIDGET-1");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DemoPayload>(JsonOptions))!;
    }

    private static async Task<DemoPayload> GetDnsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/demo/dns/stock/WIDGET-1");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DemoPayload>(JsonOptions))!;
    }

    private sealed record DemoPayload(
        string Strategy,
        string? InventoryInstanceId,
        string? HandlerInstanceId,
        bool Success);

    private sealed record WrapperPayload(bool WrappersAreSameInstance, DemoPayload First, DemoPayload Second);
}
