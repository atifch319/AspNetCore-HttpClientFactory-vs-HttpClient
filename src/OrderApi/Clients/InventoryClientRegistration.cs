using Microsoft.Extensions.Options;
using OrderApi.Configuration;

namespace OrderApi.Clients;

public static class InventoryClientRegistration
{
    public static IServiceCollection AddInventoryClients(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<InventoryOptions>()
            .Bind(configuration.GetSection(InventoryOptions.SectionName))
            .Validate(options => options.TimeoutSeconds > 0
                && options.HandlerLifetimeSeconds > 0
                && options.PooledConnectionLifetimeSeconds > 0,
                "Inventory timeout, HandlerLifetime, and PooledConnectionLifetime must be positive.")
            .ValidateOnStart();

        services.AddOptions<DnsSimulationOptions>()
            .Bind(configuration.GetSection(DnsSimulationOptions.SectionName))
            .ValidateOnStart();

        var handlerLifetime = TimeSpan.FromSeconds(
            configuration.GetSection(InventoryOptions.SectionName).GetValue(nameof(InventoryOptions.HandlerLifetimeSeconds), 120));
        if (handlerLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Inventory:HandlerLifetimeSeconds must be positive.");
        }

        services.AddHttpClient<FactoryInventoryClient>(FactoryInventoryClient.Name, ConfigureClient)
            .AddHttpMessageHandler(() => new InstanceIdHandler())
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
            .SetHandlerLifetime(handlerLifetime);

        services.AddHttpClient(NamedInventoryGateway.Name, ConfigureClient)
            .AddHttpMessageHandler(() => new InstanceIdHandler())
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler)
            .SetHandlerLifetime(handlerLifetime);

        // The gateway and the two explicitly owned clients are singletons.
        // FactoryInventoryClient stays transient: a singleton that stores a typed client would pin one handler
        // and would keep using it after HandlerLifetime has moved on. See docs/HandlerPooling.md.
        services.AddSingleton<NamedInventoryGateway>();
        services.AddSingleton<PerRequestInventoryClient>();
        services.AddSingleton<LongLivedInventoryClient>();

        var dns = configuration.GetSection(DnsSimulationOptions.SectionName).Get<DnsSimulationOptions>() ?? new DnsSimulationOptions();
        if (dns.Enabled)
        {
            var initial = EndpointDirectory.RequireIpEndpoint(BaseAddressRules.Normalize(dns.InitialBaseAddress));
            services.AddSingleton(new EndpointDirectory(initial));
            services.AddSingleton<DnsSimulationClient>();
        }

        return services;
    }

    private static void ConfigureClient(IServiceProvider serviceProvider, HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptions<InventoryOptions>>().Value;
        client.BaseAddress = BaseAddressRules.Normalize(options.BaseAddress);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        client.DefaultRequestVersion = HttpVersion.Version11;
        client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OrderApi/1.0");
    }

    private static HttpMessageHandler CreatePrimaryHandler(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<InventoryOptions>>().Value;
        return HandlerDefaults.Create(TimeSpan.FromSeconds(options.PooledConnectionLifetimeSeconds));
    }
}
