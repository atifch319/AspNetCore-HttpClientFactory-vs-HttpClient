# Production tradeoffs

IHttpClientFactory is usually a good fit for a dependency-injected ASP.NET Core application. A long-lived `HttpClient` with `SocketsHttpHandler.PooledConnectionLifetime` is also a valid production option. The per-request handler in this repo is the pattern a long-running service should not use as its default.

| Approach | Ownership | When it fits |
|---|---|---|
| `IHttpClientFactory` typed or named client | The factory owns handlers. Your scope owns the short-lived wrapper | DI applications, named integrations, handler rotation |
| Long-lived `HttpClient` | Your singleton owns one handler and disposes it at shutdown | A process that wants an explicit pool and a bounded connection lifetime |
| New `HttpClient` for a few calls | The caller owns a handler that dies with the process or with that batch | Short-lived tools |
| New `HttpClient` and handler on every request of a running server | Each call owns a pool that is thrown away | Educational comparison in this repo |

## Settings that are easy to mix up

`HttpClient.Timeout` is the client's own deadline. It cancels the request even when the caller's `CancellationToken` is not canceled. `InventoryHttp` reports that case as `InventoryTimeoutException`, which the order API surfaces as HTTP 504. A canceled caller token propagates as `OperationCanceledException`.

`HandlerLifetime` rotates factory pipelines. `PooledConnectionLifetime` retires pooled connections inside one handler. Setting one does not set the other. The factory registrations in this repo set both: the factory rotates handlers every 120 seconds, and each `SocketsHttpHandler` also has a 120 second pooled connection lifetime.

## Base address

`BaseAddressRules` requires an absolute `http` or `https` URI and appends a trailing slash when it is missing. Relative paths in `InventoryRoutes` do not start with `/`.

For a base of `http://127.0.0.1:5080/api/`, the relative URI `inventory/WIDGET-1` becomes `http://127.0.0.1:5080/api/inventory/WIDGET-1`. The relative URI `/inventory/WIDGET-1` becomes `http://127.0.0.1:5080/inventory/WIDGET-1`. This sample's base is the site root, and the relative URI includes `api/`.

## HTTP results

A non-success status code is a failed inventory call. The clients read the body and throw `InventoryCallException` with the status, the body, the request URI, and the handler instance id when the pipeline stamped one. Missing SKUs become HTTP 404 on the order API. Insufficient stock becomes HTTP 409. Other downstream failures become HTTP 502. Connection failures also become HTTP 502.

## Disposal

- Per request: the call disposes the wrapper and the handler, including when the status code is an error.
- Factory: callers may dispose the wrapper. The order API's named probe does. Disposal does not drop the pool.
- Long-lived and DNS simulation: the singletons implement `IDisposable`. The host disposes them. Request code does not.

## Cookies, headers, and secrets

Cookie behavior is documented in [HandlerPooling.md](HandlerPooling.md). The comparison clients disable cookies so the three strategies are not coupled through a jar.

No secrets, machine names, or developer-specific hostnames are in configuration. Local URLs use `127.0.0.1`. The compose file uses the service name `inventory`, which exists only on that compose network.

## What this sample does not tune

There is no retry policy, no circuit breaker, and no published performance ranking. Add those in the application that has the operational data. [BenchmarkMethodology.md](BenchmarkMethodology.md) describes how to collect a local measurement without treating it as a portable result.
