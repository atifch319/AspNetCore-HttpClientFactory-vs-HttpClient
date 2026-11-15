namespace OrderApi.Clients;

public static class StrategyNotes
{
    public const string PerRequest =
        "Educational anti-pattern for a long-running service: this call created a new HttpClient and a new handler, then disposed both. Each handler owns a separate connection pool, so the pool is discarded with the handler. Sustained churn can leave sockets in TIME_WAIT and can exhaust ephemeral ports. A short local run is not evidence that exhaustion will or will not happen.";

    public const string Factory =
        "IHttpClientFactory issued a new HttpClient wrapper for this call and pooled the handler underneath it. CreateClient does not return one shared HttpClient instance. HandlerLifetime rotates the pooled handler for later wrappers. A wrapper created earlier keeps the handler it already holds.";

    public const string LongLived =
        "This call used the process-wide HttpClient and its SocketsHttpHandler. The application owns that pair and disposes them at shutdown. PooledConnectionLifetime limits how long a pooled connection stays reusable. It does not abort a request that is already running.";

    public const string Named =
        "Named clients are another IHttpClientFactory registration. Each CreateClient call returns a new wrapper. Wrappers created while the current handler is still active share that handler and its connection pool. This registration has its own pool, separate from the typed client.";

    public const string DnsSimulation =
        "Local endpoint-directory simulation. The request URI stays on the logical host inventory.local. ConnectCallback dials the directory's current IP when a new TCP connection is established. A pooled connection keeps the peer it already has. This does not call operating-system DNS and does not watch DNS TTL.";
}
