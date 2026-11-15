# Client, handler, and connection lifetimes

IHttpClientFactory manages handlers. Connection reuse is what matters.

These are different clocks. Keeping them separate is the whole point of the sample.

| Concept | What it is | What ends it in this sample |
|---|---|---|
| HttpClient wrapper | The object you call `SendAsync` on | Disposal of that object, or the end of the process |
| HttpMessageHandler | The pipeline that owns cookies, pools, and connection setup | Disposal of the handler |
| TCP connection | One socket inside a handler's pool | Idle timeout, `PooledConnectionLifetime`, server close, or handler disposal |
| `IHttpClientFactory` `HandlerLifetime` | How long the factory will keep issuing the current handler pipeline to new wrappers | A later `CreateClient` observes a new pipeline. An existing wrapper keeps the pipeline it already holds |
| `SocketsHttpHandler.PooledConnectionLifetime` | How long a pooled connection stays eligible for another request | The connection is left out of later reuse. A request already using it runs to completion |

## 1. New HttpClient and handler per request

`PerRequestInventoryClient` is an educational anti-pattern for a long-running service.

Each outgoing call constructs a new `HttpClient` and a new `SocketsHttpHandler`, then disposes both. Disposing the handler disposes that handler's connection pool. The next call starts from an empty pool, so the service opens a new TCP connection even when the previous call targeted the same origin.

Independent handlers generally mean independent pools. Repeating that on every request creates connection churn. Closed sockets can sit in `TIME_WAIT`, and a sustained rate can exhaust the machine's ephemeral ports. A short local run, including the tests in this repo, is not evidence that exhaustion will happen and is not evidence that it cannot happen.

`PooledConnectionLifetime` on that per-request handler does not create reuse across calls. The handler does not live long enough for the pool to be used again.

`new HttpClient()` is a reasonable choice in other shapes:

- A short-lived process that makes a handful of calls and then exits.
- One long-lived `HttpClient` that owns one handler for the life of the application. That is strategy 3.

The problem is the per-request disposal in a process that keeps running.

## 2. IHttpClientFactory

`FactoryInventoryClient` is a typed client. `NamedInventoryGateway` is a named client. Both are registered with `AddHttpClient`.

`CreateClient` returns a new `HttpClient` wrapper on every call. Two wrappers created while the current pipeline is active share that pipeline and its pool. They are not the same object, and disposing a wrapper does not dispose the pooled handler. The factory created the handler with `disposeHandler: false`.

`HandlerLifetime` in `appsettings.json` is 120 seconds. When that age is reached, the next wrapper gets a newly built pipeline. A wrapper that is still alive keeps the previous pipeline. Rotation does not reach into an object you are already holding. The factory disposes an expired pipeline after the wrappers that reference it have been released.

Each `AddHttpClient` registration has its own handler pipeline. The typed client and the named client do not share a pool with each other, and neither shares a pool with the long-lived client.

Inject the typed client into a scoped or transient service. `OrderService` is scoped for that reason. A singleton that stores the typed client keeps one wrapper, and that wrapper keeps one handler, so later rotations do not apply to it. `FactoryLifetimeTests` demonstrates that capture.

This is usually the right fit for an ASP.NET Core app: DI creates the wrapper at the right scope, and the factory pools the handlers.

## 3. Long-lived HttpClient

`LongLivedInventoryClient` is also a valid production option. The type is a singleton. Its constructor creates one `SocketsHttpHandler` and one `HttpClient`. The DI container disposes that pair when the host shuts down. Request code must not dispose it.

`SocketsHttpHandler.PooledConnectionLifetime` is 120 seconds in the sample configuration. That value limits how long a connection in the pool remains eligible for another request. When the lifetime elapses during an active request, that request keeps its socket and finishes. The next request that needs a connection can open a new one.

DNS is resolved when a connection is established. This client does not subscribe to DNS TTL. A bounded `PooledConnectionLifetime` is what causes later connections to observe a newer DNS result. A correctly configured long-lived client is a normal production design, not an incomplete version of the factory.

## What the sample forces

All three comparison clients set `HttpClient.DefaultRequestVersion` to HTTP/1.1 and `HttpVersionPolicy.RequestVersionExact` once, at construction. The loopback tests can then see keep-alive reuse as TCP accepts. Production servers may negotiate HTTP/2, where several requests share fewer connections. Pooling, handler ownership, and "DNS happens when a connection is opened" still apply.

Request-specific values, such as `X-Correlation-Id`, are set on `HttpRequestMessage`. They are not added to `DefaultRequestHeaders`. That collection is shared by every concurrent caller of a shared client, and it is not safe to mutate per request.
