namespace OrderApi.Clients;

public static class HandlerDefaults
{
    public static SocketsHttpHandler Create(TimeSpan pooledConnectionLifetime)
    {
        if (pooledConnectionLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pooledConnectionLifetime),
                pooledConnectionLifetime,
                "PooledConnectionLifetime must be positive.");
        }

        return new SocketsHttpHandler
        {
            PooledConnectionLifetime = pooledConnectionLifetime,
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(5),
            // Cookie handling is demonstrated separately. The comparison clients
            // do not share a cookie jar across requests.
            UseCookies = false
        };
    }
}
