using Microsoft.AspNetCore.Builder;

namespace HttpClientLifetimes.Tests.Support;

internal sealed class RunningOrderApi : IAsyncDisposable
{
    private RunningOrderApi(WebApplication app, Uri baseUri)
    {
        App = app;
        BaseUri = baseUri;
        Client = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(15) };
    }

    public WebApplication App { get; }

    public Uri BaseUri { get; }

    public HttpClient Client { get; }

    public static async Task<RunningOrderApi> StartAsync(
        int inventoryPort,
        bool dns = false,
        int? dnsLifetimeSeconds = null,
        string? dnsInitial = null)
    {
        var args = new List<string>
        {
            $"--Inventory:BaseAddress=http://127.0.0.1:{inventoryPort}/",
            "--urls",
            "http://127.0.0.1:0"
        };

        if (dns)
        {
            args.Add("--DnsSimulation:Enabled=true");
            args.Add($"--DnsSimulation:InitialBaseAddress={dnsInitial}");
            args.Add($"--DnsSimulation:PooledConnectionLifetimeSeconds={dnsLifetimeSeconds}");
            args.Add("--DnsSimulation:TimeoutSeconds=15");
            args.Add("--DnsSimulation:LogicalHost=inventory.local");
        }
        else
        {
            args.Add("--DnsSimulation:Enabled=false");
        }

        var app = OrderApi.Program.Build(args.ToArray());
        await app.StartAsync().ConfigureAwait(false);
        var address = app.Urls.Single();
        var baseUri = address.EndsWith('/') ? new Uri(address) : new Uri(address + "/");
        return new RunningOrderApi(app, baseUri);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync().ConfigureAwait(false);
        await App.DisposeAsync().ConfigureAwait(false);
    }
}

internal sealed class RunningInventoryApi : IAsyncDisposable
{
    private RunningInventoryApi(WebApplication app, HttpClient client)
    {
        App = app;
        Client = client;
    }

    public WebApplication App { get; }

    public HttpClient Client { get; }

    public static async Task<RunningInventoryApi> StartAsync(string instanceId)
    {
        var app = InventoryApi.Program.Build(
        [
            $"--Instance:Id={instanceId}",
            "--urls",
            "http://127.0.0.1:0"
        ]);
        await app.StartAsync().ConfigureAwait(false);
        var address = app.Urls.Single();
        var baseUri = address.EndsWith('/') ? new Uri(address) : new Uri(address + "/");
        return new RunningInventoryApi(app, new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(5) });
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await App.StopAsync().ConfigureAwait(false);
        await App.DisposeAsync().ConfigureAwait(false);
    }
}
