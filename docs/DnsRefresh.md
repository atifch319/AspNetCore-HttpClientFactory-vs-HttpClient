# DNS and the endpoint-directory simulation

`HttpClient` and `SocketsHttpHandler` resolve a host when a new connection is established. They do not watch DNS TTL, and they do not poll a resolver in the background. A connection that is already in the pool keeps the peer it connected to. A later connection observes the DNS answer that exists at connect time.

`PooledConnectionLifetime` is the control that makes "later" arrive. When a pooled connection is older than that value, it is not selected for a new request. The request that needs a connection opens one, and that connect is when DNS runs. If the lifetime elapses while a request is already reading or writing, that request is not aborted. `Pooled_connection_lifetime_does_not_abort_an_active_request` holds a response on the server, waits past the lifetime, and then releases the response.

A long-lived client with a bounded `PooledConnectionLifetime` picks up endpoint changes on connections opened after that bound. That is a valid production configuration. The stale peer, if any, is the connection that has not yet been replaced.

`IHttpClientFactory` reaches a similar outcome by a different route. `HandlerLifetime` retires a handler pipeline. The next pipeline has a new pool, so its connections are new, and new connections resolve DNS again. An old wrapper still holding the retired pipeline keeps that pipeline's pool.

## What this repo simulates

`DnsSimulationClient` does not call the operating-system resolver.

- The request URI uses the hostname `inventory.local`. That name is only the pool key.
- `SocketsHttpHandler.ConnectCallback` runs when a new connection is required. The callback reads `EndpointDirectory` and dials that IP and port.
- The directory accepts absolute `http` URIs whose host is an IP address and whose port is explicit. A hostname is rejected so this path cannot be mistaken for real DNS.
- `GET /api/demo/dns/endpoint` and `PUT /api/demo/dns/endpoint` read and replace the directory.
- `GET /api/demo/dns/stock/{sku}` performs one simulated call.

`PooledConnectionLifetime` for this client comes from `DnsSimulation:PooledConnectionLifetimeSeconds`. The checked-in value is 10 seconds so a local walkthrough finishes promptly. That number is a demo setting, not a production recommendation. The comparison client's production-style value is `Inventory:PooledConnectionLifetimeSeconds`, which is 120.

The automated test uses two loopback TCP servers and a two-second lifetime:

1. The first call connects to server A.
2. The directory is pointed at server B.
3. The next call is still served by A, and B has accepted no connection.
4. A later call, after the pooled connection is no longer reusable, is served by B.

Nothing in that test waits on public DNS, a TTL, or a resolver cache.

## Manual walkthrough

Start two inventory processes and the order API, using the commands in the README. Then:

```powershell
Invoke-RestMethod http://127.0.0.1:5070/api/demo/dns/stock/WIDGET-1
Invoke-RestMethod -Method Put http://127.0.0.1:5070/api/demo/dns/endpoint -ContentType application/json -Body '{"baseAddress":"http://127.0.0.1:5081/"}'
Invoke-RestMethod http://127.0.0.1:5070/api/demo/dns/stock/WIDGET-1
```

The second stock call is expected to report `inventory-a` when it reuses the pooled connection. Call the stock endpoint again after the simulation lifetime. That later call is expected to report `inventory-b`, because it is served by a new connection. The sample does not claim the change occurs at an exact millisecond.

Docker Compose leaves `DnsSimulation:Enabled` false. The compose network name `inventory` is real DNS, and this simulation only dials IP addresses.

## Active requests

Do not send a second request while you are trying to observe an in-flight call. On HTTP/1.1 a busy connection makes the pool open another connection, and that new connection would read the directory immediately. The lifetime test waits on the single in-flight call, then releases the server. The call completes with the original instance id.
