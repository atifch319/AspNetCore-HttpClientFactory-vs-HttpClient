namespace OrderApi.Clients;

/// <summary>
/// Shows cookie scope for a factory-managed handler.
/// The primary-handler lambda creates a new CookieContainer for each handler generation.
/// Clients that share one handler share that container. After rotation, a new handler starts empty.
/// Supply one CookieContainer from outside the lambda when cookies must survive rotation.
/// </summary>
public static class CookieHandlerExample
{
    public const string ClientName = "cookie-rotation";

    public static IHttpClientBuilder AddRotatingCookieClient(
        this IServiceCollection services,
        Uri baseAddress,
        TimeSpan handlerLifetime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (handlerLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handlerLifetime));
        }

        return services.AddHttpClient(ClientName, client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestVersion = HttpVersion.Version11;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            UseCookies = true,
            CookieContainer = new System.Net.CookieContainer(),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        })
        .SetHandlerLifetime(handlerLifetime);
    }
}
